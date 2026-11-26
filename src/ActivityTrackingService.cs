// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Text;

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;
using AITSYS.Discord.LibraryDevelopmentTracking.Helpers;
using AITSYS.Discord.LibraryDevelopmentTracking.Rest;

using Microsoft.Extensions.Caching.Memory;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

/// <summary>
/// Read-only Activity projection of the configured Notion implementation
/// trackers. It deliberately exposes only the fields the statistics UI needs.
/// </summary>
internal sealed class ActivityTrackingService(Config config, NotionRestClient notionClient, IMemoryCache cache)
{
	private static readonly TimeSpan s_snapshotLifetime = TimeSpan.FromMinutes(10);

	private readonly SemaphoreSlim _cacheGate = new(1, 1);

	private readonly Config _config = config;

	private readonly NotionRestClient _notionClient = notionClient;

	private readonly IMemoryCache _cache = cache;

	public IReadOnlyList<TrackedNotionSummary> GetTrackedNotions()
		=> [.. (this._config.NotionConfig.ImplementationTrackingConfig ?? [])
			.Where(notion => !string.IsNullOrWhiteSpace(notion.PageId))
			.Select(notion => new TrackedNotionSummary(notion.PageId, notion.Name, GetCustomLinkId(notion)))];

	public string? GetCustomLinkId(string pageId)
	{
		var configuredNotion = (this._config.NotionConfig.ImplementationTrackingConfig ?? [])
			.FirstOrDefault(notion => string.Equals(notion.PageId, pageId, StringComparison.OrdinalIgnoreCase));
		return configuredNotion is null ? null : GetCustomLinkId(configuredNotion);
	}

	public void Invalidate(string pageId)
		=> this._cache.Remove($"activity-tracking:notion:{pageId}");

	public async Task<TrackedNotionDetails?> GetTrackedNotionAsync(string pageId, bool forceRefresh = false)
	{
		var configuredNotion = (this._config.NotionConfig.ImplementationTrackingConfig ?? [])
			.FirstOrDefault(notion => string.Equals(notion.PageId, pageId, StringComparison.OrdinalIgnoreCase));
		if (configuredNotion is null)
			return null;
		var cacheKey = $"activity-tracking:notion:{configuredNotion.PageId}";
		if (!forceRefresh && this._cache.TryGetValue(cacheKey, out TrackedNotionDetails? cached))
			return cached;

		await this._cacheGate.WaitAsync();
		try
		{
			if (!forceRefresh && this._cache.TryGetValue(cacheKey, out cached))
				return cached;

			var snapshot = await this.LoadTrackedNotionAsync(configuredNotion);
			this._cache.Set(cacheKey, snapshot, s_snapshotLifetime);
			return snapshot;
		}
		finally
		{
			this._cacheGate.Release();
		}
	}

