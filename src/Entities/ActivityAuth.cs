// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Text.Json;

using DisCatSharp;
using DisCatSharp.Entities;
using DisCatSharp.Entities.OAuth2;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities;

internal sealed record ActivityBootState(string BootId)
{
	internal const string CookieName = "lib_dev_tracking_dash_boot";
}

internal sealed class ActivityAuthService(DiscordClient discordClient, Config config, ILogger<ActivityAuthService> logger, ILoggerFactory loggerFactory)
{
	internal const string SessionCookieName = "lib_dev_tracking_dash_session";

	private const string ActivityRedirectUri = "https://127.0.0.1";

	private static readonly TimeSpan s_applicationCacheLifetime = TimeSpan.FromMinutes(10);

	private readonly DiscordClient _discordClient = discordClient;

	private readonly Config _config = config;

	private readonly ILogger<ActivityAuthService> _logger = logger;

	private readonly ILoggerFactory _loggerFactory = loggerFactory;

	private readonly Lock _gate = new();

	private CachedApplicationInfo? _cachedApplicationInfo;

	private DiscordOAuth2Client? _oauthClient;

	public bool IsLocalDevAllowed(HttpRequest request)
		=> this._config.DiscordConfig.AllowLocalDev
			&& HostAllowlist.IsLocalHost(request.Host.Host);

	public async Task<AuthConfigResponse> GetAuthConfigAsync(HttpRequest request)
	{
		var application = await this.GetApplicationInfoAsync();
		return new AuthConfigResponse(
			application.Id.ToString(),
			!this.IsLocalDevAllowed(request),
			this._config.DiscordConfig.AllowLocalDev,
			this.IsLocalDevAllowed(request));
	}

	public async Task<string> GetDiscordActivityHostAsync()
	{
		var application = await this.GetApplicationInfoAsync();
		return HostAllowlist.GetDiscordActivityHost(application.Id);
	}

	public async Task<AuthExchangeResult> ExchangeCodeAsync(string code, string? instanceId, string? requestedChannelId)
	{
		var application = await this.GetApplicationInfoAsync(forceRefresh: false);
		var oauthClient = this.GetOrCreateOAuthClient(application.Id);
		var token = await oauthClient.ExchangeAccessTokenAsync(code);
		var user = await oauthClient.GetCurrentUserAsync(token);
		var type = "External";
		List<string>? libraries = null;

		IReadOnlyList<DiscordGuild> guilds;
		try
		{
			guilds = [.. await oauthClient.GetCurrentUserGuildsAsync(token)];
		}
		catch (Exception ex)
		{
			this._logger.LogWarning(ex, "Failed to fetch OAuth2 guild list for user {UserId}; continuing with user/team checks only.", user.Id);
			guilds = [];
		}

		if (guilds.Count is not 0 && guilds.Any(guild => guild.Id == this._config.DiscordConfig.DiscordGuild) && this._discordClient.Guilds[this._config.DiscordConfig.DiscordGuild].TryGetMember(user.Id, out var member))
		{
			libraries = [];
			foreach (var library in this._config.DiscordConfig.LibraryRoleMapping)
				if (member.RoleIds.Contains(library.Key))
					libraries.Add(library.Value);
			if (member.RoleIds.Contains(this._config.DiscordConfig.LibraryDeveloperRoleId))
				type = "Library Developer";
			else if (member.RoleIds.Contains(this._config.DiscordConfig.BotDeveloperRoleId))
				type = "Bot Developer";
		}

		if (user.IsStaff)
			type = "Employee";
		else if (user.Id is 856780995629154305)
			type = "Admin";

		var launchContext = await this.ResolveLaunchContextAsync(requestedChannelId, instanceId, user.Id);

		var authorization = this.Authorize(application, user.Id, guilds.Select(g => g.Id));
		if (!authorization.IsAuthorized)
		{
			this._logger.LogWarning("Denied activity access for user {UserId}.", user.Id);
			throw new UnauthorizedAccessException("You are not allowed to use the activity.");
		}

		return new AuthExchangeResult(StoredDiscordAccessToken.FromDiscordToken(token), user, authorization, guilds, launchContext, type, libraries?.ToArray());
	}

