// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Rest;

public class LibraryUpdatePayload
{
	[JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
	public Properties UpdateProperties { get; set; }

	public class Properties
	{
		[JsonProperty("Pull Request / Commit", NullValueHandling = NullValueHandling.Ignore)]
		public PullRequestCommit PullRequestCommit { get; set; }

		[JsonProperty("Status", NullValueHandling = NullValueHandling.Ignore)]
		public Status Status { get; set; }

		[JsonProperty("Released In Version", NullValueHandling = NullValueHandling.Ignore)]
		public ReleasedInVersion ReleasedInVersion { get; set; }

		[JsonProperty("Notes", NullValueHandling = NullValueHandling.Ignore)]
		public Notes Notes { get; set; }

		[JsonProperty("Modified By", NullValueHandling = NullValueHandling.Ignore)]
		public ModifiedBy ModifiedBy { get; set; }
	}

	public class Notes
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichTexts { get; set; } = [];
	}

	public class PullRequestCommit
	{
		[JsonProperty("url", NullValueHandling = NullValueHandling.Include)]
		public string? Url { get; set; }
	}

	public class ReleasedInVersion
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichTexts { get; set; } = [];
	}

	public class ModifiedBy
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichTexts { get; set; } = [];
	}

	public class RichText
	{
		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public TextContent TextContent { get; set; }
	}

	public class Status
	{
		[JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
		public StatusValue StatusValue { get; set; }
	}

	public class StatusValue
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }
	}

	public class TextContent
	{
		[JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
		public string Content { get; set; }
	}
}
