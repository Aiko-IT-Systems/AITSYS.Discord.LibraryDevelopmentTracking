// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Net;
using System.Reflection;
using System.Text;

using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Rest;

public sealed class NotionRestClient
{
	private NotionConfig CONFIG { get; init; }
	private HttpClient HTTP_CLIENT { get; init; }

	public NotionRestClient(NotionConfig config, WebProxy? proxy = null)
	{
		this.CONFIG = config;
		this.HTTP_CLIENT = new HttpClient(new HttpClientHandler()
		{
			Proxy = proxy
		});
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("Authorization", "Bearer " + this.CONFIG.NotionToken);
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("Notion-Version", this.CONFIG.NotionApiVersion);
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("User-Agent", $"AITSYS-Discord-LibraryDevelopmentTracking (https://github.com/Aiko-IT-Systems/AITSYS.Discord.LibraryDevelopmentTracking, v{Assembly.GetExecutingAssembly().GetName().Version})");
	}

	internal async Task<NotionBlockResult?> GetBlockChildrenAsync(string notion)
	{
		Console.WriteLine($"Getting Notion block children for notion page ID {notion}");
		var result = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{notion}/children");
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine("Notion block children retrieved");
		return JsonConvert.DeserializeObject<NotionBlockResult>(content);
	}

	internal async Task<NotionPageResult?> GetPageAsync(string notion)
	{
		Console.WriteLine($"Getting Notion page for notion page ID {notion}");
		var result = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/pages/{notion}");
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine("Notion page retrieved");
		return JsonConvert.DeserializeObject<NotionPageResult>(content);
	}

	internal async Task<NotionSearchDataSourceResult.DataSourceResult?> GetDataSourceBySearchAsync(string notion)
	{
		Console.WriteLine($"Searching Notion data source for notion page ID {notion}");
		var targetDataSource = this.CONFIG.ImplementationTrackingConfig.FirstOrDefault(x => x.PageId.Equals(notion, StringComparison.InvariantCultureIgnoreCase))?.DataSourceId;
		if (string.IsNullOrWhiteSpace(targetDataSource))
			throw new ArgumentException("The provided notion page ID does not have a corresponding data source ID in the configuration.", nameof(notion));
		var allDataSources = await SearchAllDataSourcesAsync();
		Console.WriteLine("Notion searched");
		return allDataSources.FirstOrDefault(x => x.Id.Equals(targetDataSource, StringComparison.InvariantCultureIgnoreCase));
	}

	private static readonly HashSet<string> s_trackingSchemaColumns = ["Library", "Status", "Pull Request / Commit", "Modified By"];

	internal async Task<List<NotionSearchDataSourceResult.DataSourceResult>> SearchAllDataSourcesAsync()
	{
		Console.WriteLine("Searching Notion for all data sources");
		var payload = @"{
			""filter"": {
				""value"": ""data_source"",
				""property"": ""object""
			}
		}";
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/search", new StringContent(payload, Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		var res = JsonConvert.DeserializeObject<NotionSearchDataSourceResult>(content);
		var all = res?.Results ?? [];

		// Filter by schema: must have the required tracking table columns
		var tracking = all.Where(ds =>
		{
			if (ds.Properties is null)
				return false;

			var propNames = JObject.FromObject(ds.Properties).Properties().Select(p => p.Name).ToHashSet();
			return s_trackingSchemaColumns.IsSubsetOf(propNames);
		}).ToList();

