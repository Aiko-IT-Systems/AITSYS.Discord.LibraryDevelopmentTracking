// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

namespace AITSYS.Discord.LibraryDevelopmentTracking.Helpers;

internal static class LogSanitizer
{
	internal static string Sanitize(string? value)
	{
		if (string.IsNullOrEmpty(value))
			return value ?? string.Empty;

		return value
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\0", "\\0", StringComparison.Ordinal);
	}
}
