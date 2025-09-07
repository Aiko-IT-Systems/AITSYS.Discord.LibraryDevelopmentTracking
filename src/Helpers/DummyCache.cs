// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;

using static AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion.NotionSearchDataSourceResult;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Helpers;
internal static class DummyCache
{
	internal static async Task InitAsync()
	{
		var notion = DiscordBot.Config.NotionConfig.ImplementationTrackingConfig[0].PageId;
		Page = await DiscordBot.NotionRestClient.GetPageAsync(notion);
		Block = await DiscordBot.NotionRestClient.GetBlockChildrenAsync(notion);
		DataSource = await DiscordBot.NotionRestClient.GetDataSourceBySearchAsync(notion);
		Statistics = await DiscordBot.NotionRestClient.GetStatisticInfosAsync(notion, DataSource!.GetStatuses().Values, DataSource.GetIdsByDataSource());
	}

	internal static string? Notion { get; set; }
	internal static DataSourceResult? DataSource { get; set; }
	internal static NotionPageResult? Page { get; set; }
	internal static NotionBlockResult? Block { get; set; }
	internal static Dictionary<string, List<NotionDataSourceQueryResult.Result>>? Statistics { get; set; }
}
