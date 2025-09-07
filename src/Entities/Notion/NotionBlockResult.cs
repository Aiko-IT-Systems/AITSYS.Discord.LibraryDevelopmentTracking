// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

public class NotionBlockResult
{
	[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
	public string Object;

	[JsonProperty("results", NullValueHandling = NullValueHandling.Ignore)]
	public List<Result> Results;

	[JsonProperty("next_cursor", NullValueHandling = NullValueHandling.Ignore)]
	public object NextCursor;

	[JsonProperty("has_more", NullValueHandling = NullValueHandling.Ignore)]
	public bool? HasMore;

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public string Type;

	[JsonProperty("block", NullValueHandling = NullValueHandling.Ignore)]
	public Block TopBlock;

	[JsonProperty("developer_survey", NullValueHandling = NullValueHandling.Ignore)]
	public string DeveloperSurvey;

	[JsonProperty("request_id", NullValueHandling = NullValueHandling.Ignore)]
	public string RequestId;

	public class Annotations
	{
		[JsonProperty("bold", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Bold;

		[JsonProperty("italic", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Italic;

		[JsonProperty("strikethrough", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Strikethrough;

		[JsonProperty("underline", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Underline;

		[JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Code;

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color;
	}

	public class Block
	{
	}

	public class Callout
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichText;

		[JsonProperty("icon", NullValueHandling = NullValueHandling.Ignore)]
		public Icon Icon;

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color;
	}

	public class CreatedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object;

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;
	}

	public class Divider
	{
	}

	public class External
	{
		[JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
		public string Url;
	}

	public class Heading2
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<RichText> RichText;

		[JsonProperty("is_toggleable", NullValueHandling = NullValueHandling.Ignore)]
		public bool? IsToggleable;

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color;
	}

	public class Icon
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("emoji", NullValueHandling = NullValueHandling.Ignore)]
		public string Emoji;

		[JsonProperty("external", NullValueHandling = NullValueHandling.Ignore)]
		public External External;
	}

	public class LastEditedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object;

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;
	}

	public class Paragraph
	{
		[JsonProperty("rich_text", NullValueHandling = NullValueHandling.Ignore)]
		public List<object> RichText;

		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color;
	}

	public class Parent
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("page_id", NullValueHandling = NullValueHandling.Ignore)]
		public string PageId;
	}

	public class Result
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object;

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;

		[JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
		public Parent Parent;

		[JsonProperty("created_time", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? CreatedTime;

		[JsonProperty("last_edited_time", NullValueHandling = NullValueHandling.Ignore)]
		public DateTime? LastEditedTime;

		[JsonProperty("created_by", NullValueHandling = NullValueHandling.Ignore)]
		public CreatedBy CreatedBy;

		[JsonProperty("last_edited_by", NullValueHandling = NullValueHandling.Ignore)]
		public LastEditedBy LastEditedBy;

		[JsonProperty("has_children", NullValueHandling = NullValueHandling.Ignore)]
		public bool? HasChildren;

		[JsonProperty("archived", NullValueHandling = NullValueHandling.Ignore)]
		public bool? Archived;

		[JsonProperty("in_trash", NullValueHandling = NullValueHandling.Ignore)]
		public bool? InTrash;

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("table_of_contents", NullValueHandling = NullValueHandling.Ignore)]
		public TableOfContents TableOfContents;

		[JsonProperty("callout", NullValueHandling = NullValueHandling.Ignore)]
		public Callout Callout;

		[JsonProperty("divider", NullValueHandling = NullValueHandling.Ignore)]
		public Divider Divider;

		[JsonProperty("heading_2", NullValueHandling = NullValueHandling.Ignore)]
		public Heading2 Heading2;

		[JsonProperty("synced_block", NullValueHandling = NullValueHandling.Ignore)]
		public SyncedBlock SyncedBlock;

		[JsonProperty("paragraph", NullValueHandling = NullValueHandling.Ignore)]
		public Paragraph Paragraph;
	}

	public class RichText
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public Text Text;

		[JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
		public Annotations Annotations;

		[JsonProperty("plain_text", NullValueHandling = NullValueHandling.Ignore)]
		public string PlainText;

		[JsonProperty("href", NullValueHandling = NullValueHandling.Ignore)]
		public object Href;
	}

	public class SyncedBlock
	{
		[JsonProperty("synced_from", NullValueHandling = NullValueHandling.Ignore)]
		public object SyncedFrom;
	}

	public class TableOfContents
	{
		[JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
		public string Color;
	}

	public class Text
	{
		[JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
		public string Content;

		[JsonProperty("link", NullValueHandling = NullValueHandling.Ignore)]
		public object Link;
	}
}
