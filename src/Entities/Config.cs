// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

public class Config
{
	[JsonProperty("discord_config")]
	public DiscordConfig DiscordConfig { get; set; }

	[JsonProperty("notion_config")]
	public NotionConfig NotionConfig { get; set; }
}

public class DiscordConfig
{
	[JsonProperty("discord_token")]
	public string DiscordToken { get; set; }

	[JsonProperty("discord_guild")]
	public ulong DiscordGuild { get; set; }

	[JsonProperty("library_developer_role_id")]
	public ulong LibraryDeveloperRoleId { get; set; }

	[JsonProperty("library_role_mapping")]
	public Dictionary<ulong, string> LibraryRoleMapping { get; set; }
}

public class NotionConfig
{
	[JsonProperty("notion_token")]
	public string NotionToken { get; set; }

	[JsonProperty("notion_api_version")]
	public string NotionApiVersion { get; set; }

	[JsonProperty("implementation_tracking_config")]
	public IReadOnlyList<ImplementationTrackingConfig> ImplementationTrackingConfig { get; set; }

	[JsonProperty("notion_template_page_id")]
	public string NotionTemplatePageId { get; set; }
}

public class ImplementationTrackingConfig
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("page_id")]
	public string PageId { get; set; }

	[JsonProperty("database_id")]
	public string DatabaseId { get; set; }

	[JsonProperty("data_source_id")]
	public string DataSourceId { get; set; }
}
