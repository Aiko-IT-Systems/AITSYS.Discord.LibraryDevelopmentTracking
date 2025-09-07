// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

public class NotionTrackingListProvider : IChoiceProvider
{
	public Task<IEnumerable<DiscordApplicationCommandOptionChoice>> Provider()
	{
		var options = DiscordBot.Config.NotionConfig.ImplementationTrackingConfig.Select(x => new DiscordApplicationCommandOptionChoice(x.Name, x.PageId));
		return Task.FromResult(options.AsEnumerable());
	}
}

public class DiscordLibraryListProvider : IAutocompleteProvider
{
	public Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
	{
		IEnumerable<DiscordApplicationCommandAutocompleteChoice> options;

		if (ctx.FocusedOption.Value is null || string.IsNullOrWhiteSpace(ctx.FocusedOption.Value.ToString()))
		{
			options = DiscordBot.Config.DiscordConfig.LibraryRoleMapping.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Value, x.Key.ToString())).Take(25);
			return Task.FromResult(options.AsEnumerable());
		}
		else
		{
			var search = ctx.FocusedOption.Value.ToString()!.ToLowerInvariant();
			options = DiscordBot.Config.DiscordConfig.LibraryRoleMapping
				.Where(x => x.Value.Contains(search, StringComparison.InvariantCultureIgnoreCase))
				.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Value, x.Key.ToString()))
				.Take(25);
		}
		return Task.FromResult(options);
	}
}
