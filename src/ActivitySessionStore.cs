// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Security.Cryptography;

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;

using DisCatSharp.Entities;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

internal sealed class ActivitySessionStore
{
	private readonly Lock _gate = new();

	private readonly Dictionary<string, ActivitySession> _sessions = new(StringComparer.Ordinal);

	public ActivitySession CreateSession(DiscordUser user, AuthorizationSnapshot authorization, IEnumerable<ulong> guildIds, StoredDiscordAccessToken oauthToken, TimeSpan ttl, ActivityLaunchContext? launchContext = null, string type = "External", string[]? libraries = null)
	{
		var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		var now = DateTimeOffset.UtcNow;
		var session = new ActivitySession(
			sessionId,
			user.Id,
			user.Username,
			user.GlobalName,
			user.AvatarHash,
			authorization,
			[.. guildIds.Distinct()],
			oauthToken,
			launchContext,
			now,
			now.Add(ttl),
			type,
			libraries);

		lock (this._gate)
		{
			this.PruneExpiredUnsafe(now);
			this._sessions[sessionId] = session;
		}

		return session;
	}

	public ActivitySession? GetSession(string? sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionId))
			return null;

		lock (this._gate)
		{
			this.PruneExpiredUnsafe(DateTimeOffset.UtcNow);
			return this._sessions.TryGetValue(sessionId, out var session) ? session : null;
		}
	}

	public void RemoveSession(string? sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionId))
			return;

		lock (this._gate)
		{
			this._sessions.Remove(sessionId);
		}
	}

	public ActivitySession? UpdateOAuthToken(string sessionId, StoredDiscordAccessToken oauthToken)
	{
		lock (this._gate)
		{
			if (!this._sessions.TryGetValue(sessionId, out var session))
				return null;

			var updated = session with { OAuthToken = oauthToken };
			this._sessions[sessionId] = updated;
			return updated;
		}
	}

	public ActivitySession? UpdateLaunchContext(string sessionId, ActivityLaunchContext? launchContext)
	{
		lock (this._gate)
		{
			if (!this._sessions.TryGetValue(sessionId, out var session))
				return null;

			var updated = session with { LaunchContext = launchContext };
			this._sessions[sessionId] = updated;
			return updated;
		}
	}

	private void PruneExpiredUnsafe(DateTimeOffset now)
	{
		foreach (var key in this._sessions.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToList())
			this._sessions.Remove(key);
	}
}