	public async Task<ActivityLaunchContext?> ResolveLaunchContextAsync(string? channelId, string? instanceId, ulong userId)
	{
		if (string.IsNullOrWhiteSpace(instanceId))
			return null;

		if (instanceId is "example-cl-instance" && channelId is not null)
		{
			try
			{
				var cid = ulong.Parse(channelId ?? throw new UnauthorizedAccessException("Missing channel id for example instance."));
				var channel = await this._discordClient.GetChannelAsync(cid);
				if (channel.Type is not ChannelType.Application)
					throw new UnauthorizedAccessException("The Discord activity instance is not in an application channel.");
				this._logger.LogInformation("Resolved an application channel. Using fallback mode to verify");
				return new ActivityLaunchContext(
					instanceId,
					null,
					channel.Id.ToString(),
					channel.Type.ToString(),
					channel.GuildId,
					channel.Id);

			}
			catch (Exception ex)
			{
				this._logger.LogWarning(ex, "Failed to resolve channel {ChannelId} for user {UserId}.", channelId, userId);
				throw new UnauthorizedAccessException("The Discord activity instance could not be validated.");
			}
		}

		var instance = await this.GetActivityInstanceAsync(instanceId);
		if (instance is null)
		{
			this._logger.LogWarning("Denied activity access for user {UserId}: activity instance {InstanceId} was not found.", userId, instanceId);
			throw new UnauthorizedAccessException("The Discord activity instance is no longer valid.");
		}

		var instanceUserIds = instance.Users.Select(x => x.ToString()).ToArray();
		this._logger.LogInformation(
			"Fetched Discord activity instance {InstanceId} for user {UserId}: application={ApplicationId}, launch={LaunchId}, locationId={LocationId}, locationKind={LocationKind}, guildId={GuildId}, channelId={ChannelId}, users=[{Users}], raw={RawInstance}",
			instance.InstanceId,
			userId,
			instance.ApplicationId,
			instance.LaunchId,
			instance.Location?.Id,
			instance.Location?.Kind,
			instance.Location?.GuildId,
			instance.Location?.ChannelId,
			string.Join(", ", instanceUserIds),
			JsonSerializer.Serialize(instance));

		if (!instance.Users.Contains(userId))
		{
			this._logger.LogWarning(
				"Denied activity access for user {UserId}: activity instance {InstanceId} does not include the user. Instance users were [{Users}].",
				userId,
				instanceId,
				string.Join(", ", instanceUserIds));
			throw new UnauthorizedAccessException("You are not part of the active Discord activity instance.");
		}

		return new ActivityLaunchContext(
			instance.InstanceId?.ToString() ?? instanceId,
			instance.LaunchId,
			instance.Location?.Id?.ToString(),
			instance.Location?.Kind?.ToString(),
			instance.Location?.GuildId,
			instance.Location?.ChannelId);
	}

	public AuthorizationSnapshot Authorize(DiscordApplication application, ulong userId, IEnumerable<ulong> guildIds)
	{
		var allowedUsers = new HashSet<ulong>(this._config.DiscordConfig.AllowedUserIds);
		var allowedGuilds = new HashSet<ulong>(this._config.DiscordConfig.AllowedGuildIds);
		var teamUserIds = application.Members?.Select(x => x.Id).ToHashSet() ?? [];
		var providedGuilds = guildIds.ToHashSet();

		var viaWhitelistDisabled = !this._config.DiscordConfig.EnableWhitelist;
		var viaTeam = teamUserIds.Contains(userId);
		var viaUserAllowlist = allowedUsers.Contains(userId);
		var viaGuildAllowlist = providedGuilds.Any(allowedGuilds.Contains);

		return new AuthorizationSnapshot(viaWhitelistDisabled || viaTeam || viaUserAllowlist || viaGuildAllowlist, viaTeam, viaUserAllowlist, viaGuildAllowlist, viaWhitelistDisabled);
	}

