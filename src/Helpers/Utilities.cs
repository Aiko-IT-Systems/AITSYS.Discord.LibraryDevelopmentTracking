// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

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
		var admin = ctx.User.IsStaff;// || ctx.UserId is 856780995629154305;

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
	/// Creates a Discord string select component for status selection from a Notion data source.
	/// </summary>
	/// <param name="dataSource">The Notion data source result.</param>
	/// <param name="setAsDefault">The status to set as default.</param>
	/// <returns>A DiscordStringSelectComponent for status selection.</returns>
	internal static DiscordStringSelectComponent GetStatusSelectMenuFromDataSource(this NotionSearchDataSourceResult.DataSourceResult dataSource, string setAsDefault)
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
	/// Gets the default color mapping for known statuses.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to use for the colors.</param>
	/// <returns>A dictionary mapping status names to their default colors.</returns>
	internal static Dictionary<string, Color> GetDefaultColors(ColorMode colorMode)
	{
		return new()
					{
						{ "Not Started", colorMode.Red() },
						{ "In Progress", colorMode.Orange() },
						{ "In Review", colorMode.Blue() },
						{ "Ready For Release", colorMode.Yellow() },
						{ "Released", colorMode.Green() }
					};
	}

	/// <summary>
	/// Returns the red color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the red color.</param>
	/// <returns>The red color used for status highlighting.</returns>
	internal static Color Red(this ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#BE524B")
			: Color.FromHex("#C4554D");

	/// <summary>
	/// Returns the orange color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the orange color.</param>
	/// <returns>The orange color used for status highlighting.</returns>
	internal static Color Orange(this ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#CB7B37")
			: Color.FromHex("#CC782F");

	/// <summary>
	/// Returns the blue color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the blue color.</param>
	/// <returns>The blue color used for status highlighting.</returns>
	internal static Color Blue(this ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#447ACB")
			: Color.FromHex("#487CA5");

	/// <summary>
	/// Returns the yellow color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the yellow color.</param>
	/// <returns>The yellow color used for status highlighting.</returns>
	internal static Color Yellow(this ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#C19138")
			: Color.FromHex("#C29343");

	/// <summary>
	/// Returns the green color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the green color.</param>
	/// <returns>The green color used for status highlighting.</returns>
	internal static Color Green(this ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#4F9768")
			: Color.FromHex("#548164");

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
	/// Returns the Notion background color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the background color.</param>
	/// <returns>The background color used for Notion-styled charts and UI elements.</returns>
	internal static Color NotionBackgroundColor(ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#191919")
			: Color.FromHex("#FFFFFF");

	/// <summary>
	/// Returns the Notion foreground (text) color for the specified color mode.
	/// </summary>
	/// <param name="colorMode">The color mode (light or dark) to determine the foreground color.</param>
	/// <returns>The foreground color used for Notion-styled charts and UI elements.</returns>
	internal static Color NotionForegroundColor(ColorMode colorMode)
		=> colorMode is ColorMode.Dark
			? Color.FromHex("#D4D4D4")
			: Color.FromHex("#373530");

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
	/// <param name="colorMode">The color mode (light or dark) to use for the chart's appearance.</param>
	/// <returns>A ScottPlot.Plot object representing the pie chart for given <paramref name="data"/>.</returns>
	internal static Plot GenerateNotionPieChart(this Dictionary<string, int> data, ColorMode colorMode)
	{
		Plot notionPlot = new();
		notionPlot.Add.Palette = new Penumbra();
		notionPlot.Font.Set("gg sans", FontWeight.Medium);
		notionPlot.DataBackground.Color = NotionBackgroundColor(colorMode);
		var total = data.Sum(s => s.Value);
		var colors = GetDefaultColors(colorMode);
		var slices = data.Select(x => new PieSlice() { Value = x.Value, FillColor = colors[x.Key], Label = x.Key }).ToList();
		slices.ForEach(slice =>
		{
			slice.LabelFontColor = NotionForegroundColor(colorMode);
			slice.LabelFontSize = 30;
			slice.LegendText = $"{slice.Label} ({slice.Value})";
			slice.LabelAlignment = Alignment.MiddleCenter;
			slice.LabelText = $"{slice.Value} ({slice.Value / total * 100:F1}%)";
			slice.LabelBold = true;
			slice.LabelLineSpacing = 1.2f;
			slice.LabelFontName = "gg sans Medium";
		});
		var notionPie = notionPlot.Add.Pie(slices);
		//notionPie.ExplodeFraction = .01;
		notionPie.SliceLabelDistance = 1.5;
		notionPie.Radius = 1.1;
		//notionPie.Rotation = Angle.FromDegrees(180);
		notionPie.DonutFraction = .05;
		notionPie.LinePattern = LinePattern.Solid;
		notionPie.LineColor = NotionBackgroundColor(colorMode);
		notionPie.LineWidth = 2;
		notionPlot.Axes.Frameless();
		notionPlot.HideGrid();
		notionPlot.Legend.FontColor = NotionForegroundColor(colorMode);
		notionPlot.Legend.FontSize = 16;
		notionPlot.Legend.BackgroundColor = NotionBackgroundColor(colorMode);
		notionPlot.Legend.OutlineColor = NotionBackgroundColor(colorMode);
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

	/// <summary>
	/// Generates a ScottPlot stacked bar chart for language support, showing status counts per language.
	/// </summary>
	/// <param name="data">A dictionary mapping language names to dictionaries of status counts.</param>
	/// <param name="colorMode">The color mode (light or dark) to use for the chart's appearance.</param>
	/// <returns>A ScottPlot.Plot object representing the stacked bar chart for given <paramref name="data"/>.</returns>
	internal static Plot GenerateNotionBarChart(this Dictionary<string, Dictionary<string, int>> data, ColorMode colorMode)
	{
		Plot notionPlot = new();
		notionPlot.Add.Palette = new Penumbra();
		notionPlot.Font.Set("gg sans", FontWeight.Medium);
		notionPlot.DataBackground.Color = NotionBackgroundColor(colorMode);
		notionPlot.FigureBackground.Color = NotionBackgroundColor(colorMode);
		data = data.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
		var statuses = GetOrderedDefaultStatusesReversed().ToArray();
		var colors = GetDefaultColors(colorMode);
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
					FillColor = colors[status],
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
				FillColor = colors[status]
			};
			notionPlot.Legend.ManualItems.Add(item);
		}
		notionPlot.Axes.Bottom.TickGenerator = langTickGen;
		notionPlot.Axes.Bottom.TickLabelStyle = new LabelStyle() { ForeColor = NotionForegroundColor(colorMode), FontSize = 16, FontName = "gg sans Medium" };
		notionPlot.Axes.Left.TickLabelStyle = new LabelStyle() { ForeColor = NotionForegroundColor(colorMode), FontSize = 16, FontName = "gg sans Medium" };
		notionPlot.Axes.Left.FrameLineStyle.Pattern = LinePattern.Solid;
		notionPlot.Axes.Left.FrameLineStyle.Color = NotionForegroundColor(colorMode);
		notionPlot.Axes.Left.TickGenerator = countTickGen;
		notionPlot.Axes.Color(NotionForegroundColor(colorMode));
		notionPlot.Axes.AntiAlias(true);
		notionPlot.Axes.Margins(bottom: 0);
		notionPlot.Legend.FontColor = NotionForegroundColor(colorMode);
		notionPlot.Legend.FontSize = 16;
		notionPlot.Legend.BackgroundColor = NotionBackgroundColor(colorMode);
		notionPlot.Legend.OutlineColor = NotionBackgroundColor(colorMode);
		notionPlot.Legend.Alignment = Alignment.UpperCenter;
		notionPlot.Legend.Orientation = Orientation.Horizontal;
		notionPlot.Legend.TightHorizontalWrapping = false;
		notionPlot.Legend.SymbolPadding = 10;
		notionPlot.Legend.FontName = "gg sans Medium";
		notionPlot.Legend.ShadowFillStyle.IsVisible = false;
		notionPlot.Grid.LineColor = NotionForegroundColor(colorMode).WithAlpha(0.1);
		notionPlot.ShowLegend();
		return notionPlot;
	}
}