		Console.WriteLine($"Found {all.Count} data sources, {tracking.Count} match tracking schema");
		return tracking;
	}

	/// <summary>
	/// Resolves the page_id for a data source by crawling: database → parent block → parent page.
	/// Returns null if the chain cannot be resolved.
	/// </summary>
	internal async Task<string?> ResolvePageIdForDataSourceAsync(NotionSearchDataSourceResult.DataSourceResult dataSource)
	{
		var databaseId = dataSource.Parent?.DatabaseId;
		if (string.IsNullOrWhiteSpace(databaseId))
			return null;

		Console.WriteLine($"Resolving page_id for data source {dataSource.Id} via database {databaseId}");

		// Step 1: GET database → parent.block_id
		var dbResult = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/databases/{databaseId}");
		var dbJson = JObject.Parse(await dbResult.Content.ReadAsStringAsync());
		var dbParentType = dbJson["parent"]?["type"]?.ToString();
		if (dbParentType is not "block_id")
		{
			// Database parent is directly a page (unlikely but possible)
			if (dbParentType is "page_id")
				return dbJson["parent"]?["page_id"]?.ToString();
			return null;
		}

		var blockId = dbJson["parent"]?["block_id"]?.ToString();
		if (string.IsNullOrWhiteSpace(blockId))
			return null;

		// Step 2: GET block → parent.page_id
		var blockResult = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{blockId}");
		var blockJson = JObject.Parse(await blockResult.Content.ReadAsStringAsync());
		var blockParentType = blockJson["parent"]?["type"]?.ToString();
		if (blockParentType is "page_id")
			return blockJson["parent"]?["page_id"]?.ToString();

		// Could be deeper nesting — return null for now
		Console.WriteLine($"Could not resolve page_id: block parent type is '{blockParentType}'");
		return null;
	}

	/// <summary>
	/// Gets the page title from a Notion page.
	/// </summary>
	internal async Task<string?> GetPageTitleAsync(string pageId)
	{
		var page = await GetPageAsync(pageId);
		return page?.PageProperties?.Title?.Titles?.FirstOrDefault()?.Text?.Content?.Trim();
	}

	internal async Task<NotionDataSourceQueryResult?> QueryDataSourceAsync(string dataSourceId, string libraryName)
	{
		Console.WriteLine($"Querying Notion data source {dataSourceId} for library {libraryName}");
		var payload = $@"{{
			""filter"": {{
				""property"": ""Library"",
				""title"": {{
					""equals"": ""{libraryName}""
				}}
			}}
		}}";
		var result = await this.HTTP_CLIENT.PostAsync($"https://api.notion.com/v1/data_sources/{dataSourceId}/query", new StringContent(payload, Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine("Notion queried");
		return JsonConvert.DeserializeObject<NotionDataSourceQueryResult>(content);
	}

	internal async Task<string> UpdatePageAsync(string pageId, ulong userId, string status, string? prCommit, string? version, string? notes)
	{
		Console.WriteLine($"Updating Notion page {pageId} with status {status}, prCommit {prCommit}, version {version}, notes {notes}");
		var payload = JsonConvert.SerializeObject(new LibraryUpdatePayload()
		{
			UpdateProperties = new()
			{
				PullRequestCommit = new()
				{
					Url = prCommit
				},
				Status = new()
				{
					StatusValue = new()
					{
						Id = status
					}
				},
				ReleasedInVersion = new()
				{
					RichTexts = string.IsNullOrWhiteSpace(version) ? [] :
					[
						new()
						{
							TextContent = new()
							{
								Content = version
							}
						}
					]
				},
				Notes = new()
				{
					RichTexts = string.IsNullOrWhiteSpace(notes) ? [] :
					[
						new()
						{
							TextContent = new()
							{
								Content = notes
							}
						}
					]
				},
				ModifiedBy = new()
				{
					RichTexts = [
						new()
						{
							TextContent = new()
							{
								Content = userId.ToString()
							}
						}
					]
				}
			}
		});
		var result = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/pages/{pageId}", new StringContent(payload, Encoding.UTF8, "application/json"));
		var res = await result.Content.ReadAsStringAsync();
		Console.WriteLine("Notion updated");
		return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(res), Formatting.Indented);
	}

	internal async Task<Dictionary<string, List<NotionDataSourceQueryResult.Result>>> GetStatisticInfosAsync(string notion, IEnumerable<string> statuses, (string StatusId, string LanguageId) ids)
	{
		Console.WriteLine($"Getting Notion statistics for notion page ID {notion}");
		var targetDataSource = this.CONFIG.ImplementationTrackingConfig.FirstOrDefault(x => x.PageId.Equals(notion, StringComparison.InvariantCultureIgnoreCase))?.DataSourceId;
		if (string.IsNullOrWhiteSpace(targetDataSource))
			throw new ArgumentException("The provided notion page ID does not have a corresponding data source ID in the configuration.", nameof(notion));

		Dictionary<string, List<NotionDataSourceQueryResult.Result>> results = [];

		foreach (var status in statuses)
		{
			Console.WriteLine($"Getting Notion statistics for status {status}");
			var payload = $@"{{
				""filter"": {{
					""property"": ""Status"",
					""status"": {{
						""equals"": ""{status}""
					}}
				}}
			}}";

			var result = await this.HTTP_CLIENT.PostAsync($"https://api.notion.com/v1/data_sources/{targetDataSource}/query?filter_properties={ids.StatusId}&filter_properties={ids.LanguageId}", new StringContent(payload, Encoding.UTF8, "application/json"));
			var content = await result.Content.ReadAsStringAsync();
			var res = JsonConvert.DeserializeObject<NotionDataSourceQueryResult>(content);
			results[status] = res!.Results;
			Console.WriteLine($"Notion statistics for status {status} retrieved");
		}
		Console.WriteLine("Notion statistics retrieved");
		return results;
	}

	/// <summary>
	/// Creates a new page under the given parent page with a title and icon.
	/// Returns the created page as a JObject.
	/// </summary>
	internal async Task<JObject> CreatePageAsync(string parentPageId, string title, string iconName = "burst", string iconColor = "pink")
	{
		Console.WriteLine($"Creating Notion page '{title}' under parent {parentPageId}");
		var payload = new JObject
		{
			["parent"] = new JObject { ["type"] = "page_id", ["page_id"] = parentPageId },
			["icon"] = new JObject
			{
				["type"] = "icon",
				["icon"] = new JObject { ["name"] = iconName, ["color"] = iconColor }
			},
			["properties"] = new JObject
			{
				["title"] = new JArray
				{
					new JObject
					{
						["text"] = new JObject { ["content"] = title }
					}
				}
			}
		};
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/pages", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Page created: {result.StatusCode}");
		if (!result.IsSuccessStatusCode)
			Console.WriteLine($"Page creation error: {content}");
		return JObject.Parse(content);
	}

	/// <summary>
	/// Appends child blocks to a parent block or page.
	/// Returns the response as a JObject.
	/// </summary>
	internal async Task<JObject> AppendBlockChildrenAsync(string blockId, JArray children)
	{
		Console.WriteLine($"Appending {children.Count} blocks to {blockId}");
		var payload = new JObject { ["children"] = children };
		var result = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/blocks/{blockId}/children", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Blocks appended: {result.StatusCode}");
		return JObject.Parse(content);
	}

	/// <summary>
	/// Creates a new inline database under the given parent page.
	/// Note: Notion API only supports page_id parent for database creation.
	/// Returns the created database as a JObject.
	/// </summary>
	internal async Task<JObject> CreateDatabaseAsync(string parentPageId, string title, JObject properties)
	{
		Console.WriteLine($"Creating database '{title}' under page {parentPageId}");
		var payload = new JObject
		{
			["parent"] = new JObject { ["type"] = "page_id", ["page_id"] = parentPageId },
			["title"] = new JArray
			{
				new JObject
				{
					["type"] = "text",
					["text"] = new JObject { ["content"] = title }
				}
			},
			["is_inline"] = true,
			["properties"] = properties
		};
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/databases", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Database created: {result.StatusCode}");
		if (!result.IsSuccessStatusCode)
			Console.WriteLine($"Database creation error: {content}");
		return JObject.Parse(content);
	}

	/// <summary>
	/// Creates a row in a data source (database).
	/// </summary>
	internal async Task<JObject> CreateDataSourceRowAsync(string dataSourceId, JObject properties)
	{
		Console.WriteLine($"Creating row in data source {dataSourceId}");
		var payload = new JObject
		{
			["parent"] = new JObject { ["type"] = "data_source_id", ["data_source_id"] = dataSourceId },
			["properties"] = properties
		};
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/pages", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Row created: {result.StatusCode}");
		if (!result.IsSuccessStatusCode)
			Console.WriteLine($"Row creation error: {content}");
		return JObject.Parse(content);
	}

	/// <summary>
	/// Builds the Libraries database properties schema (matching the template).
	/// </summary>
	internal static JObject BuildLibrariesDatabaseSchema()
	{
		return new JObject
		{
			["Library"] = new JObject { ["title"] = new JObject() },
			["Status"] = new JObject
			{
				["status"] = new JObject
				{
					["options"] = new JArray
					{
						new JObject { ["name"] = "Not Started", ["color"] = "red" },
						new JObject { ["name"] = "In Progress", ["color"] = "orange" },
						new JObject { ["name"] = "In Review", ["color"] = "blue" },
						new JObject { ["name"] = "Ready For Release", ["color"] = "yellow" },
						new JObject { ["name"] = "Released", ["color"] = "green" }
					},
					["groups"] = new JArray
					{
						new JObject
						{
							["name"] = "To-do",
							["color"] = "gray",
							["option_ids"] = new JArray()
						},
						new JObject
						{
							["name"] = "In progress",
							["color"] = "blue",
							["option_ids"] = new JArray()
						},
						new JObject
						{
							["name"] = "Complete",
							["color"] = "green",
							["option_ids"] = new JArray()
						}
					}
				}
			},
			["Pull Request / Commit"] = new JObject { ["url"] = new JObject() },
			["Language"] = new JObject { ["rich_text"] = new JObject() },
			["Notes"] = new JObject { ["rich_text"] = new JObject() },
			["discord.builders Support"] = new JObject { ["checkbox"] = new JObject() },
			["Released In Version"] = new JObject { ["rich_text"] = new JObject() },
			["Modified By"] = new JObject { ["rich_text"] = new JObject() }
		};
	}

	/// <summary>
	/// Builds the Status Meaning database properties schema.
	/// </summary>
	internal static JObject BuildStatusMeaningDatabaseSchema()
	{
		return new JObject
		{
			["Status"] = new JObject
			{
				["title"] = new JObject()
			},
			["Meaning"] = new JObject
			{
				["rich_text"] = new JObject()
			}
		};
	}

	/// <summary>
	/// Builds the block children array for a new tracking notion page.
	/// Returns (blocks array, explanation heading index, synced_block index) for database creation.
	/// </summary>
	internal static JArray BuildTrackingPageBlocks(string description)
	{
		return new JArray
		{
			// 1. Table of contents
			new JObject
			{
				["object"] = "block",
				["type"] = "table_of_contents",
				["table_of_contents"] = new JObject { ["color"] = "gray" }
			},
			// 2. Description callout (💡)
			new JObject
			{
				["object"] = "block",
				["type"] = "callout",
				["callout"] = new JObject
				{
					["rich_text"] = new JArray
					{
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject { ["content"] = description }
						}
					},
					["icon"] = new JObject
					{
						["type"] = "emoji",
						["emoji"] = "💡"
					},
					["color"] = "gray_background"
				}
			},
			// 3. Info callout (use bot in CAP)
			new JObject
			{
				["object"] = "block",
				["type"] = "callout",
				["callout"] = new JObject
				{
					["rich_text"] = new JArray
					{
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject { ["content"] = "For changes in the progress of your library, please use the " },
							["annotations"] = new JObject { ["bold"] = true, ["color"] = "orange" }
						},
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject
							{
								["content"] = "Notion Tracking",
								["link"] = new JObject { ["url"] = "https://canary.discord.com/channels/1317206872763404478/1378061729807728661/1415419148586188991" }
							},
							["annotations"] = new JObject { ["bold"] = true }
						},
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject { ["content"] = " bot in CAP" },
							["annotations"] = new JObject { ["bold"] = true, ["color"] = "orange" }
						}
					},
					["icon"] = new JObject
					{
						["type"] = "icon",
						["icon"] = new JObject { ["name"] = "info-alternate", ["color"] = "blue" }
					},
					["color"] = "gray_background"
				}
			},
			// 4. Divider
			new JObject { ["object"] = "block", ["type"] = "divider", ["divider"] = new JObject() },
			// 5. Explanation heading (toggleable)
			new JObject
			{
				["object"] = "block",
				["type"] = "heading_2",
				["heading_2"] = new JObject
				{
					["rich_text"] = new JArray
					{
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject { ["content"] = "Explanation" }
						}
					},
					["is_toggleable"] = true,
					["color"] = "default_background"
				}
			},
			// 6. Divider
			new JObject { ["object"] = "block", ["type"] = "divider", ["divider"] = new JObject() },
			// 7. Statistics heading (toggleable)
			new JObject
			{
				["object"] = "block",
				["type"] = "heading_2",
				["heading_2"] = new JObject
				{
					["rich_text"] = new JArray
					{
						new JObject
						{
							["type"] = "text",
							["text"] = new JObject { ["content"] = "Statistics" }
						}
					},
					["is_toggleable"] = true,
					["color"] = "default_background"
				}
			},
			// 8. Divider
			new JObject { ["object"] = "block", ["type"] = "divider", ["divider"] = new JObject() },
			// 9. Synced block (source) — contains heading + database will be added after
			new JObject
			{
				["object"] = "block",
				["type"] = "synced_block",
				["synced_block"] = new JObject
				{
					["synced_from"] = null,
					["children"] = new JArray
					{
						new JObject
						{
							["object"] = "block",
							["type"] = "heading_2",
							["heading_2"] = new JObject
							{
								["rich_text"] = new JArray
								{
									new JObject
									{
										["type"] = "text",
										["text"] = new JObject { ["content"] = "Implementation Status" }
									}
								},
								["is_toggleable"] = false,
								["color"] = "default"
							}
						}
					}
				}
			},
			// 10. Empty paragraph
			new JObject
			{
				["object"] = "block",
				["type"] = "paragraph",
				["paragraph"] = new JObject
				{
					["rich_text"] = new JArray(),
					["color"] = "default"
				}
			}
		};
	}
}