	public async Task<AuthorizationSnapshot> ReauthorizeAsync(ActivitySession session)
	{
		var application = await this.GetApplicationInfoAsync(forceRefresh: true);
		return this.Authorize(application, session.UserId, session.GuildIds);
	}

	public async Task<StoredDiscordAccessToken> GetValidAccessTokenAsync(ActivitySessionStore sessions, ActivitySession session, bool forceRefresh = false)
	{
		if (!forceRefresh && !session.OAuthToken.ShouldRefresh())
			return session.OAuthToken;

		var application = await this.GetApplicationInfoAsync(forceRefresh: false);
		var oauthClient = this.GetOrCreateOAuthClient(application.Id);
		var refreshedToken = await oauthClient.RefreshAccessTokenAsync(session.OAuthToken.ToDiscordToken());
		var storedToken = StoredDiscordAccessToken.FromDiscordToken(refreshedToken);
		sessions.UpdateOAuthToken(session.SessionId, storedToken);
		this._logger.LogInformation("Refreshed Discord OAuth token for activity user {UserId}.", session.UserId);
		return storedToken;
	}

	public async Task<bool> CanEditLibraryAsync(ActivitySession session, string libraryName)
	{
		var user = await this._discordClient.GetUserAsync(session.UserId);
		if (user.IsStaff || session.UserId is 856780995629154305)
			return true;

		if (!this._discordClient.Guilds.TryGetValue(this._config.DiscordConfig.DiscordGuild, out var guild)
			|| !guild.Members.TryGetValue(session.UserId, out var member)
			|| !member.RoleIds.Contains(this._config.DiscordConfig.LibraryDeveloperRoleId))
			return false;

		return member.RoleIds
			.Where(this._config.DiscordConfig.LibraryRoleMapping.ContainsKey)
			.Select(roleId => this._config.DiscordConfig.LibraryRoleMapping[roleId])
			.Any(allowedLibrary => string.Equals(allowedLibrary, libraryName, StringComparison.OrdinalIgnoreCase));
	}

	private DiscordOAuth2Client GetOrCreateOAuthClient(ulong applicationId)
	{
		lock (this._gate)
		{
			if (this._oauthClient is not null && this._oauthClient.ClientId == applicationId)
				return this._oauthClient;

			this._oauthClient = new DiscordOAuth2Client(
				applicationId,
				this._config.DiscordConfig.DiscordClientSecret,
				ActivityRedirectUri,
				null!,
				null!,
				TimeSpan.FromMinutes(4),
				false,
				this._loggerFactory,
				LogLevel.Error,
				"yyyy-MM-dd HH:mm:ss zzz");
			return this._oauthClient;
		}
	}