	public async Task UpdateLibraryAsync(string pageId, ulong userId, string libraryName, string status, string? prCommit, string? version, string? notes)
	{
		var configuredNotion = (this._config.NotionConfig.ImplementationTrackingConfig ?? [])
			.FirstOrDefault(notion => string.Equals(notion.PageId, pageId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("The requested notion is not configured for tracking.");
		var dataSource = await this._notionClient.GetDataSourceBySearchAsync(pageId)
			?? throw new InvalidOperationException("The tracking data source could not be found.");
		var currentData = await this._notionClient.QueryDataSourceAsync(dataSource.Id, libraryName);
		var library = (currentData?.Results?.FirstOrDefault()) ?? throw new KeyNotFoundException("The selected library is not tracked in this notion.");
		var statusOption = dataSource.Properties?.Status?.InnerStatus?.Options?
			.FirstOrDefault(option => string.Equals(option.Id, status, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(option.Name, status, StringComparison.OrdinalIgnoreCase));
		if (statusOption is null || string.IsNullOrWhiteSpace(statusOption.Id))
			throw new ArgumentException("The selected implementation status is not valid.", nameof(status));

		var result = await this._notionClient.UpdatePageAsync(library.Id, userId, statusOption.Id, prCommit, version, notes);
		if (result.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Notion rejected the library update.");

		this._cache.Remove($"activity-tracking:notion:{configuredNotion.PageId}");
	}

	private async Task<TrackedNotionDetails> LoadTrackedNotionAsync(ImplementationTrackingConfig configuredNotion)
	{

		var pageTask = this._notionClient.GetPageAsync(configuredNotion.PageId);
		var blocksTask = this._notionClient.GetBlockChildrenAsync(configuredNotion.PageId);
		var dataSourceTask = this._notionClient.GetDataSourceBySearchAsync(configuredNotion.PageId);
		await Task.WhenAll(pageTask, blocksTask, dataSourceTask);

		var page = await pageTask;
		var blocks = await blocksTask;
		var dataSource = await dataSourceTask;
		if (page is null || blocks is null || dataSource is null)
			throw new InvalidOperationException("Notion did not return the configured tracking page or data source.");

		var statistics = await this._notionClient.GetStatisticInfosAsync(
			configuredNotion.PageId,
			dataSource.GetStatuses().Values);
		var statusCounts = statistics.GetStatisticsCounts()
			.Select(pair => new StatusCount(pair.Key, pair.Value))
			.ToList();
		var languageBreakdown = statistics.GetLanguageSupportCounts()
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(pair => new LanguageBreakdown(pair.Key, pair.Value))
			.ToList();
		var libraries = statistics
			.SelectMany(pair => pair.Value.Select(result => ToLibrary(result, pair.Key)))
			.OrderBy(library => library.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var callout = blocks.Results?
			.FirstOrDefault(block => string.Equals(block.Type, "callout", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(block.Callout?.Icon?.Emoji, "💡", StringComparison.Ordinal))
			?.Callout;
		var title = JoinText(page.PageProperties?.Title?.Titles) ?? configuredNotion.Name;
		var description = JoinText(callout?.RichText);
		return new TrackedNotionDetails(
			configuredNotion.PageId,
			GetCustomLinkId(configuredNotion),
			title,
			description,
			page.PageIcon?.Emoji,
			page.PublicUrl ?? page.Url,
			page.LastEditedTime,
			statusCounts,
			languageBreakdown,
			libraries);
	}

	private static string GetCustomLinkId(ImplementationTrackingConfig configuredNotion)
	{
		if (!Guid.TryParse(configuredNotion.PageId, out var pageId))
			throw new InvalidOperationException($"The configured Notion page ID '{configuredNotion.PageId}' is not a valid GUID.");

		// A compact UUID keeps the custom ID under Discord's 64-character limit
		// and makes it independent of the editable page title.
		return $"notion-{pageId:N}".ToLowerInvariant();
	}

	private static LibraryStatistic ToLibrary(NotionDataSourceQueryResult.Result result, string fallbackStatus)
	{
		var properties = result.Properties;
		return new LibraryStatistic(
			JoinText(properties?.Library?.Title) ?? "Unnamed library",
			JoinText(properties?.Language?.RichText) ?? "Unspecified",
			properties?.Status?.InnerStatus?.Name ?? fallbackStatus,
			JoinText(properties?.ReleasedInVersion?.RichText),
			properties?.PullRequestCommit?.Url,
			JoinText(properties?.Notes?.RichText));
	}

	private static string? JoinText(IEnumerable<NotionPageResult.Title>? values)
		=> JoinText(values?.Select(value => value.PlainText ?? value.Text?.Content));

	private static string? JoinText(IEnumerable<NotionBlockResult.RichText>? values)
		=> JoinText(values?.Select(value => value.PlainText ?? value.Text?.Content));

	private static string? JoinText(IEnumerable<NotionDataSourceQueryResult.Title>? values)
		=> JoinText(values?.Select(value => value.PlainText ?? value.Text?.Content));

	private static string? JoinText(IEnumerable<NotionDataSourceQueryResult.RichText>? values)
		=> JoinText(values?.Select(value => value.PlainText ?? value.Text?.Content));

	private static string? JoinText(IEnumerable<string?>? values)
	{
		var text = string.Concat(values?.Where(value => !string.IsNullOrWhiteSpace(value)) ?? []);
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}
}

internal sealed record TrackedNotionSummary(string Id, string Name, string CustomId);

internal sealed record TrackedNotionDetails(
	string Id,
	string CustomId,
	string Title,
	string? Description,
	string? Icon,
	string? Url,
	DateTime? LastEditedAt,
	IReadOnlyList<StatusCount> StatusCounts,
	IReadOnlyList<LanguageBreakdown> LanguageBreakdown,
	IReadOnlyList<LibraryStatistic> Libraries);

internal sealed record StatusCount(string Name, int Count);

internal sealed record LanguageBreakdown(string Language, IReadOnlyDictionary<string, int> StatusCounts);

internal sealed record LibraryStatistic(
	string Name,
	string Language,
	string Status,
	string? Version,
	string? ImplementationUrl,
	string? Notes);
