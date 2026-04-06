// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Enums;
using AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;

using ScottPlot;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Commands;

[SlashCommandGroup("dev", "Developer commands for library tracking", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.PrivateChannel, InteractionContextType.BotDm]), ApplicationCommandRequireTeamMember]
public class DevCommands : ApplicationCommandsModule
{
	[SlashCommand("cached_statistic", "Test")]
	public async Task CachedStatisticAsync(InteractionContext ctx, [Option("color_mode", "The color mode for the statistics")] ColorMode colorMode, [Option("large_statistics", "Whether to display the charts large. Defaults to false.")] bool largeStatistics = false, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to false.")] bool ephemeral = false)
	{
		try
		{
			await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);

			if (DummyCache.Page is null || DummyCache.Block is null || DummyCache.DataSource is null || DummyCache.Statistics is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The cache is not populated. Please use /library_tracking statistics first.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			var page = DummyCache.Page;
			var block = DummyCache.Block;
			if (page is null || block is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID is not valid. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var pageTitle = page.PageProperties.Title.Titles[0].Text.Content.Trim();
			var pageCallout = block.Results.First(x => x.Type is "callout").Callout;
			var dataSource = DummyCache.DataSource;
			if (dataSource is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID does not have a corresponding data source ID in the configuration. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var statuses = dataSource.GetStatuses();
			var statistics = DummyCache.Statistics!;
			var counts = statistics.GetStatisticsCounts();
			var languageBreakdown = statistics.GetLanguageSupportCounts();
			var piePlot = counts.GenerateNotionPieChart(colorMode);
			var pieBytes = piePlot.GetImageBytes(1024, 768, ImageFormat.Webp);
			var pieMemoryStream = new MemoryStream(pieBytes)
			{
				Position = 0
			};
			var barPlot = languageBreakdown.GenerateNotionBarChart(colorMode);
			var barBytes = barPlot.GetImageBytes(1024, 768, ImageFormat.Webp);
			var barMemoryStream = new MemoryStream(barBytes)
			{
				Position = 0
			};
			List<DiscordComponent> statisticComponents = largeStatistics
				? [new DiscordTextDisplayComponent("Implementation Statistic".Header3()), new DiscordMediaGalleryComponent([new("attachment://implementation_statistic.webp", "Implementation Statistic", colorMode is ColorMode.Light)]), new DiscordTextDisplayComponent("Language Support".Header3()), new DiscordMediaGalleryComponent([new("attachment://language_support.webp", "Language Support", colorMode is ColorMode.Light)])]
				: [new DiscordMediaGalleryComponent([new("attachment://implementation_statistic.webp", "Implementation Statistic", colorMode is ColorMode.Light), new("attachment://language_support.webp", "Language Support", colorMode is ColorMode.Light)])];

			var container = new DiscordContainerComponent([new DiscordSectionComponent([new($"{pageTitle.Header2()}"), new($"### Description\n{pageCallout.Icon.Emoji} {pageCallout.RichText[0].Text.Content}")]).WithThumbnailComponent($"https://www.emoji.family/api/emojis/{page.PageIcon.Emoji}/fluent/png/128"), new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large), new DiscordTextDisplayComponent("Statistics".Header2()), .. statisticComponents, new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large), new DiscordActionRowComponent([new DiscordLinkButtonComponent(page.PublicUrl, "Open Notion", emoji: new DiscordComponentEmoji(1414062917137203383))])], accentColor: new DiscordColor("#8692FE"));
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container).AddFile("implementation_statistic.webp", pieMemoryStream).AddFile("language_support.webp", barMemoryStream));
		}
		catch (DisCatSharpException)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("Discord oopsie")], accentColor: DiscordColor.DarkRed)));
		}
		catch (Exception ex)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("If you see this, notion probably fucked something up again. Their API is so fucking cursed.")], accentColor: DiscordColor.DarkRed)));
			var user = await ctx.Client.GetUserAsync(856780995629154305);
			await user.SendMessageAsync($"Notion probably fucked something up again. Might need to take a look.\n{ex.Message.BlockCode("cs")}\n{ex.StackTrace?.BlockCode("cs") ?? string.Empty}");
		}

	}
}