	private async Task<DiscordApplication> GetApplicationInfoAsync(bool forceRefresh = false)
	{
		CachedApplicationInfo? cached;
		lock (this._gate)
		{
			cached = this._cachedApplicationInfo;
			if (!forceRefresh && cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
				return cached.Application;
		}

		var application = await this._discordClient.GetCurrentApplicationInfoAsync();
		lock (this._gate)
		{
			this._cachedApplicationInfo = new CachedApplicationInfo(application, DateTimeOffset.UtcNow.Add(s_applicationCacheLifetime));
		}

		return application;
	}

	private async Task<DiscordActivityInstance?> GetActivityInstanceAsync(string instanceId)
	{
		try
		{
			return await this._discordClient.GetActivityInstanceAsync(instanceId);
		}
		catch (NotFoundException)
		{
			this._logger.LogWarning("Discord activity instance lookup returned 404 for {InstanceId}.", instanceId);
			return null;
		}
		catch (Exception ex)
		{
			this._logger.LogError(ex, "Discord activity instance lookup failed for {InstanceId}.", instanceId);
			throw;
		}
	}

	private sealed record CachedApplicationInfo(DiscordApplication Application, DateTimeOffset ExpiresAt);
}

internal sealed record AuthConfigResponse(string AppId, bool ActivityRequired, bool LocalDevAllowed, bool LocalDevActive);

internal sealed record AuthExchangeRequest(string Code, string? InstanceId, string? GuildId, string? ChannelId);

internal sealed record AuthExchangeResponse(SessionResponse Session);

internal sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed record SessionResponse(
	ViewerIdentity User,
	AuthorizationSnapshot Authorization,
	bool LocalDev,
	string? ActiveGuildId,
	string? ActiveChannelId);


internal sealed record ViewerIdentity(string Id, string Username, string? DisplayName, string? AvatarHash, string Type, string[]? Libraries);

internal sealed record AuthorizationSnapshot(bool IsAuthorized, bool ViaTeam, bool ViaUserAllowlist, bool ViaGuildAllowlist, bool ViaWhitelistDisabled);

internal sealed record ActivitySession(
	string SessionId,
	ulong UserId,
	string Username,
	string? DisplayName,
	string? AvatarHash,
	AuthorizationSnapshot Authorization,
	ulong[] GuildIds,
	StoredDiscordAccessToken OAuthToken,
	ActivityLaunchContext? LaunchContext,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	string Type,
	string[]? Libraries = null)
{
	public SessionResponse ToResponse(bool localDev = false)
		=> this.BuildResponse(this.Authorization, localDev);

	public SessionResponse ToResponse(AuthorizationSnapshot authorization, bool localDev = false)
		=> this.BuildResponse(authorization, localDev);

	private SessionResponse BuildResponse(AuthorizationSnapshot authorization, bool localDev)
		=> new(
			new ViewerIdentity(this.UserId.ToString(), this.Username, this.DisplayName, this.AvatarHash, this.Type, this.Libraries),
			authorization,
			localDev,
			this.LaunchContext?.GuildId?.ToString(),
			this.LaunchContext?.ChannelId?.ToString());
}

internal sealed record AuthExchangeResult(
	StoredDiscordAccessToken Token,
	DiscordUser User,
	AuthorizationSnapshot Authorization,
	IReadOnlyList<DiscordGuild> Guilds,
	ActivityLaunchContext? LaunchContext,
	string Type,
	string[]? Libraries);

internal sealed record ActivityLaunchContext(
	string? InstanceId,
	ulong? LaunchId,
	string? LocationId,
	string? LocationKind,
	ulong? GuildId,
	ulong? ChannelId);

internal sealed record StoredDiscordAccessToken(
	string AccessToken,
	string TokenType,
	int ExpiresIn,
	string RefreshToken,
	string Scope,
	DateTimeOffset ExpiresAt)
{
	public static StoredDiscordAccessToken FromDiscordToken(DiscordAccessToken token)
	{
		var expiresIn = Math.Max(1, token.ExpiresIn);
		return new StoredDiscordAccessToken(
			token.AccessToken,
			token.TokenType,
			expiresIn,
			token.RefreshToken,
			token.Scope,
			DateTimeOffset.UtcNow.AddSeconds(expiresIn));
	}

	public bool ShouldRefresh(TimeSpan? earlyWindow = null)
	{
		var refreshWindow = earlyWindow ?? TimeSpan.FromMinutes(1);
		return this.ExpiresAt <= DateTimeOffset.UtcNow.Add(refreshWindow);
	}

	public DiscordAccessToken ToDiscordToken()
		=> new(
			this.AccessToken,
			this.TokenType,
			Math.Max(1, (int)Math.Ceiling((this.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)),
			this.RefreshToken,
			this.Scope);
}
