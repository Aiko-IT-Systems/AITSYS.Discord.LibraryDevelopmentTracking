// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

internal class Program
{
	internal static CancellationTokenSource RestartLock = new();

	static async Task Main(string[] args)
	{
		Console.WriteLine("Hello, World!");
		while (!RestartLock.IsCancellationRequested)
		{
			Config? config;
			try
			{
				var configContent = await File.ReadAllTextAsync("config.json");
				config = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(configContent) ?? throw new Exception("Failed to load configuration.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error loading configuration: {ex.Message}");
				return;
			}

#if DEBUG
			DiscordBot bot = new(config, false);
#else
			DiscordBot bot = new(config, false);
#endif
			await bot.StartAsync();
			if (!RestartLock.IsCancellationRequested)
				Console.WriteLine("Restarting bot...");
		}
		Console.WriteLine("Bye!");
	}
}
