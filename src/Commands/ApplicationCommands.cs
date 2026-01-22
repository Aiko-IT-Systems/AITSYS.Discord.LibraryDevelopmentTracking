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
using DisCatSharp.Enums.Core;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

using ScottPlot;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Commands;

[SlashCommandGroup("library_tracking", "Commands for tracking library development", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel])]
public class LibraryTracking : ApplicationCommandsModule
{
	[SlashCommand("update_status", "Update the status of your library for given notion"), SlashCommandCooldown(1, 60, CooldownBucketType.User, typeof(CooldownResponder))]
	public async Task UpdateLibraryStatusAsync(InteractionContext ctx, [ChoiceProvider(typeof(NotionTrackingListProvider))][Option("notion", "The notion to update")] string notion, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);
		try
		{
			var check = await ctx.CheckAccessAsync(DiscordBot.Config.DiscordConfig);
			if (!check.HasAccess || (check.Member is null && !check.IsAdmin) || check.AllowedLibraries is null)
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
			var container = new DiscordContainerComponent([new DiscordSectionComponent([new($"{pageTitle.Header2()}"), new($"### Description\n{pageCallout.Icon.Emoji} {pageCallout.RichText[0].Text.Content}")]).WithThumbnailComponent($"https://www.emoji.family/api/emojis/{page.PageIcon.Emoji}/fluent/png/128"), new DiscordTextDisplayComponent("Please select the library to update".Header3()), .. actionRows, new DiscordTextDisplayComponent(check.IsAdmin ? "Since you're a discord staff or server admin, you are able to modify every library" : "Based on your <id:customize> selected roles, we selected your library / libraries")], accentColor: DiscordColor.Green);
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
			var discordBuildersSupport = libraryData.Results[0].Properties.DiscordBuildersSupport.Checkbox;
			var lastModifiedBy = libraryData.Results[0].Properties.ModifiedBy.RichText.Count is not 0 ? Convert.ToUInt64(string.Join("", libraryData.Results[0].Properties.ModifiedBy.RichText.SelectMany(x => x.Text.Content))) : Convert.ToUInt64(856780995629154305);
			var lastModifiedByUser = await ctx.Client.GetUserAsync(lastModifiedBy);

			var modalBuilder = new DiscordInteractionModalBuilder("Library Status Update");
			modalBuilder.AddTextDisplayComponent(new($"## Please adjust the data as needed\nThis will modify {selectedLibrary.Mention} in {pageTitle.Bold()}.\nThe current data is filled out.\n\n-# Last edited by: {lastModifiedByUser.Mention()} ({lastModifiedBy})\n-# Last modification: {(libraryData.Results[0].LastEditedTime.HasValue ? libraryData.Results[0].LastEditedTime!.Value.Timestamp() : libraryData.Results[0].CreatedTime.HasValue ? libraryData.Results[0].CreatedTime!.Value.Timestamp() : "Unknown")}"));
			modalBuilder.AddLabelComponent(new("Status", "The status of the implementation", dataSource.GetStatusRadioSelectFromDataSource(currentStatus)));
			modalBuilder.AddLabelComponent(new("Pull Request / Commit", "The pull request or commit implementing the changes / features", new DiscordTextInputComponent(TextComponentStyle.Small, "pr_commit", "Pull Request / Commit with implementation", null, null, false, prCommit)));
			modalBuilder.AddLabelComponent(new("Version", "The version number releasing the change / feature", new DiscordTextInputComponent(TextComponentStyle.Small, "version", "Released Version", null, null, false, version)));
			modalBuilder.AddLabelComponent(new("Details", "Additional notes (like delays, etc)", new DiscordTextInputComponent(TextComponentStyle.Paragraph, "notes", "Notes", null, null, false, notes)));
			//modalBuilder.AddLabelComponent(new("Discord.builders Support", "Whether the implementation is added to discord.builder", new DiscordCheckboxComponent("discord_builders", discordBuildersSupport)));
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
				.FirstOrDefault(x => x.Component is DiscordRadioGroupComponent y && y.CustomId is "status")?.Component as DiscordRadioGroupComponent)?.SelectedValue;
			newStatus = string.IsNullOrWhiteSpace(newStatus) ? currentStatus : newStatus;
			var newPrCommit = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "pr_commit")?.Component as DiscordTextInputComponent)?.Value;
			newPrCommit = string.IsNullOrWhiteSpace(newPrCommit) ? null : newPrCommit;
			var newVersion = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "version")?.Component as DiscordTextInputComponent)?.Value;
			newVersion = string.IsNullOrWhiteSpace(newVersion) ? null : newVersion;
			var newNotes = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "notes")?.Component as DiscordTextInputComponent)?.Value;
			newNotes = string.IsNullOrWhiteSpace(newNotes) ? null : newNotes;
			var res = await DiscordBot.NotionRestClient.UpdatePageAsync(libraryData.Results[0].Id, ctx.UserId, newStatus, newPrCommit, newVersion, newNotes);
			if (!res.Contains("error"))
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Successfully updated {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease allow some time for Notion to reflect the changes.")], accentColor: DiscordColor.Green)));
			else
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Failed to update {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease contact a server administrator.\n\n{res.BlockCode("cs")}")], accentColor: DiscordColor.DarkRed)));
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

	[SlashCommand("i_dont_use_canary", "Update the status of your library for given notion"), SlashCommandCooldown(1, 60, CooldownBucketType.User, typeof(CooldownResponder))]
	public async Task IDontUseCanaryAsync(InteractionContext ctx, [ChoiceProvider(typeof(NotionTrackingListProvider))][Option("notion", "The notion to update")] string notion, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);
		try
		{
			var check = await ctx.CheckAccessAsync(DiscordBot.Config.DiscordConfig);
			if (!check.HasAccess || (check.Member is null && !check.IsAdmin) || check.AllowedLibraries is null)
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
			var container = new DiscordContainerComponent([new DiscordSectionComponent([new($"{pageTitle.Header2()}"), new($"### Description\n{pageCallout.Icon.Emoji} {pageCallout.RichText[0].Text.Content}")]).WithThumbnailComponent($"https://www.emoji.family/api/emojis/{page.PageIcon.Emoji}/fluent/png/128"), new DiscordTextDisplayComponent("Please select the library to update".Header3()), .. actionRows, new DiscordTextDisplayComponent(check.IsAdmin ? "Since you're a discord staff or server admin, you are able to modify every library" : "Based on your <id:customize> selected roles, we selected your library / libraries")], accentColor: DiscordColor.Green);
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
			var discordBuildersSupport = libraryData.Results[0].Properties.DiscordBuildersSupport.Checkbox;
			var lastModifiedBy = libraryData.Results[0].Properties.ModifiedBy.RichText.Count is not 0 ? Convert.ToUInt64(string.Join("", libraryData.Results[0].Properties.ModifiedBy.RichText.SelectMany(x => x.Text.Content))) : Convert.ToUInt64(856780995629154305);
			var lastModifiedByUser = await ctx.Client.GetUserAsync(lastModifiedBy);

			var modalBuilder = new DiscordInteractionModalBuilder("Library Status Update");
			modalBuilder.AddTextDisplayComponent(new($"## Please adjust the data as needed\nThis will modify {selectedLibrary.Mention} in {pageTitle.Bold()}.\nThe current data is filled out.\n\n-# Last edited by: {lastModifiedByUser.Mention()} ({lastModifiedBy})\n-# Last modification: {(libraryData.Results[0].LastEditedTime.HasValue ? libraryData.Results[0].LastEditedTime!.Value.Timestamp() : libraryData.Results[0].CreatedTime.HasValue ? libraryData.Results[0].CreatedTime!.Value.Timestamp() : "Unknown")}"));
			modalBuilder.AddLabelComponent(new("Status", "The status of the implementation", dataSource.GetStatusSelectMenuFromDataSource(currentStatus)));
			modalBuilder.AddLabelComponent(new("Pull Request / Commit", "The pull request or commit implementing the changes / features", new DiscordTextInputComponent(TextComponentStyle.Small, "pr_commit", "Pull Request / Commit with implementation", null, null, false, prCommit)));
			modalBuilder.AddLabelComponent(new("Version", "The version number releasing the change / feature", new DiscordTextInputComponent(TextComponentStyle.Small, "version", "Released Version", null, null, false, version)));
			modalBuilder.AddLabelComponent(new("Details", "Additional notes (like delays, etc)", new DiscordTextInputComponent(TextComponentStyle.Paragraph, "notes", "Notes", null, null, false, notes)));
			//modalBuilder.AddLabelComponent(new("Discord.builders Support", "Whether the implementation is added to discord.builder", new DiscordCheckboxComponent("discord_builders", discordBuildersSupport)));
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
				.FirstOrDefault(x => x.Component is DiscordStringSelectComponent y && y.CustomId is "status")?.Component as DiscordStringSelectComponent)?.SelectedValues?.FirstOrDefault();;
			newStatus = string.IsNullOrWhiteSpace(newStatus) ? currentStatus : newStatus;
			var newPrCommit = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "pr_commit")?.Component as DiscordTextInputComponent)?.Value;
			newPrCommit = string.IsNullOrWhiteSpace(newPrCommit) ? null : newPrCommit;
			var newVersion = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "version")?.Component as DiscordTextInputComponent)?.Value;
			newVersion = string.IsNullOrWhiteSpace(newVersion) ? null : newVersion;
			var newNotes = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "notes")?.Component as DiscordTextInputComponent)?.Value;
			newNotes = string.IsNullOrWhiteSpace(newNotes) ? null : newNotes;
			var res = await DiscordBot.NotionRestClient.UpdatePageAsync(libraryData.Results[0].Id, ctx.UserId, newStatus, newPrCommit, newVersion, newNotes);
			if (!res.Contains("error"))
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Successfully updated {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease allow some time for Notion to reflect the changes.")], accentColor: DiscordColor.Green)));
			else
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Failed to update {selectedLibrary.Mention} in {pageTitle.Bold()}.\n\nPlease contact a server administrator.\n\n{res.BlockCode("cs")}")], accentColor: DiscordColor.DarkRed)));
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

	[SlashCommand("get_status", "Get the status of a library for all notions"), SlashCommandCooldown(5, 60, CooldownBucketType.Global, typeof(CooldownResponder))]
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

	[SlashCommand("statistics", "Get statistics for given notion"), SlashCommandCooldown(5, 60, CooldownBucketType.Global)]
	public async Task GetStatisticsAsync(InteractionContext ctx, [ChoiceProvider(typeof(NotionTrackingListProvider))][Option("notion", "The notion to get the statistics for")] string notion, [Option("color_mode", "The color mode for the statistics")] ColorMode colorMode, [Option("large_statistics", "Whether to display the charts large. Defaults to false.")] bool largeStatistics = false, [Option("ephemeral", "Whether to hide the output from public (only you can see it). Defaults to true.")] bool ephemeral = true)
	{
		try
			{
				await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, ephemeral ? new DiscordInteractionResponseBuilder().AsEphemeral() : null);
				DummyCache.Notion = notion;
				var page = await DiscordBot.NotionRestClient.GetPageAsync(notion);
				DummyCache.Page = page;
				var block = await DiscordBot.NotionRestClient.GetBlockChildrenAsync(notion);
				DummyCache.Block = block;
				if (page is null || block is null)
				{
					await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID is not valid. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
					return;
				}
				var pageTitle = page.PageProperties.Title.Titles[0].Text.Content.Trim();
				var pageCallout = block.Results.First(x => x.Type is "callout").Callout;
				var dataSource = await DiscordBot.NotionRestClient.GetDataSourceBySearchAsync(notion);
				DummyCache.DataSource = dataSource;
				if (dataSource is null)
				{
					await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The provided notion page ID does not have a corresponding data source ID in the configuration. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
					return;
				}
				var statuses = dataSource.GetStatuses();
				var statistics = await DiscordBot.NotionRestClient.GetStatisticInfosAsync(notion, statuses.Values, dataSource.GetIdsByDataSource());
				DummyCache.Statistics = statistics;
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

[SlashCommandGroup("housekeeping", "Housekeeping commands for library tracking", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.PrivateChannel, InteractionContextType.BotDm], defaultMemberPermissions: (long)Permissions.Administrator)]
public class LibraryHouseKeeping : ApplicationCommandsModule
{
	[SlashCommand("enable_notion", "Enable a notion to be selected by library maintainers")]
	public async Task EnableNotionAsync(InteractionContext ctx, [Option("name", "The name of the notion")] string name)
	{
		// Implementation for enabling a notion
		var notionName = $"{name.Trim()} - Implementation Statuses";
		await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithContent($"DUMMY: You have enabled a new notion: {notionName}\n-# This is not implemented yet!"));
	}

	[SlashCommand("slap_library", "Slap a library")]
	public async Task SlapLibraryAsync(InteractionContext ctx, [Autocomplete(typeof(DiscordLibraryListProvider)), Option("library", "The library to slap", true)] string library)
	{
		// Implementation for slapping a library
		await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithContent($"DUMMY: You slapped the library: {library}\n-# This is not implemented yet!"));
	}

	[SlashCommand("invite_user", "Invite a specific user")]
	public async Task InviteUserAsync(InteractionContext ctx, [Option("user", "The user to invite")] DiscordUser user)
	{
		var interactivity = ctx.Client.GetInteractivity();
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());
		var roleSelect = new DiscordRoleSelectComponent("Select the roles to assign to the user", minOptions:0, maxOptions: 25);
		var actionRow = new DiscordActionRowComponent([roleSelect]);
		var container = new DiscordContainerComponent([new DiscordTextDisplayComponent($"Please select the roles to assign to {user.Mention()}.\nDon't select anything for 20 seconds to skip roles."), actionRow], accentColor: DiscordColor.Blue);
		var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents([container]));
		var result = await interactivity.WaitForSelectAsync(msg, roleSelect.CustomId!, ComponentType.RoleSelect, TimeSpan.FromSeconds(20));
		var processed = false;
		if (result.TimedOut)
		{
			var invite = await ctx.Channel.CreateInviteAsync(maxUses: 1, unique: true, targetUserIds: [user.Id]);
			while (!processed)
			{
				var jobStatus = await ctx.Client.GetInviteTargetUsersJobStatusAsync(invite.Code);
				if (jobStatus.Status is InviteTargetUsersJobStatus.Completed)
				{
					processed = true;
					break;
				}
				else if (jobStatus.Status is InviteTargetUsersJobStatus.Failed)
				{
					await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Failed to create an invite for {user.Mention()}: {jobStatus.ErrorMessage!.BlockCode("json")}.")], accentColor: DiscordColor.Red)).WithAllowedMentions(Mentions.None));
					return;
				}
				await Task.Delay(5000);
			}
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"You did not select any roles for {user.Mention()}.\nHere is their invite link: {invite.Url}")], accentColor: DiscordColor.Yellow)).WithAllowedMentions(Mentions.None));
		}
		else
		{
			var interaction = result.Result.Interaction;
			await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
			var selectedRoleIds = result.Result.Values.Select(x => Convert.ToUInt64(x));
			var invite = await ctx.Channel.CreateInviteAsync(maxUses: 1, unique: true, roleIds: [..selectedRoleIds], targetUserIds: [user.Id]);
			while (!processed)
			{
				var jobStatus = await ctx.Client.GetInviteTargetUsersJobStatusAsync(invite.Code);
				if (jobStatus.Status is InviteTargetUsersJobStatus.Completed)
				{
					processed = true;
					break;
				}
				else if (jobStatus.Status is InviteTargetUsersJobStatus.Failed)
				{
					await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Failed to create an invite for {user.Mention()}: {jobStatus.ErrorMessage!.BlockCode("json")}.")], accentColor: DiscordColor.Red)).WithAllowedMentions(Mentions.None));
					return;
				}
				await Task.Delay(5000);
			}
			await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"You have selected the roles <@&{string.Join(">, <@&", selectedRoleIds)}>\nHere is their invite link: {invite.Url}")], accentColor: DiscordColor.Green)).WithAllowedMentions(Mentions.None));
		}
	}
}

[SlashCommandGroup("dev", "Developer commands for library tracking", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.PrivateChannel, InteractionContextType.BotDm]), ApplicationCommandRequireTeamMember]
public class Dev : ApplicationCommandsModule
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
