// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DisCatSharp;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.ApplicationCommands.Entities;
using DisCatSharp.Entities;
using DisCatSharp.Entities.Core;
using DisCatSharp.Enums;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Commands;

public sealed class CooldownResponder : ICooldownResponder
{
	public async Task Responder(BaseContext context, CooldownBucket cooldownBucket)
		=> await context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithV2Components().AddComponents(new DiscordContainerComponent([new DiscordTextDisplayComponent($"Ratelimit hit ({cooldownBucket.RemainingUses}/{cooldownBucket.MaxUses})\nPlease try again {cooldownBucket.ResetsAt.Timestamp()}"), new DiscordTextDisplayComponent("-# Due to excessive notion api calls we have to limit general usage")], accentColor: DiscordColor.Orange)));
}
