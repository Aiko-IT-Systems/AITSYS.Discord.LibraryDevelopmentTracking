// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Entities;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

public class NotionTrackingListProvider : IAutocompleteProvider
{
	public Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
	{
		IEnumerable<DiscordApplicationCommandAutocompleteChoice> options;

		if (ctx.FocusedOption.Value is null || string.IsNullOrWhiteSpace(ctx.FocusedOption.Value.ToString()))
		{
			options = DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Name, x.PageId)).Take(25);
			return Task.FromResult(options.AsEnumerable());
		}

		var search = ctx.FocusedOption.Value.ToString()!.ToLowerInvariant();
		options = DiscordBot.Configuration.NotionConfig.ImplementationTrackingConfig
			.Where(x => x.Name.Contains(search, StringComparison.InvariantCultureIgnoreCase))
			.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Name, x.PageId))
			.Take(25);
		return Task.FromResult(options);
	}
}

public class DiscordLibraryListProvider : IAutocompleteProvider
{
	public Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
	{
		IEnumerable<DiscordApplicationCommandAutocompleteChoice> options;

		if (ctx.FocusedOption.Value is null || string.IsNullOrWhiteSpace(ctx.FocusedOption.Value.ToString()))
		{
			options = DiscordBot.Configuration.DiscordConfig.LibraryRoleMapping.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Value, x.Key.ToString())).Take(25);
			return Task.FromResult(options.AsEnumerable());
		}
		else
		{
			var search = ctx.FocusedOption.Value.ToString()!.ToLowerInvariant();
			options = DiscordBot.Configuration.DiscordConfig.LibraryRoleMapping
				.Where(x => x.Value.Contains(search, StringComparison.InvariantCultureIgnoreCase))
				.Select(x => new DiscordApplicationCommandAutocompleteChoice(x.Value, x.Key.ToString()))
				.Take(25);
		}
		return Task.FromResult(options);
	}
}
