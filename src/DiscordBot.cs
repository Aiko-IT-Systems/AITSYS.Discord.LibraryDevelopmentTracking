// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Net;

using AITSYS.Discord.LibraryDevelopmentTracking.Commands;
using AITSYS.Discord.LibraryDevelopmentTracking.Events;
using AITSYS.Discord.LibraryDevelopmentTracking.Helpers;
using AITSYS.Discord.LibraryDevelopmentTracking.Rest;

using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.Enums;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Enums;
using DisCatSharp.Interactivity.Extensions;

using Microsoft.Extensions.Logging;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

public sealed class DiscordBot
{
	internal static Config Config { get; private set; }

	internal static NotionRestClient NotionRestClient { get; private set; }

	internal DiscordClient DiscordClient { get; private set; }

	internal ApplicationCommandsExtension ApplicationCommandsExtension { get; private set; }

	internal InteractivityExtension InteractivityExtension { get; private set; }

	public static CancellationTokenSource CancellationTokenSource { get; } = new();

	public DiscordBot(Config config)
	{
		ArgumentNullException.ThrowIfNull(config);
		Config = config;
		WebProxy? proxy = null; // new WebProxy("127.0.0.1", 8000);
		NotionRestClient = new NotionRestClient(Config.NotionConfig, proxy);
		this.DiscordClient = new DiscordClient(new DiscordConfiguration()
		{
			Token = Config.DiscordConfig.DiscordToken,
			TokenType = TokenType.Bot,
			Intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers | DiscordIntents.MessageContent,
			ApiChannel = ApiChannel.Canary,
			MinimumLogLevel = LogLevel.Debug,
			AutoReconnect = true,
			ReconnectIndefinitely = true,
			ReportMissingFields = false,
			EnableSentry = false,
			DisableUpdateCheck = true,
			Proxy = proxy
		});
		this.ApplicationCommandsExtension = this.DiscordClient.UseApplicationCommands(new()
		{
			EnableDefaultHelp = false,
			EnableDefaultUserAppsHelp = true,
			DebugStartup = true,
			CheckAllGuilds = false,
			EnableLocalization = false
		});
		this.InteractivityExtension = this.DiscordClient.UseInteractivity(new()
		{
			PaginationBehaviour = PaginationBehaviour.WrapAround,
			PaginationDeletion = PaginationDeletion.DeleteMessage,
			PollBehaviour = PollBehaviour.DeleteEmojis,
			AckPaginationButtons = true
		});
		this.Setup();
	}

	public void Setup()
	{
		this.DiscordClient.ComponentInteractionCreated += Interactions.ComponentInteractionCreated;
		this.DiscordClient.Ready += async (client, args) => _ = await client.Guilds[Config.DiscordConfig.DiscordGuild].GetAllMembersAsync();
		this.ApplicationCommandsExtension.RegisterGlobalCommands<LibraryTracking>();
		this.ApplicationCommandsExtension.RegisterGlobalCommands<LibraryHouseKeeping>();
		this.ApplicationCommandsExtension.RegisterGlobalCommands<Dev>();
	}

	public async Task StartAsync()
	{
		await this.DiscordClient.ConnectAsync();
		await DummyCache.InitAsync();
		while (!CancellationTokenSource.IsCancellationRequested)
		{
			await Task.Delay(1000);
		}
		await this.DiscordClient.DisconnectAsync();
	}
}
