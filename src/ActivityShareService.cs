// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Collections.Concurrent;

using DisCatSharp;

using Microsoft.Extensions.Logging;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

/// <summary>
/// Bridges authenticated Activity sessions to Discord's Activity attachment and
/// quick-link APIs. Tokens remain server-side; the browser only receives the
/// short-lived CDN URL needed by the Embedded App SDK.
/// </summary>
internal sealed class ActivityShareService(
	DiscordClient discordClient,
	IHttpClientFactory httpClientFactory,
	ILogger<ActivityShareService> logger)
{
	private static readonly Uri s_emojiImageBaseUri = new("https://www.emoji.family/api/emojis/");

	private readonly ConcurrentDictionary<string, byte> _attemptedQuickLinks = new(StringComparer.Ordinal);

	private readonly SemaphoreSlim _quickLinkGate = new(1, 1);

	private readonly DiscordClient _discordClient = discordClient;

	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

	private readonly ILogger<ActivityShareService> _logger = logger;

	public async Task<string> CreateActivityAttachmentAsync(
		string accessToken,
		Stream image,
		string fileName,
		CancellationToken cancellationToken)
	{
		var upload = await this._discordClient.CreateActivityAttachmentAsync(
			accessToken,
			image,
			fileName,
			"image/png",
			cancellationToken);
		var mediaUrl = upload.Attachment?.Url?.ToString();
		return string.IsNullOrWhiteSpace(mediaUrl)
			? throw new InvalidOperationException("Discord did not return a shareable Activity attachment URL.")
			: mediaUrl;
	}

	/// <summary>
	/// Creates the richer Discord preview for a configured notion at most once
	/// per app process. Both successful and failed attempts are remembered: this
	/// API is best-effort and must never retry-spam Discord after a bad request.
	/// </summary>
	public async Task TryProvisionQuickLinkAsync(
		TrackedNotionDetails notion,
		string accessToken,
		CancellationToken cancellationToken)
	{
		if (!this._attemptedQuickLinks.TryAdd(notion.CustomId, 0))
			return;

		await this._quickLinkGate.WaitAsync(cancellationToken);
		try
		{
			var image = await this.GetNotionIconDataUrlAsync(notion.Icon, cancellationToken);
			await this._discordClient.CreateActivityQuickLinkAsync(
				accessToken,
				notion.CustomId,
				Truncate(notion.Description ?? "Read-only Discord library implementation statistics.", 200),
				Truncate(notion.Title, 100),
				image,
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			this._logger.LogDebug(ex, "Discord quick-link preview provisioning failed for notion {NotionId}; the custom launch route remains usable.", notion.Id);
		}
		finally
		{
			this._quickLinkGate.Release();
		}
	}

	private async Task<string> GetNotionIconDataUrlAsync(string? icon, CancellationToken cancellationToken)
	{
		var emoji = string.IsNullOrWhiteSpace(icon) ? "📄" : icon;
		var relativePath = $"{Uri.EscapeDataString(emoji)}/fluent/png/512";
		var imageUri = new Uri(s_emojiImageBaseUri, relativePath);
		using var response = await this._httpClientFactory.CreateClient().GetAsync(imageUri, cancellationToken);
		response.EnsureSuccessStatusCode();
		var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		return $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
	}

	private static string Truncate(string value, int maximumLength)
		=> value.Length <= maximumLength ? value : value[..(maximumLength - 1)].TrimEnd() + "…";
}

internal sealed record ActivityShareImageResponse(string MediaUrl);
