// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Rest;

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

internal sealed class NotionManagementService(Config config, NotionRestClient notionClient, ActivityTrackingService tracking)
{
	private readonly SemaphoreSlim _configurationGate = new(1, 1);
	private readonly Config _config = config;
	private readonly NotionRestClient _notionClient = notionClient;
	private readonly ActivityTrackingService _tracking = tracking;

	public async Task<IReadOnlyList<ManagedNotion>> GetNotionsAsync()
	{
		var configured = this._config.NotionConfig.ImplementationTrackingConfig ?? [];
		var notions = configured.Select(notion => new ManagedNotion(
			notion.PageId,
			notion.Name,
			notion.DatabaseId,
			notion.DataSourceId,
			true,
			notion.PageId.Equals(this._config.NotionConfig.NotionTemplatePageId, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		notions.AddRange((this._config.NotionConfig.DisabledImplementationTrackingConfig ?? []).Select(notion => new ManagedNotion(
			notion.PageId,
			notion.Name,
			notion.DatabaseId,
			notion.DataSourceId,
			false,
			false)));
		var knownPageIds = notions.Select(notion => NormalizeId(notion.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var knownDataSourceIds = notions.Select(notion => NormalizeId(notion.DataSourceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var templatePageId = this._config.NotionConfig.NotionTemplatePageId;

		foreach (var dataSource in await this._notionClient.SearchAllDataSourcesAsync())
		{
			if (dataSource.InTrash is true || knownDataSourceIds.Contains(NormalizeId(dataSource.Id)))
				continue;

			var pageId = await this._notionClient.ResolvePageIdForDataSourceAsync(dataSource);
			if (string.IsNullOrWhiteSpace(pageId)
				|| pageId.Equals(templatePageId, StringComparison.OrdinalIgnoreCase)
				|| knownPageIds.Contains(NormalizeId(pageId)))
				continue;

			var title = await this._notionClient.GetPageTitleAsync(pageId)
				?? dataSource.Title?.FirstOrDefault()?.PlainText
				?? "Untitled";
			notions.Add(new ManagedNotion(
				pageId,
				title,
				dataSource.Parent?.DatabaseId,
				dataSource.Id,
				false,
				false));
			knownPageIds.Add(NormalizeId(pageId));
			knownDataSourceIds.Add(NormalizeId(dataSource.Id));
		}

		return notions;
	}

	public async Task EnableAsync(string pageId)
	{
		var existing = this.FindConfiguredNotion(pageId);
		if (existing is not null)
			return;

		var notion = (await this.GetNotionsAsync()).FirstOrDefault(candidate =>
			!candidate.Enabled && string.Equals(candidate.Id, pageId, StringComparison.OrdinalIgnoreCase));
		if (notion is null || string.IsNullOrWhiteSpace(notion.DatabaseId) || string.IsNullOrWhiteSpace(notion.DataSourceId))
			throw new KeyNotFoundException("The selected disabled notion could not be discovered or is missing its database information.");

		await this.MutateConfigurationAsync(() =>
		{
			this._config.NotionConfig.DisabledImplementationTrackingConfig.RemoveAll(candidate =>
				string.Equals(candidate.PageId, notion.Id, StringComparison.OrdinalIgnoreCase));
			this._config.NotionConfig.ImplementationTrackingConfig.Add(new ImplementationTrackingConfig
			{
				Name = notion.Name,
				PageId = notion.Id,
				DatabaseId = notion.DatabaseId,
				DataSourceId = notion.DataSourceId
			});
		});
	}

	public async Task DisableAsync(string pageId)
	{
		var notion = this.FindConfiguredNotion(pageId)
			?? throw new KeyNotFoundException("The selected notion is not enabled.");
		await this.MutateConfigurationAsync(() =>
		{
			this._config.NotionConfig.ImplementationTrackingConfig.Remove(notion);
			this._config.NotionConfig.DisabledImplementationTrackingConfig.RemoveAll(candidate => string.Equals(candidate.PageId, notion.PageId, StringComparison.OrdinalIgnoreCase));
			this._config.NotionConfig.DisabledImplementationTrackingConfig.Add(notion);
		});
		this._tracking.Invalidate(pageId);
	}

	public async Task<NotionMetadataUpdateResult> UpdateMetadataAsync(string pageId, string title, string description)
	{
		if (this.FindConfiguredNotion(pageId) is null
			&& !(await this.GetNotionsAsync()).Any(notion => string.Equals(notion.Id, pageId, StringComparison.OrdinalIgnoreCase)))
			throw new KeyNotFoundException("The selected notion could not be found in the configured or discoverable trackers.");

		var (titleUpdated, _) = await this._notionClient.RenamePageAsync(pageId, title);
		var descriptionUpdated = await this._notionClient.UpdateDescriptionCalloutAsync(pageId, description);
		var configured = this.FindConfiguredNotion(pageId);
		if (titleUpdated && configured is not null)
			await this.MutateConfigurationAsync(() => configured.Name = title);
		var disabled = this.FindDisabledNotion(pageId);
		if (titleUpdated && disabled is not null)
			await this.MutateConfigurationAsync(() => disabled.Name = title);
		this._tracking.Invalidate(pageId);
		return new NotionMetadataUpdateResult(titleUpdated, descriptionUpdated);
	}

	public async Task<NotionMetadata> GetMetadataAsync(string pageId)
	{
		var known = this.FindConfiguredNotion(pageId) is not null
			|| this.FindDisabledNotion(pageId) is not null
			|| (await this.GetNotionsAsync()).Any(notion => string.Equals(notion.Id, pageId, StringComparison.OrdinalIgnoreCase));
		if (!known)
			throw new KeyNotFoundException("The selected notion could not be found in the configured or discoverable trackers.");

		var pageTask = this._notionClient.GetPageAsync(pageId);
		var blocksTask = this._notionClient.GetBlockChildrenAsync(pageId);
		await Task.WhenAll(pageTask, blocksTask);
		var page = await pageTask;
		var blocks = await blocksTask;
		var title = page?.PageProperties?.Title?.Titles?.FirstOrDefault()?.Text?.Content?.Trim()
			?? this.FindConfiguredNotion(pageId)?.Name
			?? this.FindDisabledNotion(pageId)?.Name
			?? "Untitled";
		var callout = blocks?.Results?.FirstOrDefault(block =>
			string.Equals(block.Type, "callout", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(block.Callout?.Icon?.Emoji, "💡", StringComparison.Ordinal))?.Callout;
		var description = callout?.RichText is null
			? string.Empty
			: string.Concat(callout.RichText.Select(text => text.PlainText ?? text.Text?.Content ?? string.Empty));
		return new NotionMetadata(title, description);
	}

	public async Task<NotionCreationResult> CreateAsync(string title, string description, bool autoEnable)
	{
		var fullTitle = $"{title.Trim()} - Implementation Statuses";
		var parentPageId = this._config.NotionConfig.NotionParentPageId;
		var templatePageId = this._config.NotionConfig.NotionTemplatePageId;
		if (string.IsNullOrWhiteSpace(parentPageId) || string.IsNullOrWhiteSpace(templatePageId))
			throw new InvalidOperationException("The Notion parent page and template page must be configured before creating a tracker.");

		var createdPage = await this._notionClient.CreatePageFromTemplateAsync(parentPageId, fullTitle, templatePageId);
		var pageId = createdPage["id"]?.ToString();
		if (string.IsNullOrWhiteSpace(pageId))
			throw new InvalidOperationException("Notion created a response without a page ID.");

		if (!await this._notionClient.WaitForTemplateContentAsync(pageId))
			throw new InvalidOperationException($"The page was created, but Notion did not expose template content in time. Page ID: {pageId}");

		var (titleUpdated, renameResult) = await this._notionClient.RenamePageAsync(pageId, fullTitle);
		var descriptionUpdated = await this._notionClient.UpdateDescriptionCalloutAsync(pageId, description.Trim());
		var (databaseId, dataSourceId) = await this._notionClient.FindLibrariesDataSourceAsync(pageId);
		var enabled = autoEnable && !string.IsNullOrWhiteSpace(databaseId) && !string.IsNullOrWhiteSpace(dataSourceId);
		if (enabled)
		{
			await this.MutateConfigurationAsync(() => this._config.NotionConfig.ImplementationTrackingConfig.Add(new ImplementationTrackingConfig
			{
				Name = fullTitle,
				PageId = pageId,
				DatabaseId = databaseId!,
				DataSourceId = dataSourceId!
			}));
		}
		else if (!string.IsNullOrWhiteSpace(databaseId) && !string.IsNullOrWhiteSpace(dataSourceId))
		{
			await this.MutateConfigurationAsync(() => this._config.NotionConfig.DisabledImplementationTrackingConfig.Add(new ImplementationTrackingConfig
			{
				Name = fullTitle,
				PageId = pageId,
				DatabaseId = databaseId!,
				DataSourceId = dataSourceId!
			}));
		}

		return new NotionCreationResult(
			pageId,
			fullTitle,
			databaseId,
			dataSourceId,
			enabled,
			titleUpdated,
			descriptionUpdated,
			Url: renameResult["public_url"]?.ToString() ?? renameResult["url"]?.ToString() ?? createdPage["url"]?.ToString());
	}

	private ImplementationTrackingConfig? FindConfiguredNotion(string pageId)
		=> (this._config.NotionConfig.ImplementationTrackingConfig ?? [])
			.FirstOrDefault(notion => string.Equals(notion.PageId, pageId, StringComparison.OrdinalIgnoreCase));

	private ImplementationTrackingConfig? FindDisabledNotion(string pageId)
		=> (this._config.NotionConfig.DisabledImplementationTrackingConfig ?? [])
			.FirstOrDefault(notion => string.Equals(notion.PageId, pageId, StringComparison.OrdinalIgnoreCase));

	private async Task MutateConfigurationAsync(Action mutation)
	{
		await this._configurationGate.WaitAsync();
		try
		{
			var path = Path.GetFullPath("config.json");
			var temporaryPath = path + ".tmp";
			var previousEnabled = this._config.NotionConfig.ImplementationTrackingConfig.ToList();
			var previousDisabled = this._config.NotionConfig.DisabledImplementationTrackingConfig.ToList();
			mutation();
			try
			{
				var json = JsonConvert.SerializeObject(this._config, Formatting.Indented);
				await File.WriteAllTextAsync(temporaryPath, json);
				File.Move(temporaryPath, path, true);
			}
			catch
			{
				this._config.NotionConfig.ImplementationTrackingConfig = previousEnabled;
				this._config.NotionConfig.DisabledImplementationTrackingConfig = previousDisabled;
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
				throw;
			}
		}
		finally
		{
			this._configurationGate.Release();
		}
	}

	private static string NormalizeId(string? value)
		=> value?.Replace("-", string.Empty, StringComparison.Ordinal) ?? string.Empty;
}

internal sealed record ManagedNotion(string Id, string Name, string? DatabaseId, string DataSourceId, bool Enabled, bool IsTemplate);

internal sealed record NotionMetadataUpdateResult(bool TitleUpdated, bool DescriptionUpdated);

internal sealed record NotionMetadata(string Title, string Description);

internal sealed record NotionCreationResult(
	string Id,
	string Name,
	string? DatabaseId,
	string? DataSourceId,
	bool Enabled,
	bool TitleUpdated,
	bool DescriptionUpdated,
	string? Url);
