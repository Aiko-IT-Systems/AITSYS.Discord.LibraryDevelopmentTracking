// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;

using AITSYS.Discord.LibraryDevelopmentTracking.Commands;
using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Helpers;
using AITSYS.Discord.LibraryDevelopmentTracking.Rest;

using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Enums;
using DisCatSharp.Interactivity.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

public sealed class DiscordBot
{
	internal static Config Configuration { get; private set; }

	internal static NotionRestClient NotionRestClient { get; private set; }

	internal DiscordClient DiscordClient { get; private set; }

	internal ApplicationCommandsExtension ApplicationCommandsExtension { get; private set; }

	internal InteractivityExtension InteractivityExtension { get; private set; }

	internal static CancellationTokenSource Shutdown { get; } = new();

	internal WebApplication WebApplication { get; private set; }

	public DiscordBot(Config config, bool useProxy = false)
	{
		ArgumentNullException.ThrowIfNull(config);
		Configuration = config;
		var proxy = useProxy ? new WebProxy("127.0.0.1", 8000) : null;
		NotionRestClient = new NotionRestClient(Configuration.NotionConfig, proxy);
		this.DiscordClient = new DiscordClient(new DiscordConfiguration()
		{
			Token = Configuration.DiscordConfig.DiscordToken,
			TokenType = TokenType.Bot,
			Intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers,
			Api =
			{
				Channel = ApiChannel.Canary
			},
			Diagnostics =
			{
				UpdateChecks = new()
				{
					Disabled = true
				}
			},
			Gateway =
			{
				AutoReconnect = true,
				ReconnectIndefinitely = true
			},
			Logging =
			{
				MinimumLogLevel = LogLevel.Debug
			},
			Telemetry =
			{
				EnableSentry = false
			},
			Proxy = proxy,
			HasActivitiesEnabled = true
		});
		this.ApplicationCommandsExtension = this.DiscordClient.UseApplicationCommands(new()
		{
			EnableDefaultHelp = false,
			EnableDefaultUserAppsHelp = true,
			DebugStartup = true,
			CheckAllGuilds = false,
			EnableLocalization = false
		});
		this.InteractivityExtension = this.DiscordClient.UseInteractivity(new()
		{
			PaginationBehaviour = PaginationBehaviour.WrapAround,
			PaginationDeletion = PaginationDeletion.DeleteMessage,
			PollBehaviour = PollBehaviour.DeleteEmojis,
			AckPaginationButtons = true,
			Timeout = TimeSpan.FromMinutes(5)
		});
		this.Setup(config);
	}

	public void Setup(Config config)
	{
		this.DiscordClient.Ready += async (client, args) => _ = await client.Guilds[Configuration.DiscordConfig.DiscordGuild].GetAllMembersAsync();
		this.ApplicationCommandsExtension.RegisterGlobalCommands<LibraryTrackingCommands>();
		this.ApplicationCommandsExtension.RegisterGuildCommands<LibraryHouseKeepingCommands>(1317206872763404478);
		foreach (var guild in config.DiscordConfig.DiscordGuilds)
			this.ApplicationCommandsExtension.RegisterGuildCommands<LibraryHouseKeepingCommands>(guild);
		this.ApplicationCommandsExtension.RegisterEntryPointCommand("View Library Statuses", [InteractionContextType.BotDm, InteractionContextType.Guild, InteractionContextType.PrivateChannel], [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall]);
	}

	public async Task StartAsync()
	{
		await this.DiscordClient.ConnectAsync();
		await this.RunServerAsync(Configuration);
		while (!Shutdown.IsCancellationRequested)
		{
			await Task.Delay(1000);
		}
		await this.WebApplication.StopAsync();
		await this.DiscordClient.DisconnectAsync();
	}

