// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using DisCatSharp;
using DisCatSharp.EventArgs;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

public static class Interactions
{
	public static async Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
	{
		await Task.CompletedTask;
	}
}
