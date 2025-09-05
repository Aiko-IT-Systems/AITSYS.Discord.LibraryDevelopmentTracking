// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

namespace AITSYS.Discord.LibraryDevelopmentTracking;

internal class Program
{
	static void Main(string[] args)
	{
		Console.WriteLine("Hello, World!");
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

		DiscordBot bot = new(config);
		bot.StartAsync().GetAwaiter().GetResult();
		Console.WriteLine("Bye!");
	}
}
