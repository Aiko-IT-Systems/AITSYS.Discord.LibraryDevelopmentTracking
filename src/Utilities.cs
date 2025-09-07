// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;

using DisCatSharp;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;

using ScottPlot;
using ScottPlot.Palettes;
using ScottPlot.TickGenerators;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

public static class Utilities
{
	/// <summary>
	/// Checks if the user has access to library developer commands and returns access status, the member, and allowed libraries.
	/// </summary>
	/// <param name="ctx">The interaction context.</param>
	/// <param name="guild">The Discord guild.</param>
	/// <param name="config">The Discord configuration.</param>
	/// <returns>
	/// A tuple containing:
	/// <list type="bullet">
	/// <item><description>HasAccess: Whether the user has access.</description></item>
	/// <item><description>Member: The Discord member object, or null if not found.</description></item>
	/// <item><description>AllowedLibraries: A dictionary of allowed library roles, or null if none.</description></item>
	/// </list>
	/// </returns>
	public static async Task<(bool HasAccess, DiscordMember? Member, Dictionary<ulong, DiscordRole>? AllowedLibraries)> CheckAccessAsync(this InteractionContext ctx, DiscordGuild guild, DiscordConfig config)
	{
		// TODO: Adjust as needed
		var admin = ctx.User.IsStaff || ctx.UserId is 856780995629154305;
		DiscordMember? member = null;
		if (ctx.GuildId != config.DiscordGuild)
		{
			if (!guild.TryGetMember(ctx.User.Id, out member))
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"You have to be a member of {ctx.Client.Guilds[DiscordBot.Config.DiscordConfig.DiscordGuild].Name.InlineCode()} to use this command."));
				return (false, member, null);
			}
		}
		else
			member = ctx.Member!;

		if (!member.RoleIds.Contains(config.LibraryDeveloperRoleId) && !admin)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("You need to be a library developer to use this command."));
			return (false, member, null);
		}

		var allowedLibraries = !admin
			? member.Roles.Where(role => DiscordBot.Config.DiscordConfig.LibraryRoleMapping.ContainsKey(role.Id)).ToDictionary(role => role.Id, role => role)
			: DiscordBot.Config.DiscordConfig.LibraryRoleMapping.Select(map => guild.GetRole(map.Key)!).ToDictionary(role => role.Id, role => role);

		if (allowedLibraries is null or { Count: 0 })
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("You do not have any library roles assigned to you. Please contact a server administrator."));
			return (false, member, null);
		}

		return (true, member, allowedLibraries);
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
	/// Creates a Discord string select component for status selection from a Notion data source.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <param name="setAsDefault">The status to set as default.</param>
	/// <returns>A DiscordStringSelectComponent for status selection.</returns>
	internal static DiscordStringSelectComponent GetStatusSelectMenuFromDataSource(NotionSearchDataSourceResult.DataSourceResult dataSource, string setAsDefault)
	{
		var options = dataSource.Properties.Status.InnerStatus.Options.ToDictionary(option => option.Id, option => option.Name);
		DiscordStringSelectComponent statusSelect = new("Select the current status", options.Select(x => new DiscordStringSelectComponentOption(x.Value, x.Key, isDefault: x.Value.Equals(setAsDefault, StringComparison.InvariantCultureIgnoreCase))), "status", 1, 1, required: true);
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
		var options = allowedLibraries.Select(x => new DiscordStringSelectComponentOption(x.Value.Name, x.Key.ToString(), emoji: new DiscordComponentEmoji(LanguageEmojis.Map[string.Join("", currentDatas[x.Value.Name].Results[0].Properties.Language.RichText.SelectMany(x => x.Text.Content))]))).ToList();
		foreach (var chunk in options.Chunk(25))
		{
			DiscordStringSelectComponent select = new($"Select the library ({i}) you want to update", chunk, minOptions: 1, maxOptions: 1);
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
	/// Gets the default color mapping for known statuses.
	/// </summary>
	/// <returns>A dictionary mapping status names to their default colors.</returns>
	internal static Dictionary<string, Color> GetDefaultColors()
	{
		return new()
					{
						{ "Not Started", Colors.Red },
						{ "In Progress", Colors.Orange },
						{ "In Review", Colors.Blue },
						{ "Ready For Release", Colors.Yellow },
						{ "Released", Colors.Green }
					};
	}

	/// <summary>
	/// Gets the default color mapping for known statuses in reverse order.
	/// </summary>
	/// <returns>A dictionary mapping status names to their default colors in reverse order.</returns>
	internal static Dictionary<string, Color> GetDefaultColorsReversed()
		=> GetDefaultColors().Reverse().ToDictionary(x => x.Key, x => x.Value);

	/// <summary>
	/// Gets the default ordered list of status names.
	/// </summary>
	/// <returns>A list of status names in default order.</returns>
	internal static List<string> GetOrderedDefaultStatuses()
		=> ["Not Started", "In Progress", "In Review", "Ready For Release", "Released"];

	/// <summary>
	/// Gets the default ordered list of status names in reverse order.
	/// </summary>
	/// <returns>A list of status names in reverse order.</returns>
	internal static List<string> GetOrderedDefaultStatusesReversed()
		=> [.. GetOrderedDefaultStatuses().AsEnumerable().Reverse()];

	/// <summary>
	/// Gets the status and language property IDs from a Notion data source.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <returns>A tuple containing the status ID and language ID.</returns>
	internal static (string StatusId, string LanguageId) GetIdsByDataSource(this NotionSearchDataSourceResult.DataSourceResult dataSource)
	{
		var statusId = dataSource.Properties.Status.Id;
		var languageId = dataSource.Properties.Language.Id;
		return (statusId, languageId);
	}

	/// <summary>
	/// Generates a ScottPlot pie chart from the provided status data.
	/// </summary>
	/// <param name="data">A dictionary mapping status names to their counts.</param>
	/// <returns>A ScottPlot.Plot object representing the pie chart.</returns>
	internal static Plot GenerateNotionPieChart(this Dictionary<string, int> data)
	{
		Plot notionPlot = new();
		notionPlot.Add.Palette = new Penumbra();
		notionPlot.Font.Set("gg sans", FontWeight.Medium);
		notionPlot.DataBackground.Color = Color.FromHex("#191919");
		var total = data.Sum(s => s.Value);
		var colors = GetDefaultColors();
		var slices = data.Select(x => new PieSlice() { Value = x.Value, FillColor = colors[x.Key], Label = x.Key }).ToList();
		slices.ForEach(slice =>
		{
			slice.LabelFontColor = Color.FromHex("#6F7D6F");
			slice.LabelFontSize = 20;
			slice.LegendText = $"{slice.Label}";
			slice.LabelAlignment = Alignment.MiddleCenter;
			slice.LabelText = $"{slice.Value} ({slice.Value / (float)total * 100:F1}%)";
			slice.LabelBold = true;
			slice.LabelLineSpacing = 1.2f;
			slice.LabelFontName = "gg sans Medium";
		});
		var notionPie = notionPlot.Add.Pie(slices);
		notionPie.ExplodeFraction = .01;
		notionPie.SliceLabelDistance = 1.5;
		notionPie.Radius = 1;
		notionPie.DonutFraction = .2;
		notionPie.LinePattern = LinePattern.Solid;
		notionPie.LineColor = Color.FromHex("#191919");
		notionPie.LineWidth = 2;
		notionPlot.Axes.Frameless();
		notionPlot.HideGrid();
		notionPlot.Legend.FontColor = Color.FromHex("#6F7D6F");
		notionPlot.Legend.FontSize = 16;
		notionPlot.Legend.BackgroundColor = Color.FromHex("#191919");
		notionPlot.Legend.OutlineColor = Color.FromHex("#191919");
		notionPlot.Legend.Alignment = Alignment.LowerCenter;
		notionPlot.Legend.Orientation = Orientation.Horizontal;
		notionPlot.Legend.TightHorizontalWrapping = false;
		notionPlot.Legend.SymbolPadding = 10;
		notionPlot.Legend.FontName = "gg sans Medium";
		notionPlot.Legend.ShadowFillStyle.IsVisible = false;
		notionPlot.ShowLegend();
		return notionPlot;
	}

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
		counts = counts.OrderBy(kv => Utilities.GetOrderedDefaultStatuses().IndexOf(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
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
			counts[language] = Utilities.GetOrderedDefaultStatuses().ToDictionary(x => x, x => 0);
		foreach (var res in data)
		{
			foreach (var item in res.Value)
			{
				var targetLang = string.Join("", item.Properties.Language.RichText.Select(x => x.Text.Content));
				var status = item.Properties.Status.InnerStatus.Name;
				counts[targetLang][status]++;
			}
		}
		return counts;
	}

	/// <summary>
	/// Generates a ScottPlot stacked bar chart for language support, showing status counts per language.
	/// </summary>
	/// <param name="data">A dictionary mapping language names to dictionaries of status counts.</param>
	/// <returns>A ScottPlot.Plot object representing the stacked bar chart.</returns>
	internal static Plot GenerateNotionBarChart(this Dictionary<string, Dictionary<string, int>> data)
	{
		Plot notionPlot = new();
		notionPlot.Add.Palette = new Penumbra();
		notionPlot.Font.Set("gg sans", FontWeight.Medium);
		notionPlot.DataBackground.Color = Color.FromHex("#191919");
		notionPlot.FigureBackground.Color = Color.FromHex("#191919");
		data = data.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
		var statuses = GetOrderedDefaultStatusesReversed().ToArray();
		var colors = GetDefaultColorsReversed().Select(x => x.Value).ToArray();
		var maxCount = data.Values.Max(x => x.Values.Sum()) + 2;
		foreach (var group in data.Keys)
		{
			List<Bar> bars = [];
			var nextBarBase = 0;
			for (var i = 0; i < statuses.Length; i++)
			{
				var status = statuses[i];
				var value = data[group].TryGetValue(status, out var val) ? val : 0;
				Bar bar = new()
				{
					Value = nextBarBase + value,
					FillColor = colors[i],
					ValueBase = nextBarBase,
					Position = Array.IndexOf([.. data.Keys], group)
				};
				bars.Add(bar);
				nextBarBase += value;
			}
			notionPlot.Add.Bars(bars.ToArray());
		}

		NumericManual langTickGen = new();
		var langIndex = 0;
		foreach (var language in data.Keys)
		{
			langTickGen.AddMajor(langIndex, language);
			langIndex++;
		}
		NumericManual countTickGen = new();
		for (var i = 0; i <= maxCount; i++)
			countTickGen.AddMajor(i, i.ToString());

		statuses = statuses.Reverse().ToArray();
		for (var i = 0; i < statuses.Length; i++)
		{
			var status = statuses[i];
			LegendItem item = new()
			{
				LabelText = status,
				FillColor = colors[i]
			};
			notionPlot.Legend.ManualItems.Add(item);
		}
		notionPlot.Axes.Bottom.TickGenerator = langTickGen;
		notionPlot.Axes.Bottom.TickLabelStyle = new LabelStyle() { ForeColor = Color.FromHex("#6F7D6F"), FontSize = 16, FontName = "gg sans Medium" };
		notionPlot.Axes.Left.TickLabelStyle = new LabelStyle() { ForeColor = Color.FromHex("#6F7D6F"), FontSize = 16, FontName = "gg sans Medium" };
		notionPlot.Axes.Left.FrameLineStyle.Pattern = LinePattern.Solid;
		notionPlot.Axes.Left.FrameLineStyle.Color = Color.FromHex("#FFFFFF");
		notionPlot.Axes.Left.TickGenerator = countTickGen;
		notionPlot.Axes.Color(Color.FromHex("#6F7D6F"));
		notionPlot.Axes.AntiAlias(true);
		notionPlot.Axes.Margins(bottom: 0);
		notionPlot.Legend.FontColor = Color.FromHex("#6F7D6F");
		notionPlot.Legend.FontSize = 16;
		notionPlot.Legend.BackgroundColor = Color.FromHex("#191919");
		notionPlot.Legend.OutlineColor = Color.FromHex("#191919");
		notionPlot.Legend.Alignment = Alignment.UpperCenter;
		notionPlot.Legend.Orientation = Orientation.Horizontal;
		notionPlot.Legend.TightHorizontalWrapping = false;
		notionPlot.Legend.SymbolPadding = 10;
		notionPlot.Legend.FontName = "gg sans Medium";
		notionPlot.Legend.ShadowFillStyle.IsVisible = false;
		notionPlot.ShowLegend();
		return notionPlot;
	}
}
