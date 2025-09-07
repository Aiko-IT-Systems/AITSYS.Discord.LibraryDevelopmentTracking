// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;

public class NotionDataSourceQueryResult
{
	[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
	public string Object { get; set; }

	[JsonProperty("results", NullValueHandling = NullValueHandling.Ignore)]
	public List<Result> Results { get; set; }

	[JsonProperty("next_cursor", NullValueHandling = NullValueHandling.Ignore)]
	public object NextCursor { get; set; }

	[JsonProperty("has_more", NullValueHandling = NullValueHandling.Ignore)]
	public bool? HasMore { get; set; }

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public string Type { get; set; }

	[JsonProperty("page_or_data_source", NullValueHandling = NullValueHandling.Ignore)]
	public object PageOrDataSource { get; set; }

	[JsonProperty("developer_survey", NullValueHandling = NullValueHandling.Ignore)]
	public string DeveloperSurvey { get; set; }

	[JsonProperty("request_id", NullValueHandling = NullValueHandling.Ignore)]
	public string RequestId { get; set; }

	public class Annotations
	{
		[JsonProperty("bold", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Bold { get; set; }

		[JsonProperty("italic", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Italic { get; set; }

		[JsonProperty("strikethrough", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Strikethrough { get; set; }

		[JsonProperty("underline", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Underline { get; set; }

		[JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Code { get; set; }

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color { get; set; }
	}

	public class CreatedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object { get; set; }

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }
	}

	public class DiscordBuildersSupport
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("checkbox", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Checkbox { get; set; }
	}

	public class ID
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("unique_id", NullValueHandling = NullValueHandling.Ignore)]
		public UniqueId UniqueId { get; set; }
	}

	public class Language
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichText { get; set; }
	}

	public class LastEditedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object { get; set; }

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }
	}

	public class Library
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
		public List<Title> Title { get; set; }
	}

	public class Notes
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichText { get; set; }
	}

	public class Parent
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("data_source_id", NullValueHandling = NullValueHandling.Ignore)]
		public string DataSourceId { get; set; }

		[JsonProperty("database_id", NullValueHandling = NullValueHandling.Ignore)]
		public string DatabaseId { get; set; }
	}

	public class Properties
	{
		[JsonProperty("Pull Request / Commit", NullValueHandling = NullValueHandling.Ignore)]
		public PullRequestCommit PullRequestCommit { get; set; }

		[JsonProperty("Language", NullValueHandling = NullValueHandling.Ignore)]
		public Language Language { get; set; }

		[JsonProperty("Status", NullValueHandling = NullValueHandling.Ignore)]
		public Status Status { get; set; }

		[JsonProperty("Notes", NullValueHandling = NullValueHandling.Ignore)]
		public Notes Notes { get; set; }

		[JsonProperty("discord.builders Support", NullValueHandling = NullValueHandling.Ignore)]
		public DiscordBuildersSupport DiscordBuildersSupport { get; set; }

		[JsonProperty("ID", NullValueHandling = NullValueHandling.Ignore)]
		public ID ID { get; set; }

		[JsonProperty("Released In Version", NullValueHandling = NullValueHandling.Ignore)]
		public ReleasedInVersion ReleasedInVersion { get; set; }

		[JsonProperty("Library", NullValueHandling = NullValueHandling.Ignore)]
		public Library Library { get; set; }
	}

	public class PullRequestCommit
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
		public string Url { get; set; }
	}

	public class ReleasedInVersion
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichText { get; set; }
	}

	public class Result
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object { get; set; }

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("created_time", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? CreatedTime { get; set; }

		[JsonProperty("last_edited_time", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? LastEditedTime { get; set; }

		[JsonProperty("created_by", NullValueHandling = NullValueHandling.Ignore)]
		public CreatedBy CreatedBy { get; set; }

		[JsonProperty("last_edited_by", NullValueHandling = NullValueHandling.Ignore)]
		public LastEditedBy LastEditedBy { get; set; }

		[JsonProperty("cover", NullValueHandling = NullValueHandling.Ignore)]
		public object Cover { get; set; }

		[JsonProperty("icon", NullValueHandling = NullValueHandling.Ignore)]
		public object Icon { get; set; }

		[JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
		public Parent Parent { get; set; }

		[JsonProperty("archived", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Archived { get; set; }

		[JsonProperty("in_trash", NullValueHandling = NullValueHandling.Ignore)]
		public bool? InTrash { get; set; }

		[JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
		public Properties Properties { get; set; }

		[JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
		public string Url { get; set; }

		[JsonProperty("public_url", NullValueHandling = NullValueHandling.Ignore)]
		public string PublicUrl { get; set; }
	}

	public class RichText
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public Text Text { get; set; }

		[JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
		public Annotations Annotations { get; set; }

		[JsonProperty("plain_text", NullValueHandling = NullValueHandling.Ignore)]
		public string PlainText { get; set; }

		[JsonProperty("href", NullValueHandling = NullValueHandling.Ignore)]
		public object Href { get; set; }
	}


	public class Status
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
		public InnerStatus InnerStatus { get; set; }
	}

	public class InnerStatus
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id { get; set; }

		[JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
		public string Name { get; set; }

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color { get; set; }
	}

	public class Text
	{
		[JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
		public string Content { get; set; }

		[JsonProperty("link", NullValueHandling = NullValueHandling.Ignore)]
		public object Link { get; set; }
	}

	public class Title
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type { get; set; }

		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public Text Text { get; set; }

		[JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
		public Annotations Annotations { get; set; }

		[JsonProperty("plain_text", NullValueHandling = NullValueHandling.Ignore)]
		public string PlainText { get; set; }

		[JsonProperty("href", NullValueHandling = NullValueHandling.Ignore)]
		public object Href { get; set; }
	}

	public class UniqueId
	{
		[JsonProperty("prefix", NullValueHandling = NullValueHandling.Ignore)]
		public object Prefix { get; set; }

		[JsonProperty("number", NullValueHandling = NullValueHandling.Ignore)]
		public int? Number { get; set; }
	}
}
