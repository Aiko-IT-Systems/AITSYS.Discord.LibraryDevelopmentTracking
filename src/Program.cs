// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

namespace AITSYS.Discord.LibraryDevelopmentTracking;

internal class Program
{
	internal static CancellationTokenSource RestartLock = new();

	static void Main(string[] args)
	{
		Console.WriteLine("Hello, World!");
		while (!RestartLock.IsCancellationRequested)
		{
			Config? config;
			try
			{
				// TODO: Fill out the config.json
				// NOTE: This will only work for notion pages based on the following template: https://www.notion.so/marketplace/templates/discord-lib-devs-implementations-tracking
				var configContent = File.ReadAllText("config.json");
				config = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(configContent);
				if (config == null)
				{
					throw new Exception("Failed to load configuration.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error loading configuration: {ex.Message}");
				return;
			}

#if DEBUG
			DiscordBot bot = new(config, true);
#else
			DiscordBot bot = new(config, false);
#endif
			bot.StartAsync().GetAwaiter().GetResult();
			if (!RestartLock.IsCancellationRequested)
				Console.WriteLine("Restarting bot...");
		}
		Console.WriteLine("Bye!");
	}
}
