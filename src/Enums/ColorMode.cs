// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using DisCatSharp.ApplicationCommands.Attributes;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Enums;

public enum ColorMode
{
	[ChoiceName("Light Mode")]
	Light,

	[ChoiceName("Dark Mode")]
	Dark
}
