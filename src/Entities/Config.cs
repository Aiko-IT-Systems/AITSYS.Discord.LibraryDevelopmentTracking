// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities;

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

	[JsonProperty("discord_guilds")]
	public List<ulong> DiscordGuilds { get; set; }

	[JsonProperty("library_developer_role_id")]
	public ulong LibraryDeveloperRoleId { get; set; }

	[JsonProperty("library_role_mapping")]
	public Dictionary<ulong, string> LibraryRoleMapping { get; set; }

	[JsonProperty("discord_client_secret")]
	public string DiscordClientSecret { get; set; }

	[JsonProperty("discord_public_key")]
	public string DiscordPublicKey { get; set; }

	[JsonProperty("port")]
	public int Port { get; set; }

	[JsonProperty("public_host")]
	public string PublicHost { get; set; }

	[JsonProperty("allow_local_dev")]
	public bool AllowLocalDev { get; set; }

	[JsonProperty("enable_whitelist")]
	public bool EnableWhitelist { get; set; }

	[JsonProperty("allowed_user_ids")]
	public List<ulong> AllowedUserIds { get; set; } = [];

	[JsonProperty("allowed_guild_ids")]
	public List<ulong> AllowedGuildIds { get; set; } = [];

	[JsonProperty("session_ttl_minutes")]
	public int SessionTtlMinutes { get; set; }
}

public class NotionConfig
{
	[JsonProperty("notion_token")]
	public string NotionToken { get; set; }

	[JsonProperty("notion_api_version")]
	public string NotionApiVersion { get; set; }

	[JsonProperty("implementation_tracking_config")]
	public List<ImplementationTrackingConfig> ImplementationTrackingConfig { get; set; }

	[JsonProperty("notion_template_page_id")]
	public string NotionTemplatePageId { get; set; }

	[JsonProperty("notion_parent_page_id")]
	public string NotionParentPageId { get; set; }

	[JsonProperty("notion_user_token")]
	public string? NotionUserToken { get; set; }

	[JsonProperty("notion_space_id")]
	public string? NotionSpaceId { get; set; }

	[JsonProperty("notion_user_id")]
	public string? NotionUserId { get; set; }
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
