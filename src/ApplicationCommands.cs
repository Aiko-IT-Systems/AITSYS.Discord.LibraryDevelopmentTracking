// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Interactivity.Extensions;

using ScottPlot;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

[SlashCommandGroup("library_tracking", "Commands for tracking library development", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel])]
public class LibraryTracking : ApplicationCommandsModule
{
	[SlashCommand("update_status", "Update the status of your library for given notion")]
	public async Task UpdateLibraryStatusAsync(InteractionContext ctx, [ChoiceProvider(typeof(NotionTrackingListProvider))][Option("notion", "The notion to update")] string notion, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);
		try
		{
			var check = await ctx.CheckAccessAsync(ctx.Client.Guilds[DiscordBot.Config.DiscordConfig.DiscordGuild], DiscordBot.Config.DiscordConfig);
			if (!check.HasAccess || check.Member is null || check.AllowedLibraries is null)
				return;

			var page = await DiscordBot.NotionRestClient.GetPageAsync(notion);
			var block = await DiscordBot.NotionRestClient.GetBlockChildrenAsync(notion);
			if (page is null || block is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID is not valid. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var pageTitle = page.PageProperties.Title.Titles[0].Text.Content.Trim();
			var pageCallout = block.Results.First(x => x.Type is "callout").Callout;
			var dataSource = await DiscordBot.NotionRestClient.GetDataSourceBySearchAsync(notion);
			if (dataSource is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID does not have a corresponding data source ID in the configuration. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var currentDatas = await dataSource.GetCurrentDataAsync([.. check.AllowedLibraries.Values.Select(x => x.Name)]);

			var selects = check.AllowedLibraries.GetLibrarySelects(currentDatas);
			var actionRows = selects.Select(select => new DiscordActionRowComponent([select]));
			var container = new DiscordContainerComponent([new DiscordSectionComponent([new($"{pageTitle.Header2()}"), new($"### Description\n{pageCallout.Icon.Emoji} {pageCallout.RichText[0].Text.Content}")]).WithThumbnailComponent($"https://www.emoji.family/api/emojis/{page.PageIcon.Emoji}/fluent/png/128"), new DiscordTextDisplayComponent("Please select the library to update".Header3()), .. actionRows], accentColor: DiscordColor.Green);
			var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container));

			var interactivity = ctx.Client.GetInteractivity();
			var result = await interactivity.WaitForSelectAsync(msg, x => x.User.Id == ctx.UserId && selects.Select(x => x.CustomId).Contains(x.Id), ComponentType.StringSelect);

			if (result.TimedOut)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("You took too long to respond. Please try again.")], accentColor: DiscordColor.Red)));
				return;
			}

			var selectedLibraryRoleId = ulong.Parse(result.Result.Values.First());
			var selectedLibrary = check.AllowedLibraries[selectedLibraryRoleId];
			var libraryData = currentDatas[selectedLibrary.Name];

			var currentStatus = libraryData.Results[0].Properties.Status.InnerStatus.Name;
			var prCommit = libraryData.Results[0].Properties.PullRequestCommit.Url;
			var version = libraryData.Results[0].Properties.ReleasedInVersion.RichText.Count is not 0 ? string.Join("", libraryData.Results[0].Properties.ReleasedInVersion.RichText.SelectMany(x => x.Text.Content)) : null;
			var notes = libraryData.Results[0].Properties.Notes.RichText.Count is not 0 ? string.Join("", libraryData.Results[0].Properties.Notes.RichText.SelectMany(x => x.Text.Content)) : null;

			var modalBuilder = new DiscordInteractionModalBuilder("Library Status Update");
			modalBuilder.AddTextDisplayComponent(new($"## Please adjust the data as needed\nThis will modify {selectedLibrary.Mention} in {pageTitle.Bold()}\n\n-# Last modification: {(libraryData.Results[0].LastEditedTime.HasValue ? libraryData.Results[0].LastEditedTime!.Value.Timestamp() : libraryData.Results[0].CreatedTime.HasValue ? libraryData.Results[0].CreatedTime!.Value.Timestamp() : "Unknown")}"));
			modalBuilder.AddLabelComponent(new("Status", "The status of the implementation", Utilities.GetStatusSelectMenuFromDataSource(dataSource, currentStatus)));
			modalBuilder.AddLabelComponent(new("Pull Request / Commit", "The pull request or commit implementing the changes / features", new DiscordTextInputComponent(TextComponentStyle.Small, "pr_commit", "Pull Request / Commit with implementation", null, null, false, prCommit)));
			modalBuilder.AddLabelComponent(new("Version", "The version number releasing the change / feature", new DiscordTextInputComponent(TextComponentStyle.Small, "version", "Released Version", null, null, false, version)));
			modalBuilder.AddLabelComponent(new("Details", "Additional notes (like delays, etc)", new DiscordTextInputComponent(TextComponentStyle.Paragraph, "notes", "Notes", null, null, false, notes)));
			await result.Result.Interaction.CreateInteractionModalResponseAsync(modalBuilder);
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Currently modifying {selectedLibrary.Name}.\nPlease fill out the modal to update the status.")], accentColor: DiscordColor.Orange)));

			var modalResult = await interactivity.WaitForModalAsync(modalBuilder.CustomId);
			if (modalResult.TimedOut)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("You took too long to respond. Please try again.")], accentColor: DiscordColor.Red)));
				return;
			}

			await modalResult.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

			var newStatus = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.Where(x => x.Component is DiscordStringSelectComponent y && y.CustomId is "status")
				.FirstOrDefault()?.Component as DiscordStringSelectComponent)?.SelectedValues?.FirstOrDefault();
			newStatus = string.IsNullOrWhiteSpace(newStatus) ? currentStatus : newStatus;
			var newPrCommit = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.Where(x => x.Component is DiscordTextInputComponent y && y.CustomId is "pr_commit")
				.FirstOrDefault()?.Component as DiscordTextInputComponent)?.Value;
			newPrCommit = string.IsNullOrWhiteSpace(newPrCommit) ? null : newPrCommit;
			var newVersion = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.Where(x => x.Component is DiscordTextInputComponent y && y.CustomId is "version")
				.FirstOrDefault()?.Component as DiscordTextInputComponent)?.Value;
			newVersion = string.IsNullOrWhiteSpace(newVersion) ? null : newVersion;
			var newNotes = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.Where(x => x.Component is DiscordTextInputComponent y && y.CustomId is "notes")
				.FirstOrDefault()?.Component as DiscordTextInputComponent)?.Value;
			newNotes = string.IsNullOrWhiteSpace(newNotes) ? null : newNotes;
			var res = await DiscordBot.NotionRestClient.UpdatePageAsync(libraryData.Results[0].Id, newStatus, newPrCommit, newVersion, newNotes);
			if (!res.Contains("error"))
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Successfully updated {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease allow some time for Notion to reflect the changes.")], accentColor: DiscordColor.Green)));
			else
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Failed to update {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease contact a server administrator.\n\n{res.BlockCode("cs")}")], accentColor: DiscordColor.DarkRed)));
		}
		catch (Exception ex)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("If you see this, notion probably fucked something up again. There API is so fucking cursed.")], accentColor: DiscordColor.DarkRed)));
			var user = await ctx.Client.GetUserAsync(856780995629154305);
			await user.SendMessageAsync($"Notion probably fucked something up again. Might need to take a look.\n{ex.Message.BlockCode("cs")}\n{ex.StackTrace?.BlockCode("cs") ?? string.Empty}");
		}
	}

	[SlashCommand("get_status", "Get the status of a library for given notion")]
	public async Task GetLibraryStatusAsync(InteractionContext ctx, [Autocomplete(typeof(DiscordLibraryListProvider)), Option("library", "The library to get the status from", true)] string library, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		try
		{
			await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);
			var libraryName = DiscordBot.Config.DiscordConfig.LibraryRoleMapping[ulong.Parse(library)];
			if (libraryName is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The selected library is not valid. Please contact a server administrator.")])));
				return;
			}
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"You selected {libraryName} for meow")])));
		}
		catch (Exception ex)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("If you see this, notion probably fucked something up again. There API is so fucking cursed.")], accentColor: DiscordColor.DarkRed)));
			var user = await ctx.Client.GetUserAsync(856780995629154305);
			await user.SendMessageAsync($"Notion probably fucked something up again. Might need to take a look.\n{ex.Message.BlockCode("cs")}\n{ex.StackTrace?.BlockCode("cs") ?? string.Empty}");
		}
	}

	[SlashCommand("statistics", "Get statistics for given notion")]
	public async Task GetStatisticsAsync(InteractionContext ctx, [ChoiceProvider(typeof(NotionTrackingListProvider))][Option("notion", "The notion to get the statistics for")] string notion, [Option("large_statistics", "Whether to display the charts large. Defaults to false.")] bool largeStatistics = false, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		try
		{
			await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);

			var page = await DiscordBot.NotionRestClient.GetPageAsync(notion);
			var block = await DiscordBot.NotionRestClient.GetBlockChildrenAsync(notion);
			if (page is null || block is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID is not valid. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var pageTitle = page.PageProperties.Title.Titles[0].Text.Content.Trim();
			var pageCallout = block.Results.First(x => x.Type is "callout").Callout;
			var dataSource = await DiscordBot.NotionRestClient.GetDataSourceBySearchAsync(notion);
			if (dataSource is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID does not have a corresponding data source ID in the configuration. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}
			var statuses = dataSource.GetStatuses();
			var statistics = await DiscordBot.NotionRestClient.GetStatisticInfosAsync(notion, statuses.Values, Utilities.GetIdsByDataSource(dataSource));
			var counts = statistics.GetStatisticsCounts();
			var languageBreakdown = statistics.GetLanguageSupportCounts();
			var piePlot = counts.GenerateNotionPieChart();
			var pieBytes = piePlot.GetImageBytes(1024, 768, ImageFormat.Webp);
			var pieMemoryStream = new MemoryStream(pieBytes)
			{
				Position = 0
			};
			var barPlot = languageBreakdown.GenerateNotionBarChart();
			var barBytes = barPlot.GetImageBytes(1024, 768, ImageFormat.Webp);
			var barMemoryStream = new MemoryStream(barBytes)
			{
				Position = 0
			};
			List<DiscordComponent> statisticComponents = largeStatistics
				? [new DiscordTextDisplayComponent("Implementation Statistic".Header3()), new DiscordMediaGalleryComponent([new("attachment://implementation_statistic.webp", "Implementation Statistic")]), new DiscordTextDisplayComponent("Language Support".Header3()), new DiscordMediaGalleryComponent([new("attachment://language_support.webp", "Language Support")])]
				: [new DiscordMediaGalleryComponent([new("attachment://implementation_statistic.webp", "Implementation Statistic"), new("attachment://language_support.webp", "Language Support")])];

			var container = new DiscordContainerComponent([new DiscordSectionComponent([new($"{pageTitle.Header2()}"), new($"### Description\n{pageCallout.Icon.Emoji} {pageCallout.RichText[0].Text.Content}")]).WithThumbnailComponent($"https://www.emoji.family/api/emojis/{page.PageIcon.Emoji}/fluent/png/128"), new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large), new DiscordTextDisplayComponent("Statistics".Header2()), .. statisticComponents, new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large), new DiscordActionRowComponent([new DiscordLinkButtonComponent(page.PublicUrl, "Open Notion", emoji: new DiscordComponentEmoji(1414062917137203383))])], accentColor: DiscordColor.Blue);
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container).AddFile("implementation_statistic.webp", pieMemoryStream).AddFile("language_support.webp", barMemoryStream));
		}
		catch (Exception ex)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("If you see this, notion probably fucked something up again. There API is so fucking cursed.")], accentColor: DiscordColor.DarkRed)));
			var user = await ctx.Client.GetUserAsync(856780995629154305);
			await user.SendMessageAsync($"Notion probably fucked something up again. Might need to take a look.\n{ex.Message.BlockCode("cs")}\n{ex.StackTrace?.BlockCode("cs") ?? string.Empty}");
		}
	}
}

[SlashCommandGroup("housekeeping", "Housekeeping commands for library tracking", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.PrivateChannel, InteractionContextType.BotDm], defaultMemberPermissions: (long)Permissions.Administrator)]
public class LibraryHouseKeeping : ApplicationCommandsModule
{

}
