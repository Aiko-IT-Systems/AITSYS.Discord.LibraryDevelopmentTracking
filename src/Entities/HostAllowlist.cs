// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Microsoft.AspNetCore.Http;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities;

internal static class HostAllowlist
{
	private static readonly string[] s_discordEntryHosts =
	[
		"discord.com",
		"canary.discord.com",
		"ptb.discord.com",
		"staging.discord.co",
		"discordapp.com"
	];

	/// <summary>
	/// Represents the name identifier used for the Discord worker service.
	/// </summary>
	private const string DiscordWorkerName = "discordsays.com";

	private static readonly HashSet<string> s_localHosts = new(StringComparer.OrdinalIgnoreCase)
	{
		"127.0.0.1",
		"localhost"
	};

	internal static async Task<(bool IsAllowed, string FailureReason)> IsAllowedAsync(HttpRequest request, Config config, ActivityAuthService auth, bool proxyAuthSuccess)
	{
		var host = request.Host.Host;
		if (string.IsNullOrWhiteSpace(host))
			return (false, "Request host header was empty.");

		if (IsLocalHost(host))
		{
			if (config.DiscordConfig.AllowLocalDev)
				return (true, string.Empty);

			return (false, "Local development access is disabled.");
		}

		if (!IsDiscordProxyHost(host, config))
			return (false, $"Host '{host}' did not match activity.public_host.");

		var cfWorker = request.Headers["Cf-Worker"].ToString();
		if (!string.Equals(cfWorker, DiscordWorkerName, StringComparison.OrdinalIgnoreCase))
			return (false, $"Cf-Worker was '{cfWorker}', expected '{DiscordWorkerName}'.");

		if (!proxyAuthSuccess)
		{
			if (!TryGetRefererHost(request, out var refererHost))
			return (false, "Request did not include a valid Referer host.");

			var activityHost = await auth.GetDiscordActivityHostAsync();
			if (string.Equals(refererHost, activityHost, StringComparison.OrdinalIgnoreCase) || IsDiscordEntryRequest(request, refererHost))
				return (true, string.Empty);

			return (false, $"Referer host '{refererHost}' did not match the Discord activity host '{activityHost}' or an approved Discord entry host.");
		}
		else
			return (true, string.Empty);
	}

	private static bool TryGetRefererHost(HttpRequest request, out string? host)
	{
		host = null;

		var referer = request.Headers.Referer.ToString();
		if (string.IsNullOrWhiteSpace(referer))
			return false;

		if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
			return false;

		host = refererUri.Host;
		return !string.IsNullOrWhiteSpace(host);
	}

	private static bool IsDiscordEntryRequest(HttpRequest request, string? refererHost)
	=> !string.IsNullOrWhiteSpace(refererHost) && s_discordEntryHosts.Contains(refererHost, StringComparer.OrdinalIgnoreCase) && (request.Path == "/" || string.Equals(request.Path.Value, "/index.html", StringComparison.OrdinalIgnoreCase));

	internal static bool IsLocalHost(string? host)
		=> !string.IsNullOrWhiteSpace(host) && s_localHosts.Contains(host);

	internal static bool IsDiscordProxyHost(string? host, Config config)
		=> !string.IsNullOrWhiteSpace(host)
			&& !string.IsNullOrWhiteSpace(config.DiscordConfig.PublicHost)
			&& string.Equals(host, config.DiscordConfig.PublicHost, StringComparison.OrdinalIgnoreCase);

	internal static string GetDiscordActivityHost(ulong applicationId)
		=> $"{applicationId}.discordsays.com";
}
