// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;
public class NotionPageResult
{
	[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
	public string Object;

	[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
	public string Id;

	[JsonProperty("created_time", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? CreatedTime;

	[JsonProperty("last_edited_time", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? LastEditedTime;

	[JsonProperty("created_by", NullValueHandling = NullValueHandling.Ignore)]
	public CreatedBy CreatedByUser;

	[JsonProperty("last_edited_by", NullValueHandling = NullValueHandling.Ignore)]
	public LastEditedBy LastEditedByUser;

	[JsonProperty("cover", NullValueHandling = NullValueHandling.Ignore)]
	public object Cover;

	[JsonProperty("icon", NullValueHandling = NullValueHandling.Ignore)]
	public Icon PageIcon;

	[JsonProperty("archived", NullValueHandling = NullValueHandling.Ignore)]
	public bool? Archived;

	[JsonProperty("in_trash", NullValueHandling = NullValueHandling.Ignore)]
	public bool? InTrash;

	[JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
	public Properties PageProperties;

	[JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
	public string Url;

	[JsonProperty("public_url", NullValueHandling = NullValueHandling.Ignore)]
	public string PublicUrl;

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

	public class CreatedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object;

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;
	}

	public class Icon
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("emoji", NullValueHandling = NullValueHandling.Ignore)]
		public string Emoji;
	}

	public class LastEditedBy
	{
		[JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
		public string Object;

		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;
	}

	public class Parent
	{
		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("block_id", NullValueHandling = NullValueHandling.Ignore)]
		public string BlockId;
	}

	public class Properties
	{
		[JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
		public Title Title;
	}

	public class Text
	{
		[JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
		public string Content;

		[JsonProperty("link", NullValueHandling = NullValueHandling.Ignore)]
		public object Link;
	}

	public class Title
	{
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Id;

		[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
		public string Type;

		[JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
		public List<Title> Titles;

		[JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
		public Text Text;

		[JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
		public Annotations Annotations;

		[JsonProperty("plain_text", NullValueHandling = NullValueHandling.Ignore)]
		public string PlainText;

		[JsonProperty("href", NullValueHandling = NullValueHandling.Ignore)]
		public object Href;
	}

}
