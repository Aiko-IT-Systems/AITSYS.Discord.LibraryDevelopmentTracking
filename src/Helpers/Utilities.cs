// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;
using AITSYS.Discord.LibraryDevelopmentTracking.Enums;

using DisCatSharp;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;

using ScottPlot;
using ScottPlot.Palettes;
using ScottPlot.TickGenerators;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

public static class Utilities
{
	/// <summary>
	/// Checks if the user has access to library developer commands and returns access status, the member, and allowed libraries.
	/// </summary>
	/// <param name="ctx">The interaction context.</param>
	/// <param name="config">The Discord configuration.</param>
	///
	/// <returns>
	/// A tuple containing:
	/// <list type="bullet">
	/// <item><description>HasAccess: Whether the user has access.</description></item>
	/// <item><description>Member: The Discord member object, or null if not found.</description></item>
	/// <item><description>AllowedLibraries: A dictionary of allowed library roles, or null if none.</description></item>
	/// </list>
	/// </returns>
	public static async Task<(bool HasAccess, DiscordMember? Member, Dictionary<ulong, DiscordRole>? AllowedLibraries, bool IsAdmin)> CheckAccessAsync(this InteractionContext ctx, DiscordConfig config)
	{
		// TODO: Adjust as needed
		var admin = ctx.User.IsStaff || ctx.UserId is 856780995629154305;

		ctx.Client.Guilds[config.DiscordGuild].Members.TryGetValue(ctx.User.Id, out var member);

		if (!admin && !(member?.RoleIds.Contains(config.LibraryDeveloperRoleId) ?? false))
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("You need to be a library developer to use this command."));
			return (false, member, null, admin);
		}

		var allowedLibraries = !admin
			? member?.Roles.Where(role => config.LibraryRoleMapping.ContainsKey(role.Id)).ToDictionary(role => role.Id, role => role) ?? []
			: config.LibraryRoleMapping.Select(map => ctx.Client.Guilds[config.DiscordGuild].GetRole(map.Key)!).ToDictionary(role => role.Id, role => role);

		if (allowedLibraries is null or { Count: 0 })
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("You do not have any library roles assigned to you. Please contact a server administrator."));
			return (false, member, null, admin);
		}

		return (true, member, allowedLibraries, admin);
	}

	/// <summary>
	/// Queries the Notion data source for each library in parallel and returns the results.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <param name="libraries">A list of library names to query.</param>
	/// <returns>A dictionary mapping library names to their Notion data source query results.</returns>
	internal static async Task<Dictionary<string, NotionDataSourceQueryResult>> GetCurrentDataAsync(this NotionSearchDataSourceResult.DataSourceResult? dataSource, List<string> libraries)
	{
		if (dataSource is null || libraries is null || libraries.Count is 0)
			return [];

		var tasks = libraries.Select(async library =>
		{
			Console.WriteLine($"Fetching data for {library}...");
			var data = await DiscordBot.NotionRestClient.QueryDataSourceAsync(dataSource.Id, library);
			Console.WriteLine($"Fetched data for {library}.");
			return (library, data);
		}).ToArray();

		var results = await Task.WhenAll(tasks);
		return results
			.Where(x => x.data is not null)
			.ToDictionary(x => x.library, x => x.data!);
	}

	/// <summary>
	/// Creates a Discord radio group component for status selection from a Notion data source.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <param name="setAsDefault">The status to set as default.</param>
	/// <returns>A DiscordRadioGroupComponent for status selection.</returns>
	internal static DiscordRadioGroupComponent GetStatusRadioSelectFromDataSource(this NotionSearchDataSourceResult.DataSourceResult dataSource, string setAsDefault)
	{
		var options = dataSource.Properties.Status.InnerStatus.Options.ToDictionary(option => option.Id, option => option.Name).Select(x => new DiscordRadioGroupComponentOption(x.Value, x.Key, isDefault: x.Value.Equals(setAsDefault, StringComparison.InvariantCultureIgnoreCase)));
		DiscordRadioGroupComponent statusSelect = new(options, "status", required: true);
		return statusSelect;
	}

	/// <summary>
	/// Creates Discord string select components for selecting libraries based on allowed libraries and current data.
	/// </summary>
	/// <param name="allowedLibraries">A dictionary of allowed library roles.</param>
	/// <param name="currentDatas">A dictionary of current Notion data source query results.</param>
	/// <returns>A list of DiscordStringSelectComponent objects for library selection.</returns>
	internal static List<DiscordStringSelectComponent> GetLibrarySelects(this Dictionary<ulong, DiscordRole>? allowedLibraries, Dictionary<string, NotionDataSourceQueryResult> currentDatas)
	{
		List<DiscordStringSelectComponent> selects = [];
		if (allowedLibraries is null)
			return selects;

		var i = 1;
		var options = allowedLibraries.Select(x => new DiscordStringSelectComponentOption(x.Value.Name, x.Key.ToString(), emoji: LanguageEmojis.Map.TryGetValue(string.Join("", currentDatas[x.Value.Name].Results[0].Properties.Language.RichText.SelectMany(x => x.Text.Content)), out var val) ? new DiscordComponentEmoji(val) : null!)).ToList();
		foreach (var chunk in options.Chunk(25))
		{
			var count = allowedLibraries.Count <= 25 ? string.Empty : $"(Page {i}) ";
			DiscordStringSelectComponent select = new($"Select the library {count}you want to update", chunk, minOptions: 1, maxOptions: 1);
			selects.Add(select);
			i++;
		}
		return selects;
	}

	/// <summary>
	/// Gets a dictionary mapping status option IDs to their names from a Notion data source.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <returns>A dictionary of status option IDs and names.</returns>
	internal static Dictionary<string, string> GetStatuses(this NotionSearchDataSourceResult.DataSourceResult dataSource)
		=> dataSource.Properties.Status.InnerStatus.Options.ToDictionary(option => option.Id, option => option.Name);

	/// <summary>
	/// Gets the default ordered list of status names.
	/// </summary>
	/// <returns>A list of status names in default order.</returns>
	internal static List<string> GetOrderedDefaultStatuses()
		=> ["Not Started", "In Progress", "In Review", "Ready For Release", "Released"];

	/// <summary>
	/// Gets a dictionary mapping status names to their counts from Notion query results.
	/// </summary>
	/// <param name="data">A dictionary mapping status names to lists of Notion results.</param>
	/// <returns>A dictionary mapping status names to their counts.</returns>
	internal static Dictionary<string, int> GetStatisticsCounts(this Dictionary<string, List<NotionDataSourceQueryResult.Result>> data)
	{
		Dictionary<string, int> counts = [];
		foreach (var res in data)
			counts[res.Key] = res.Value.Count;
		counts = counts.OrderBy(kv => GetOrderedDefaultStatuses().IndexOf(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
		return counts;
	}

	/// <summary>
	/// Gets a dictionary mapping language names to dictionaries of status counts from Notion query results.
	/// </summary>
	/// <param name="data">A dictionary mapping status names to lists of Notion results.</param>
	/// <returns>A dictionary mapping language names to dictionaries of status counts.</returns>
	internal static Dictionary<string, Dictionary<string, int>> GetLanguageSupportCounts(this Dictionary<string, List<NotionDataSourceQueryResult.Result>> data)
	{
		Dictionary<string, Dictionary<string, int>> counts = [];
		var languages = data.Values.SelectMany(x => x).Select(x => string.Join("", x.Properties.Language.RichText.Select(y => y.Text.Content))).Distinct();
		foreach (var language in languages)
			counts[language] = GetOrderedDefaultStatuses().ToDictionary(x => x, x => 0);
		foreach (var res in data)
			foreach (var item in res.Value)
			{
				var targetLang = string.Join("", item.Properties.Language.RichText.Select(x => x.Text.Content));
				var status = item.Properties.Status.InnerStatus.Name;
				counts[targetLang][status]++;
			}
		return counts;
	}
}
