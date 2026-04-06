// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Commands;

[SlashCommandGroup("housekeeping", "Housekeeping commands for library tracking", integrationTypes: [ApplicationCommandIntegrationTypes.GuildInstall, ApplicationCommandIntegrationTypes.UserInstall], allowedContexts: [InteractionContextType.Guild, InteractionContextType.PrivateChannel, InteractionContextType.BotDm], defaultMemberPermissions: (long)Permissions.Administrator)]
public class LibraryHouseKeepingCommands : ApplicationCommandsModule
{
	[SlashCommand("enable_notion", "Enable a notion to be selected by library maintainers")]
	public async Task EnableNotionAsync(InteractionContext ctx)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());
		try
		{
			var allDataSources = await DiscordBot.NotionRestClient.SearchAllDataSourcesAsync();
			var existingDataSourceIds = DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig.Select(x => x.DataSourceId).ToHashSet(StringComparer.InvariantCultureIgnoreCase);
			var newDataSources = allDataSources.Where(x => !existingDataSourceIds.Contains(x.Id)).ToList();

			if (newDataSources.Count is 0)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
					new DiscordTextDisplayComponent("No New Notions Found".Header2()),
					new DiscordTextDisplayComponent($"All {allDataSources.Count} discovered data sources are already enabled.\nIf you expected more, ensure the Notion integration has access to the relevant pages.")
				], accentColor: DiscordColor.Yellow)));
				return;
			}

			// Resolve page_ids by crawling the Notion parent chain (database → block → page)
			var templatePageId = DiscordBot.Configuration.NotionConfig.NotionTemplatePageId;
			var resolvedSources = new List<(Entities.Notion.NotionSearchDataSourceResult.DataSourceResult Ds, string PageId, string PageTitle, string DatabaseId)>();
			foreach (var ds in newDataSources)
			{
				var pageId = await DiscordBot.NotionRestClient.ResolvePageIdForDataSourceAsync(ds);
				if (string.IsNullOrWhiteSpace(pageId))
					continue;

				// Filter out the template page data source
				if (!string.IsNullOrWhiteSpace(templatePageId) && pageId.Equals(templatePageId, StringComparison.InvariantCultureIgnoreCase))
					continue;

				var pageTitle = await DiscordBot.NotionRestClient.GetPageTitleAsync(pageId) ?? "Untitled";
				resolvedSources.Add((ds, pageId, pageTitle, ds.Parent?.DatabaseId ?? "unknown"));
			}

			if (resolvedSources.Count is 0)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
					new DiscordTextDisplayComponent("No New Notions Found".Header2()),
					new DiscordTextDisplayComponent("All discoverable data sources are already enabled or belong to the template page.\nIf you expected more, ensure the Notion integration has access to the relevant pages.")
				], accentColor: DiscordColor.Yellow)));
				return;
			}

			var options = resolvedSources.Select(rs =>
				new DiscordStringSelectComponentOption(rs.PageTitle, rs.Ds.Id, $"Page: {rs.PageId[..Math.Min(rs.PageId.Length, 36)]}")
			).Take(25).ToList();

			var select = new DiscordStringSelectComponent("Select a notion to enable", options, minOptions: 1, maxOptions: 1);
			var container = new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Discover & Enable Notions".Header2()),
				new DiscordTextDisplayComponent($"Found {resolvedSources.Count} data source{(resolvedSources.Count is 1 ? "" : "s")} not yet enabled.\nSelect one to add:"),
				new DiscordActionRowComponent([select])
			], accentColor: DiscordColor.Blue);
			var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container));

			var interactivity = ctx.Client.GetInteractivity();
			var result = await interactivity.WaitForSelectAsync(msg, x => x.User.Id == ctx.UserId && x.Id == select.CustomId, ComponentType.StringSelect);

			if (result.TimedOut)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("You took too long to respond. Please try again.")], accentColor: DiscordColor.Red)));
				return;
			}

			await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

			var selectedDataSourceId = result.Result.Values.First();
			var (rDs, rPageId, rPageTitle, rDatabaseId) = resolvedSources.First(x => x.Ds.Id == selectedDataSourceId);

			var newEntry = new ImplementationTrackingConfig
			{
				Name = rPageTitle,
				PageId = rPageId,
				DatabaseId = rDatabaseId,
				DataSourceId = selectedDataSourceId
			};

			DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig.Add(newEntry);

			var configJson = Newtonsoft.Json.JsonConvert.SerializeObject(DiscordBot.Configuration, Newtonsoft.Json.Formatting.Indented);
			await File.WriteAllTextAsync("config.json", configJson);

			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Notion Enabled Successfully ✅".Header2()),
				new DiscordTextDisplayComponent($"{"Name".Bold()}: {rPageTitle}\n{"Page ID".Bold()}: {rPageId}\n{"Database ID".Bold()}: {rDatabaseId}\n{"Data Source ID".Bold()}: {selectedDataSourceId}"),
				new DiscordTextDisplayComponent("-# The notion is now available for library tracking commands.")
			], accentColor: DiscordColor.Green)));
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

	[SlashCommand("disable_notion", "Disable a notion from being selected by library maintainers")]
	public async Task DisableNotionAsync(InteractionContext ctx)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());
		try
		{
			var enabledNotions = DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig;
			if (enabledNotions.Count is 0)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
					new DiscordTextDisplayComponent("No Notions Enabled".Header2()),
					new DiscordTextDisplayComponent("There are no enabled notions to disable.")
				], accentColor: DiscordColor.Yellow)));
				return;
			}

			var options = enabledNotions.Select(n =>
				new DiscordStringSelectComponentOption(n.Name, n.DataSourceId, $"Page: {n.PageId[..Math.Min(n.PageId.Length, 36)]}")
			).Take(25).ToList();

			var select = new DiscordStringSelectComponent("Select a notion to disable", options, minOptions: 1, maxOptions: 1);
			var container = new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Disable a Notion".Header2()),
				new DiscordTextDisplayComponent($"Currently {enabledNotions.Count} notion{(enabledNotions.Count is 1 ? "" : "s")} enabled.\nSelect one to remove:"),
				new DiscordActionRowComponent([select])
			], accentColor: DiscordColor.Orange);
			var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container));

			var interactivity = ctx.Client.GetInteractivity();
			var result = await interactivity.WaitForSelectAsync(msg, x => x.User.Id == ctx.UserId && x.Id == select.CustomId, ComponentType.StringSelect);

			if (result.TimedOut)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("You took too long to respond. Please try again.")], accentColor: DiscordColor.Red)));
				return;
			}

			await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

			var selectedDataSourceId = result.Result.Values.First();
			var entry = enabledNotions.FirstOrDefault(x => x.DataSourceId == selectedDataSourceId);
			if (entry is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The selected notion was not found in the configuration.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			enabledNotions.Remove(entry);

			var configJson = Newtonsoft.Json.JsonConvert.SerializeObject(DiscordBot.Configuration, Newtonsoft.Json.Formatting.Indented);
			await File.WriteAllTextAsync("config.json", configJson);

			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Notion Disabled Successfully 🗑️".Header2()),
				new DiscordTextDisplayComponent($"{"Name".Bold()}: {entry.Name}\n{"Data Source ID".Bold()}: {entry.DataSourceId}"),
				new DiscordTextDisplayComponent("-# The notion has been removed from library tracking commands.")
			], accentColor: DiscordColor.Green)));
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

	[SlashCommand("create_notion", "Create a new tracking notion from template")]
	public async Task CreateNotionAsync(InteractionContext ctx)
	{
		var modalBuilder = new DiscordInteractionModalBuilder("Create New Tracking Notion");
		modalBuilder.AddLabelComponent(new("Title", "The title for the new tracking notion", new DiscordTextInputComponent(TextComponentStyle.Small, "notion_title", "e.g. Phase 5", null, 100, true)));
		modalBuilder.AddLabelComponent(new("Description", "Describe what this tracking notion covers", new DiscordTextInputComponent(TextComponentStyle.Paragraph, "notion_description", "Description of the tracking scope", null, 500, true)));
		modalBuilder.AddLabelComponent(new("Auto-Enable", "Enable this notion for tracking commands immediately", new DiscordCheckboxComponent("auto_enable", isDefault: true)));
		await ctx.Interaction.CreateInteractionModalResponseAsync(modalBuilder);

		var interactivity = ctx.Client.GetInteractivity();
		var modalResult = await interactivity.WaitForModalAsync(modalBuilder.CustomId);

		if (modalResult.TimedOut)
			return;

		await modalResult.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());

		try
		{
			var title = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "notion_title")?.Component as DiscordTextInputComponent)?.Value;
			var description = (modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordTextInputComponent y && y.CustomId is "notion_description")?.Component as DiscordTextInputComponent)?.Value;
			var autoEnableCheckbox = modalResult.Result.Interaction.Data.ModalComponents
				.OfType<DiscordLabelComponent>()
				.FirstOrDefault(x => x.Component is DiscordCheckboxComponent y && y.CustomId is "auto_enable")?.Component as DiscordCheckboxComponent;
			var autoEnable = autoEnableCheckbox?.Value ?? true;

			if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("Title and description are required.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			var fullTitle = $"{title} - Implementation Statuses";
			var parentPageId = DiscordBot.Configuration.NotionConfig.NotionParentPageId;
			var templatePageId = DiscordBot.Configuration.NotionConfig.NotionTemplatePageId;

			if (string.IsNullOrWhiteSpace(parentPageId) || string.IsNullOrWhiteSpace(templatePageId))
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("Parent page ID or template page ID is not configured. Set `notion_parent_page_id` and `notion_template_page_id` in config.json.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			if (!DiscordBot.NotionRestClient.HasV3Client)
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("V3 API not configured. Set `notion_user_token` and `notion_space_id` in config.json.\nThe internal API is required for template duplication.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			// Step 1: Duplicate template via v3 API
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Creating Notion...".Header2()),
				new DiscordTextDisplayComponent("⏳ Duplicating template (this takes a few seconds)...")
			], accentColor: DiscordColor.Blue)));

			var pageId = await DiscordBot.NotionRestClient.DuplicatePageAsync(templatePageId, parentPageId);
			if (string.IsNullOrWhiteSpace(pageId))
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("Failed to duplicate template page. The v3 API token may have expired.\nRefresh `notion_user_token` in config.json and try again.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			// Step 2: Wait for page to become visible via public API
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Creating Notion...".Header2()),
				new DiscordTextDisplayComponent("✅ Template duplicated\n⏳ Waiting for page to become available...")
			], accentColor: DiscordColor.Blue)));

			if (!await DiscordBot.NotionRestClient.WaitForPageVisibilityAsync(pageId))
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Duplication succeeded but page is not yet visible via public API.\nPage ID: {pageId}\nTry running `/housekeeping enable_notion` after a few seconds.")], accentColor: DiscordColor.Yellow)));
				return;
			}

			// Step 3: Rename the page
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Creating Notion...".Header2()),
				new DiscordTextDisplayComponent("✅ Template duplicated\n⏳ Renaming page...")
			], accentColor: DiscordColor.Blue)));

			var (renameSuccess, renameResult) = await DiscordBot.NotionRestClient.RenamePageAsync(pageId, fullTitle);

			// Step 4: Update description callout
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Creating Notion...".Header2()),
				new DiscordTextDisplayComponent($"{(renameSuccess ? "✅" : "⚠️")} Page rename {(renameSuccess ? "done" : "failed")}\n⏳ Updating description...")
			], accentColor: DiscordColor.Blue)));

			var descriptionUpdated = await DiscordBot.NotionRestClient.UpdateDescriptionCalloutAsync(pageId, description);

			// Step 5: Find Libraries data source ID
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
				new DiscordTextDisplayComponent("Creating Notion...".Header2()),
				new DiscordTextDisplayComponent($"{(renameSuccess ? "✅" : "⚠️")} Page rename\n{(descriptionUpdated ? "✅" : "⚠️")} Description update\n⏳ Discovering Libraries database...")
			], accentColor: DiscordColor.Blue)));

			var (databaseId, dataSourceId) = await DiscordBot.NotionRestClient.FindLibrariesDataSourceAsync(pageId);

			// Step 6: Move page into Phases section
			var moved = false;
			if (DiscordBot.NotionRestClient.HasV3Client)
			{
				await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
					new DiscordTextDisplayComponent("Creating Notion...".Header2()),
					new DiscordTextDisplayComponent($"{(renameSuccess ? "✅" : "⚠️")} Page rename\n{(descriptionUpdated ? "✅" : "⚠️")} Description update\n{(!string.IsNullOrWhiteSpace(dataSourceId) ? "✅" : "⚠️")} Libraries database\n⏳ Moving into Phases section...")
				], accentColor: DiscordColor.Blue)));

				var phasesBlockId = await DiscordBot.NotionRestClient.FindPhasesToggleBlockAsync(parentPageId);
				if (!string.IsNullOrWhiteSpace(phasesBlockId))
				{
					var lastPhase = await DiscordBot.NotionRestClient.FindLastChildBlockAsync(phasesBlockId);
					moved = await DiscordBot.NotionRestClient.MoveBlockAsync(pageId, parentPageId, phasesBlockId, lastPhase);
				}
				else
					Console.WriteLine("Phases toggle block not found in DLD page");
			}

			// Step 7: Build result and auto-enable
			var publicUrl = renameSuccess ? (renameResult["public_url"]?.ToString() ?? renameResult["url"]?.ToString()) : null;
			var statusParts = new List<string>
			{
				"✅ Template duplicated",
				renameSuccess ? "✅ Page renamed" : "⚠️ Page rename failed (rename manually)",
				descriptionUpdated ? "✅ Description updated" : "⚠️ Description update failed (update manually)"
			};

			if (!string.IsNullOrWhiteSpace(databaseId) && !string.IsNullOrWhiteSpace(dataSourceId))
				statusParts.Add("✅ Libraries database found");
			else
				statusParts.Add("⚠️ Libraries database not auto-detected (register manually)");

			if (moved)
				statusParts.Add("✅ Moved into Phases section");
			else
				statusParts.Add("⚠️ Could not auto-move into Phases (move manually)");

			if (autoEnable && !string.IsNullOrWhiteSpace(databaseId) && !string.IsNullOrWhiteSpace(dataSourceId))
			{
				var newEntry = new ImplementationTrackingConfig
				{
					Name = fullTitle,
					PageId = pageId,
					DatabaseId = databaseId,
					DataSourceId = dataSourceId
				};

				DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig.Add(newEntry);
				var configJson = Newtonsoft.Json.JsonConvert.SerializeObject(DiscordBot.Configuration, Newtonsoft.Json.Formatting.Indented);
				await File.WriteAllTextAsync("config.json", configJson);
				statusParts.Add("✅ Registered in config");
			}
			else if (autoEnable)
				statusParts.Add("⚠️ Auto-enable skipped (could not detect data source)");
			else
				statusParts.Add("ℹ️ Auto-enable skipped (unchecked)");

			var statusText = string.Join("\n", statusParts);

			var responseComponents = new List<DiscordComponent>
			{
				new DiscordTextDisplayComponent("Notion Created Successfully ✅".Header2()),
				new DiscordTextDisplayComponent(statusText),
				new DiscordSeparatorComponent(true, SeparatorSpacingSize.Small),
				new DiscordTextDisplayComponent($"{"Name".Bold()}: {fullTitle}\n{"Page ID".Bold()}: {pageId}\n{"Database ID".Bold()}: {databaseId ?? "unknown"}\n{"Data Source ID".Bold()}: {dataSourceId ?? "unknown"}")
			};

			if (!string.IsNullOrWhiteSpace(publicUrl))
				responseComponents.Add(new DiscordActionRowComponent([new DiscordLinkButtonComponent(publicUrl, "Open in Notion", emoji: new DiscordComponentEmoji(1414062917137203383))]));

			responseComponents.Add(new DiscordTextDisplayComponent(autoEnable && !string.IsNullOrWhiteSpace(dataSourceId)
				? $"-# The notion is now available for library tracking commands.{(moved ? "" : "\n-# ⚠️ Move the page into the Phases section in Notion manually.")}"
				: $"-# Use `/housekeeping enable_notion` to register this notion for tracking.{(moved ? "" : "\n-# ⚠️ Move the page into the Phases section in Notion manually.")}"));

			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent(responseComponents, accentColor: DiscordColor.Green)));
		}
		catch (DisCatSharpException)
		{
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("Discord oopsie")], accentColor: DiscordColor.DarkRed)));
		}
		catch (Exception ex)
		{
			await modalResult.Result.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("If you see this, notion probably fucked something up again. Their API is so fucking cursed.")], accentColor: DiscordColor.DarkRed)));
			var user = await ctx.Client.GetUserAsync(856780995629154305);
			await user.SendMessageAsync($"Notion probably fucked something up again. Might need to take a look.\n{ex.Message.BlockCode("cs")}\n{ex.StackTrace?.BlockCode("cs") ?? string.Empty}");
		}
	}

	[SlashCommand("slap_library", "Slap a library")]
	public async Task SlapLibraryAsync(InteractionContext ctx, [Autocomplete(typeof(DiscordLibraryListProvider)), Option("library", "The library to slap", true)] string library)
	{
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());
		try
		{
			if (!ulong.TryParse(library, out var libraryRoleId) || !DiscordBot.Configuration.DiscordConfig.LibraryRoleMapping.TryGetValue(libraryRoleId, out var libraryName) || libraryName is null)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent("The selected library is not valid. Please contact a server administrator.")], accentColor: DiscordColor.DarkRed)));
				return;
			}

			List<(string NotionTitle, string Status, string? PrUrl, string? Version, string? Notes)> pendingEntries = [];

			foreach (var notion in DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig)
			{
				try
				{
					var page = await DiscordBot.NotionRestClient.GetPageAsync(notion.PageId);
					var pageTitle = page?.PageProperties?.Title?.Titles?.FirstOrDefault()?.Text?.Content?.Trim() ?? notion.Name;

					var dataSource = await DiscordBot.NotionRestClient.GetDataSourceBySearchAsync(notion.PageId);
					if (dataSource is null)
						continue;

					var queryResult = await DiscordBot.NotionRestClient.QueryDataSourceAsync(dataSource.Id, libraryName);
					if (queryResult?.Results is null || queryResult.Results.Count is 0)
						continue;

					var status = queryResult.Results[0].Properties.Status.InnerStatus.Name;
					if (status is "Released")
						continue;

					var prUrl = queryResult.Results[0].Properties.PullRequestCommit.Url;
					var version = queryResult.Results[0].Properties.ReleasedInVersion.RichText.Count is not 0
						? string.Join("", queryResult.Results[0].Properties.ReleasedInVersion.RichText.SelectMany(x => x.Text.Content))
						: null;
					var notes = queryResult.Results[0].Properties.Notes.RichText.Count is not 0
						? string.Join("", queryResult.Results[0].Properties.Notes.RichText.SelectMany(x => x.Text.Content))
						: null;

					pendingEntries.Add((pageTitle, status, prUrl, version, notes));
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Skipping notion '{notion.Name}' due to error: {ex.Message}");
				}
			}

			if (pendingEntries.Count is 0)
			{
				await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(new DiscordContainerComponent([
					new DiscordTextDisplayComponent("All Caught Up! 🎉".Header2()),
					new DiscordTextDisplayComponent($"<@&{libraryRoleId}> is fully up to date across all tracked notions.\nNothing to slap here — great work!")
				], accentColor: DiscordColor.Green)));
				return;
			}

			List<DiscordComponent> components =
			[
				new DiscordTextDisplayComponent($"Hey <@&{libraryRoleId}>! Here's what still needs work 👀".Header2()),
				new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large)
			];

			foreach (var entry in pendingEntries)
			{
				var detail = $"{"Status".Bold()}: {entry.Status}";
				if (!string.IsNullOrWhiteSpace(entry.Notes))
					detail += $"\n{"Notes".Bold()}: {entry.Notes}";
				if (!string.IsNullOrWhiteSpace(entry.Version))
					detail += $"\n{"Version".Bold()}: {entry.Version}";

				components.Add(new DiscordTextDisplayComponent($"{entry.NotionTitle.Header3()}\n{detail}"));

				if (!string.IsNullOrWhiteSpace(entry.PrUrl))
					components.Add(new DiscordActionRowComponent([new DiscordLinkButtonComponent(entry.PrUrl, "View PR / Commit")]));

				components.Add(new DiscordSeparatorComponent(true, SeparatorSpacingSize.Large));
			}

			components.Add(new DiscordTextDisplayComponent($"-# {pendingEntries.Count} pending implementation{(pendingEntries.Count is 1 ? "" : "s")} found. Get on it!"));

			var container = new DiscordContainerComponent(components, accentColor: DiscordColor.Orange);
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents(container).WithAllowedMentions(Mentions.All));
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

	[SlashCommand("invite_user", "Invite a specific user")]
	public async Task InviteUserAsync(InteractionContext ctx, [Option("user", "The user to invite")] DiscordUser user)
	{
		var interactivity = ctx.Client.GetInteractivity();
		await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral());
		var roleSelect = new DiscordRoleSelectComponent("Select the roles to assign to the user", minOptions: 0, maxOptions: 25);
		var actionRow = new DiscordActionRowComponent([roleSelect]);
		var container = new DiscordContainerComponent([new DiscordTextDisplayComponent($"Please select the roles to assign to {user.Mention()}.\nDon't select anything for 20 seconds to skip roles."), actionRow], accentColor: DiscordColor.Blue);
		var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents([container]));
		var result = await interactivity.WaitForSelectAsync(msg, roleSelect.CustomId!, ComponentType.RoleSelect, TimeSpan.FromSeconds(20));
		var processed = false;
		if (result.TimedOut)
		{
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents([container.AddComponent(new DiscordTextDisplayComponent("Creating invite.."))]).DisableAllComponents());
			var invite = await ctx.Guild!.GetDefaultChannel()!.CreateInviteAsync(maxUses: 1, unique: true, targetUserIds: [user.Id]);
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
			await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithV2Components().AddComponents([container.AddComponent(new DiscordTextDisplayComponent("Creating invite.."))]).DisableAllComponents());
			var selectedRoleIds = result.Result.Values.Select(x => Convert.ToUInt64(x));
			var invite = await ctx.Guild!.GetDefaultChannel()!.CreateInviteAsync(maxUses: 1, unique: true, roleIds: [.. selectedRoleIds], targetUserIds: [user.Id]);
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