	private async Task RunServerAsync(Config config)
	{
		var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
		var assetVersion = GetAssetVersion(webRoot);

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ContentRootPath = AppContext.BaseDirectory,
			WebRootPath = webRoot
		});
		builder.WebHost.UseUrls($"http://0.0.0.0:{config.DiscordConfig.Port}");
		builder.Services.AddSingleton(config);
		builder.Services.AddSingleton(NotionRestClient);
		builder.Services.AddMemoryCache();
		builder.Services.AddHttpClient();
		builder.Services.AddSingleton(new ActivityBootState(Guid.NewGuid().ToString("N")));
		builder.Services.AddSingleton(this.DiscordClient);
		builder.Services.AddSingleton<ActivitySessionStore>();
		builder.Services.AddSingleton<ActivityAuthService>();
		builder.Services.AddSingleton<ActivityTrackingService>();
		builder.Services.AddSingleton<ActivityShareService>();
		builder.Services.ConfigureHttpJsonOptions(o =>
		{
			o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
			o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
		});
		builder.Services.Configure<ForwardedHeadersOptions>(o =>
		{
			o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			o.KnownIPNetworks.Clear();
			o.KnownProxies.Clear();
		});

		this.WebApplication = builder.Build();
		_ = this.WebApplication.Services.GetRequiredService<DiscordClient>();
		var authService = this.WebApplication.Services.GetRequiredService<ActivityAuthService>();
		var bootState = this.WebApplication.Services.GetRequiredService<ActivityBootState>();
		this.WebApplication.Logger.LogInformation("Initialized activity services.");

		this.WebApplication.UseForwardedHeaders();
		this.WebApplication.Use(async (context, next) =>
		{
			EnsureBootMarker(context, bootState);
			await next();
		});
		this.WebApplication.Use(async (context, next) =>
		{
			var (isAllowed, hostFailureReason) = await HostAllowlist.IsAllowedAsync(context.Request, config, authService);
			Console.WriteLine($"Attempted auth for activity. Result: {isAllowed} ({hostFailureReason})");
			if (!IsAlwaysAnonymousStaticPath(context.Request.Path) && !isAllowed)
			{
				this.WebApplication.Logger.LogWarning("Rejected host for {Path}: {Reason}", context.Request.Path, hostFailureReason);
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
				{
					await context.Response.WriteAsJsonAsync(new { message = "Access denied." });
				}
				else
				{
					await WriteErrorPageAsync(context, StatusCodes.Status403Forbidden, "Access denied", "You are not allowed to use the activity.");
				}
				return;
			}

			await next();
		});
		this.WebApplication.Use(async (context, next) =>
		{
			Console.WriteLine("Checking paths and request");
			var anonymousPath = IsAlwaysAnonymousStaticPath(context.Request.Path);
			var shouldValidate = DiscordProxyAuthentication.ShouldValidate(context.Request, config);

			if (!anonymousPath && shouldValidate && !DiscordProxyAuthentication.ValidateProxyRequest(context.Request, config, out var failureReason))
			{
				this.WebApplication.Logger.LogWarning("Rejected Discord proxy request for {Path}: {Reason}", context.Request.Path, failureReason);
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
				{
					await context.Response.WriteAsJsonAsync(new { message = "Invalid Discord proxy authentication." });
				}
				else
				{
					await WriteErrorPageAsync(context, StatusCodes.Status401Unauthorized, "Access denied", "You are not allowed to use the activity.");
				}
				return;
			}
			else
			{
				if (!anonymousPath && shouldValidate)
					this.WebApplication.Logger.LogInformation("Validated Discord proxy request for {Path}", context.Request.Path);
				else if (anonymousPath)
					this.WebApplication.Logger.LogInformation("Allowing anonymous static path for {Path}", context.Request.Path);
				else
					this.WebApplication.Logger.LogInformation("Allowing non-anonymous path for {Path} without validation. This should not happen!", context.Request.Path);
			}

			await next();
		});
		this.WebApplication.Use(async (context, next) =>
		{
			if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) || IsAnonymousApiPath(context.Request.Path))
			{
				await next();
				return;
			}

			if (authService.IsLocalDevAllowed(context.Request))
			{
				context.Items[typeof(SessionResponse)] = BuildLocalDevSession();
				await next();
				return;
			}

			var sessionStore = context.RequestServices.GetRequiredService<ActivitySessionStore>();
			var sessionCookie = context.Request.Cookies[ActivityAuthService.SessionCookieName];
			var session = sessionStore.GetSession(sessionCookie);
			if (session is null)
			{
				ExpireSessionCookieIfPresent(context, sessionCookie);
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				await context.Response.WriteAsJsonAsync(new { message = "Authentication required." });
				return;
			}

			var refreshedAuthorization = await authService.ReauthorizeAsync(session);
			if (!refreshedAuthorization.IsAuthorized)
			{
				sessionStore.RemoveSession(session.SessionId);
				ExpireSessionCookieIfPresent(context, sessionCookie);
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				await context.Response.WriteAsJsonAsync(new { message = "Your activity access has been revoked." });
				return;
			}

			context.Items[typeof(ActivitySession)] = session;
			context.Items[typeof(SessionResponse)] = session.ToResponse(refreshedAuthorization);
			await next();
		});
		this.WebApplication.Use(async (context, next) =>
		{
			if (context.Request.Method == HttpMethods.Get
				&& (context.Request.Path == "/"
					|| string.Equals(context.Request.Path.Value, "/index.html", StringComparison.OrdinalIgnoreCase)))
			{
				var indexPath = Path.Combine(webRoot, "index.html");
				if (!File.Exists(indexPath))
				{
					context.Response.StatusCode = StatusCodes.Status404NotFound;
					return;
				}

				var html = await File.ReadAllTextAsync(indexPath);
				html = html.Replace("__ASSET_VERSION__", assetVersion, StringComparison.Ordinal);
				context.Response.ContentType = "text/html; charset=utf-8";
				context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
				context.Response.Headers.Pragma = "no-cache";
				context.Response.Headers.Expires = "0";
				await context.Response.WriteAsync(html);
				return;
			}

			await next();
		});
		this.WebApplication.UseDefaultFiles();
		this.WebApplication.UseStaticFiles(new StaticFileOptions
		{
			OnPrepareResponse = context =>
			{
				var headers = context.Context.Response.Headers;
				headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
				headers.Pragma = "no-cache";
				headers.Expires = "0";
			}
		});

		this.WebApplication.MapGet("/api/auth/config", async (HttpContext context, ActivityAuthService auth) =>
			Results.Json(await auth.GetAuthConfigAsync(context.Request)));

		this.WebApplication.MapGet("/api/auth/session", async (HttpContext context, ActivityAuthService auth, ActivitySessionStore sessions) =>
		{
			if (auth.IsLocalDevAllowed(context.Request))
				return Results.Json(BuildLocalDevSession());

			var requestedChannelId = context.Request.Query["channel_id"].ToString();
			var sessionCookie = context.Request.Cookies[ActivityAuthService.SessionCookieName];
			var session = sessions.GetSession(sessionCookie);
			if (session is null)
				ExpireSessionCookieIfPresent(context, sessionCookie);

			if (session is not null)
			{
				var instanceId = context.Request.Query["instance_id"].ToString();
				if (!string.IsNullOrWhiteSpace(instanceId)
					&& !string.Equals(session.LaunchContext?.InstanceId, instanceId, StringComparison.Ordinal))
				{
					try
					{
						var refreshedLaunchContext = await auth.ResolveLaunchContextAsync(requestedChannelId, instanceId, session.UserId);
						session = sessions.UpdateLaunchContext(session.SessionId, refreshedLaunchContext) ?? session;
					}
					catch (UnauthorizedAccessException ex)
					{
						this.WebApplication.Logger.LogWarning(ex, "Failed to rebind activity session {SessionId} for user {UserId} to instance {InstanceId}.", session.SessionId, session.UserId, instanceId);
						return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
					}
				}

				var refreshedAuthorization = await auth.ReauthorizeAsync(session);
				if (!refreshedAuthorization.IsAuthorized)
				{
					sessions.RemoveSession(session.SessionId);
					ExpireSessionCookieIfPresent(context, sessionCookie);
					return Results.Json(new { message = "Your activity access has been revoked." }, statusCode: StatusCodes.Status403Forbidden);
				}

				return Results.Json(session.ToResponse(refreshedAuthorization));
			}

			return session is null
				? Results.Json(new { message = "No active session." }, statusCode: StatusCodes.Status401Unauthorized)
				: Results.Json(session.ToResponse());
		});

		this.WebApplication.MapPost("/api/auth/access-token", async (HttpContext context, ActivityAuthService auth, ActivitySessionStore sessions) =>
		{
			if (auth.IsLocalDevAllowed(context.Request))
				return Results.BadRequest(new { message = "Discord access tokens are unavailable in local development mode." });

			var sessionCookie = context.Request.Cookies[ActivityAuthService.SessionCookieName];
			var session = sessions.GetSession(sessionCookie);
			if (session is null)
			{
				ExpireSessionCookieIfPresent(context, sessionCookie);
				return Results.Json(new { message = "Authentication required." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			var forceRefresh = string.Equals(
				context.Request.Query["forceRefresh"],
				bool.TrueString,
				StringComparison.OrdinalIgnoreCase);

			try
			{
				var token = await auth.GetValidAccessTokenAsync(sessions, session, forceRefresh);
				return Results.Json(new AccessTokenResponse(token.AccessToken, token.ExpiresAt));
			}
			catch (Exception ex)
			{
				this.WebApplication.Logger.LogWarning(ex, "Failed to obtain Discord access token for activity user {UserId}.", session.UserId);
				sessions.RemoveSession(session.SessionId);
				ExpireSessionCookieIfPresent(context, sessionCookie);
				return Results.Json(new { message = "Discord authentication expired. Please authenticate again." }, statusCode: StatusCodes.Status401Unauthorized);
			}
		});

		this.WebApplication.MapPost("/api/auth/exchange", async (HttpContext context, AuthExchangeRequest payload, ActivityAuthService auth, ActivitySessionStore sessions) =>
		{
			if (auth.IsLocalDevAllowed(context.Request))
				return Results.Json(new AuthExchangeResponse(BuildLocalDevSession()));

			if (payload is null || string.IsNullOrWhiteSpace(payload.Code))
				return Results.BadRequest(new { message = "OAuth code is required." });
			if (string.IsNullOrWhiteSpace(config.DiscordConfig.DiscordClientSecret))
				return Results.Problem("activity.discord_client_secret is not configured.", statusCode: StatusCodes.Status500InternalServerError);

			try
			{
				var exchange = await auth.ExchangeCodeAsync(payload.Code, payload.InstanceId, payload.ChannelId);
				var ttl = TimeSpan.FromMinutes(Math.Max(5, config.DiscordConfig.SessionTtlMinutes));
				var session = sessions.CreateSession(exchange.User, exchange.Authorization, exchange.Guilds.Select(g => g.Id), exchange.Token, ttl, exchange.LaunchContext);
				var sessionResponse = session.ToResponse();
				context.Response.Cookies.Append(ActivityAuthService.SessionCookieName, session.SessionId, CreateSessionCookieOptions(ttl));
				return Results.Json(new AuthExchangeResponse(sessionResponse));
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
			}
			catch (BadRequestException ex)
			{
				this.WebApplication.Logger.LogWarning(ex, "Failed to exchange Discord OAuth code for instance {InstanceId}: {response}", payload.InstanceId, ex.JsonMessage);
				return Results.Json(new { message = "Discord authentication failed." }, statusCode: StatusCodes.Status400BadRequest);
			}
			catch (Exception ex)
			{
				this.WebApplication.Logger.LogError(ex, "Failed to exchange Discord activity auth code.");
				return Results.Json(new { message = "Discord authentication failed." }, statusCode: StatusCodes.Status401Unauthorized);
			}
		});

		this.WebApplication.MapGet("/api/tracking/notions", (ActivityTrackingService tracking) =>
			Results.Json(tracking.GetTrackedNotions()));

		this.WebApplication.MapGet("/api/tracking/notions/{pageId}", async (string pageId, ActivityTrackingService tracking, ILogger<DiscordBot> logger, bool refresh = false) =>
		{
			try
			{
				var notion = await tracking.GetTrackedNotionAsync(pageId, refresh);
				return notion is null
					? Results.NotFound(new { message = "The requested notion is not configured for tracking." })
					: Results.Json(notion);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to build read-only Activity statistics for configured notion {PageId}.", pageId);
				return Results.Problem("The tracking data could not be loaded right now.", statusCode: StatusCodes.Status502BadGateway);
			}
		});

		this.WebApplication.MapPost("/api/tracking/notions/{pageId}/quick-link", async (
			HttpContext context,
			string pageId,
			ActivityTrackingService tracking,
			ActivityShareService sharing,
			ActivityAuthService auth,
			ActivitySessionStore sessions,
			ILogger<DiscordBot> logger,
			CancellationToken cancellationToken) =>
		{
			if (auth.IsLocalDevAllowed(context.Request))
				return Results.NoContent();

			if (context.Items[typeof(ActivitySession)] is not ActivitySession session)
				return Results.Json(new { message = "Authentication required." }, statusCode: StatusCodes.Status401Unauthorized);

			try
			{
				var notion = await tracking.GetTrackedNotionAsync(pageId);
				if (notion is null)
					return Results.NotFound(new { message = "The requested notion is not configured for tracking." });

				var token = await auth.GetValidAccessTokenAsync(sessions, session);
				await sharing.TryProvisionQuickLinkAsync(notion, token.AccessToken, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// Custom links still route by custom_id without their optional Discord preview.
				logger.LogDebug(ex, "Best-effort quick-link provisioning failed for notion {PageId}.", pageId);
			}

			return Results.NoContent();
		});

		this.WebApplication.MapPost("/api/tracking/notions/{pageId}/share-image", async (
			HttpContext context,
			string pageId,
			IFormFile? image,
			ActivityTrackingService tracking,
			ActivityShareService sharing,
			ActivityAuthService auth,
			ActivitySessionStore sessions,
			ILogger<DiscordBot> logger,
			CancellationToken cancellationToken) =>
		{
			if (auth.IsLocalDevAllowed(context.Request))
				return Results.BadRequest(new { message = "Chart sharing is only available inside Discord." });

			if (context.Items[typeof(ActivitySession)] is not ActivitySession session)
				return Results.Json(new { message = "Authentication required." }, statusCode: StatusCodes.Status401Unauthorized);

			if (tracking.GetCustomLinkId(pageId) is null)
				return Results.NotFound(new { message = "The requested notion is not configured for tracking." });

			if (image is null || image.Length == 0)
				return Results.BadRequest(new { message = "A PNG chart image is required." });
			if (image.Length > 8 * 1024 * 1024)
				return Results.BadRequest(new { message = "The chart image must be 8 MB or smaller." });
			if (!string.Equals(image.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
				return Results.BadRequest(new { message = "Only PNG chart images can be shared." });

			try
			{
				var token = await auth.GetValidAccessTokenAsync(sessions, session);
				await using var stream = image.OpenReadStream();
				var fileName = $"{SanitizeActivityFileName(pageId)}-statistics.png";
				var mediaUrl = await sharing.CreateActivityAttachmentAsync(token.AccessToken, stream, fileName, cancellationToken);
				return Results.Json(new ActivityShareImageResponse(mediaUrl));
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to upload shared chart image for notion {PageId} and activity user {UserId}.", pageId, session.UserId);
				return Results.Problem("Discord could not prepare this chart for sharing.", statusCode: StatusCodes.Status502BadGateway);
			}
		}).DisableAntiforgery();

		this.WebApplication.MapPost("/api/auth/logout", (HttpContext context, ActivitySessionStore sessions) =>
		{
			sessions.RemoveSession(context.Request.Cookies[ActivityAuthService.SessionCookieName]);
			context.Response.Cookies.Delete(ActivityAuthService.SessionCookieName, CreateSessionCookieOptions(TimeSpan.Zero));
			return Results.Ok();
		});

		Console.WriteLine($"Serving activity UI at http://localhost:{config.DiscordConfig.Port}\nCtrl+C to stop.");
		await this.WebApplication.RunAsync();
	}

	private static bool IsAnonymousApiPath(PathString path)
		=> path.StartsWithSegments("/api/auth/config", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWithSegments("/api/auth/exchange", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWithSegments("/api/auth/session", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWithSegments("/api/auth/logout", StringComparison.OrdinalIgnoreCase);

	private static bool IsAlwaysAnonymousStaticPath(PathString path)
		=> path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
			|| path.Equals("/discord.png", StringComparison.OrdinalIgnoreCase);

	private static CookieOptions CreateSessionCookieOptions(TimeSpan ttl)
		=> new()
		{
			HttpOnly = true,
			Secure = true,
			SameSite = SameSiteMode.None,
			Path = "/",
			Expires = ttl <= TimeSpan.Zero ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.Add(ttl)
		};

	private static SessionResponse BuildLocalDevSession()
		=> new(
			new ViewerIdentity("0", "Local Dev", "Local Development", null),
			new AuthorizationSnapshot(true, false, true, false, false),
			true,
			null,
			null);

	private static void ExpireSessionCookieIfPresent(HttpContext context, string? sessionCookie)
	{
		if (string.IsNullOrWhiteSpace(sessionCookie))
			return;

		context.Response.Cookies.Delete(
			ActivityAuthService.SessionCookieName,
			CreateSessionCookieOptions(TimeSpan.Zero));
	}

	private static void EnsureBootMarker(HttpContext context, ActivityBootState bootState)
	{
		var currentMarker = context.Request.Cookies[ActivityBootState.CookieName];
		if (!string.Equals(currentMarker, bootState.BootId, StringComparison.Ordinal))
		{
			ExpireSessionCookieIfPresent(
				context,
				context.Request.Cookies[ActivityAuthService.SessionCookieName]);
		}

		context.Response.Cookies.Append(
			ActivityBootState.CookieName,
			bootState.BootId,
			CreateSessionCookieOptions(TimeSpan.FromDays(30)));
	}

	private static string GetAssetVersion(string webRoot)
	{
		var appJsPath = Path.Combine(webRoot, "WebApplication.js");
		return File.Exists(appJsPath) ? File.GetLastWriteTimeUtc(appJsPath).Ticks.ToString() : DateTimeOffset.UtcNow.Ticks.ToString();
	}

	private static string SanitizeActivityFileName(string value)
		=> new([.. value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')]);

	private static Task WriteErrorPageAsync(HttpContext context, int statusCode, string title, string message)
	{
		context.Response.StatusCode = statusCode;
		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
		context.Response.Headers.Pragma = "no-cache";
		context.Response.Headers.Expires = "0";

		var encodedTitle = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(title);
		var encodedMessage = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);
		var html = $$"""
			<!DOCTYPE html>
			<html lang="en">
			<head>
				<meta charset="UTF-8" />
				<meta name="viewport" content="width=device-width, initial-scale=1.0" />
				<title>{{encodedTitle}} · Library Tracking</title>
				<style>
					:root {
						color-scheme: dark;
						font-family: Inter, "Segoe UI", system-ui, sans-serif;
					}
					* { box-sizing: border-box; }
					body {
						margin: 0;
						min-height: 100vh;
						display: flex;
						align-items: center;
						justify-content: center;
						padding: 24px;
						background: #2b2d31;
						color: #f2f3f5;
					}
					.card {
						width: min(500px, 100%);
						padding: 32px;
						background: #313338;
						border: 1px solid #3f4147;
						border-radius: 12px;
						box-shadow: 0 12px 40px rgba(0, 0, 0, 0.28);
					}
					.brand {
						display: flex;
						align-items: center;
						gap: 12px;
						margin-bottom: 28px;
					}
					.brand img {
						width: 40px;
						height: 40px;
						border-radius: 10px;
					}
					.brand-copy {
						display: grid;
						gap: 2px;
					}
					.brand strong { font-size: 15px; }
					.brand span {
						color: #949ba4;
						font-size: 13px;
					}
					.status {
						display: inline-flex;
						align-items: center;
						padding: 5px 9px;
						border-radius: 999px;
						color: #f0b232;
						background: rgba(240, 178, 50, 0.12);
						font-size: 12px;
						font-weight: 700;
						letter-spacing: 0.04em;
						text-transform: uppercase;
					}
					h1 {
						margin: 14px 0 10px;
						font-size: clamp(26px, 7vw, 32px);
						line-height: 1.15;
					}
					p {
						margin: 0;
						color: #b5bac1;
						font-size: 16px;
						line-height: 1.6;
					}
				</style>
			</head>
			<body>
				<div class="card">
					<div class="brand">
						<img src="/discord.png" alt="" />
						<div class="brand-copy">
							<strong>Library Tracking</strong>
							<span>Discord Developer Libraries</span>
						</div>
					</div>
					<div class="status">{{statusCode}}</div>
					<h1>{{encodedTitle}}</h1>
					<p>{{encodedMessage}}</p>
				</div>
			</body>
			</html>
			""";

		return context.Response.WriteAsync(html);
	}


}
