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
	private HttpClient? V3_HTTP_CLIENT { get; init; }

	/// <summary>
	/// Whether the internal v3 API is available (user token configured).
	/// </summary>
	internal bool HasV3Client => this.V3_HTTP_CLIENT is not null;

	public NotionRestClient(NotionConfig config, WebProxy? proxy = null)
	{
		this.CONFIG = config;
		var handler = new HttpClientHandler() { Proxy = proxy };
		this.HTTP_CLIENT = new HttpClient(handler);
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("Authorization", "Bearer " + this.CONFIG.NotionToken);
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("Notion-Version", this.CONFIG.NotionApiVersion);
		this.HTTP_CLIENT.DefaultRequestHeaders.Add("User-Agent", $"AITSYS-Discord-LibraryDevelopmentTracking (https://github.com/Aiko-IT-Systems/AITSYS.Discord.LibraryDevelopmentTracking, v{Assembly.GetExecutingAssembly().GetName().Version})");

		if (!string.IsNullOrWhiteSpace(config.NotionUserToken) && !string.IsNullOrWhiteSpace(config.NotionSpaceId))
		{
			this.V3_HTTP_CLIENT = new HttpClient(new HttpClientHandler() { Proxy = proxy });
			this.V3_HTTP_CLIENT.DefaultRequestHeaders.Add("Cookie", $"token_v2={config.NotionUserToken}");
			if (!string.IsNullOrWhiteSpace(config.NotionUserId))
				this.V3_HTTP_CLIENT.DefaultRequestHeaders.Add("x-notion-active-user-header", config.NotionUserId);
			this.V3_HTTP_CLIENT.DefaultRequestHeaders.Add("x-notion-space-id", config.NotionSpaceId);
			this.V3_HTTP_CLIENT.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");
			Console.WriteLine("V3 internal API client initialized");
		}
		else
			Console.WriteLine("V3 internal API client not configured (notion_user_token or notion_space_id missing)");
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
	/// Note: In API version 2025-09-03, schema properties are ignored during creation.
	/// Use <see cref="PatchDataSourceAsync"/> to set up the schema on the returned data source.
	/// The response includes a <c>data_sources[0].id</c> field.
	/// </summary>
	internal async Task<JObject> CreateDatabaseAsync(string parentPageId, string title)
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
			["is_inline"] = true
		};
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/databases", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Database created: {result.StatusCode}");
		if (!result.IsSuccessStatusCode)
			Console.WriteLine($"Database creation error: {content}");
		return JObject.Parse(content);
	}

	/// <summary>
	/// Patches a data source to set up or modify its property schema.
	/// In API version 2025-09-03, the data source owns the schema, not the database.
	/// To rename the default title property, use <c>{"Name": {"name": "NewTitle"}}</c>.
	/// </summary>
	internal async Task<JObject> PatchDataSourceAsync(string dataSourceId, JObject properties)
	{
		Console.WriteLine($"Patching data source {dataSourceId} with {properties.Count} properties");
		var payload = new JObject { ["properties"] = properties };
		var result = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/data_sources/{dataSourceId}", new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		Console.WriteLine($"Data source patched: {result.StatusCode}");
		if (!result.IsSuccessStatusCode)
			Console.WriteLine($"Data source patch error: {content}");
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
	/// Builds the Libraries data source PATCH schema.
	/// Renames the default <c>Name</c> title to <c>Library</c> and adds all tracking columns.
	/// </summary>
	internal static JObject BuildLibrariesDataSourcePatchSchema()
	{
		return new JObject
		{
			["Name"] = new JObject { ["name"] = "Library" },
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
	/// Builds the Status Meaning data source PATCH schema.
	/// Renames the default <c>Name</c> title to <c>Status</c> and adds <c>Description</c>.
	/// </summary>
	internal static JObject BuildStatusMeaningDataSourcePatchSchema()
	{
		return new JObject
		{
			["Name"] = new JObject { ["name"] = "Status" },
			["Description"] = new JObject { ["rich_text"] = new JObject() }
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

	#region V3 Internal API (Template Duplication)

	/// <summary>
	/// Duplicates a page using the internal v3 API.
	/// Creates a copy_indicator block under the target parent, then enqueues a duplicateBlock task.
	/// Cleans up the copy_indicator on failure. Returns the new page ID on success, or null on failure.
	/// </summary>
	internal async Task<string?> DuplicatePageAsync(string sourcePageId, string targetParentPageId, int timeoutSeconds = 60)
	{
		if (this.V3_HTTP_CLIENT is null)
			throw new InvalidOperationException("V3 API client not configured. Set notion_user_token and notion_space_id in config.");

		var spaceId = this.CONFIG.NotionSpaceId!;
		var newBlockId = Guid.NewGuid().ToString();

		Console.WriteLine($"Creating copy_indicator {newBlockId} under {targetParentPageId}");

		// Step 1: Create copy_indicator block via submitTransaction
		var txPayload = new JObject
		{
			["requestId"] = Guid.NewGuid().ToString(),
			["transactions"] = new JArray
			{
				new JObject
				{
					["id"] = Guid.NewGuid().ToString(),
					["spaceId"] = spaceId,
					["operations"] = new JArray
					{
						new JObject
						{
							["pointer"] = new JObject { ["table"] = "block", ["id"] = newBlockId, ["spaceId"] = spaceId },
							["path"] = new JArray(),
							["command"] = "set",
							["args"] = new JObject
							{
								["type"] = "copy_indicator",
								["id"] = newBlockId,
								["parent_id"] = targetParentPageId,
								["parent_table"] = "block",
								["alive"] = true,
								["space_id"] = spaceId
							}
						},
						new JObject
						{
							["pointer"] = new JObject { ["table"] = "block", ["id"] = targetParentPageId, ["spaceId"] = spaceId },
							["path"] = new JArray { "content" },
							["command"] = "listAfter",
							["args"] = new JObject { ["id"] = newBlockId }
						}
					}
				}
			}
		};

		var txResult = await this.V3_HTTP_CLIENT.PostAsync("https://www.notion.so/api/v3/submitTransaction",
			new StringContent(txPayload.ToString(), Encoding.UTF8, "application/json"));
		if (!txResult.IsSuccessStatusCode)
		{
			Console.WriteLine($"Failed to create copy_indicator: {txResult.StatusCode} {await txResult.Content.ReadAsStringAsync()}");
			return null;
		}

		// Step 2: Enqueue duplicateBlock task
		Console.WriteLine($"Enqueuing duplicateBlock: {sourcePageId} → {newBlockId}");
		var taskPayload = new JObject
		{
			["task"] = new JObject
			{
				["eventName"] = "duplicateBlock",
				["request"] = new JObject
				{
					["sourceBlocks"] = new JArray { new JObject { ["id"] = sourcePageId, ["spaceId"] = spaceId } },
					["targetBlocks"] = new JArray { new JObject { ["id"] = newBlockId, ["spaceId"] = spaceId } },
					["addCopyName"] = false,
					["deepCopyTransclusionContainers"] = true,
					["convertExternalObjectInstancesToPlaceholders"] = false,
					["isTemplateInstantiation"] = false,
					["allowRedundancy"] = false
				},
				["cellRouting"] = new JObject { ["spaceIds"] = new JArray { spaceId } }
			}
		};

		var taskResult = await this.V3_HTTP_CLIENT.PostAsync("https://www.notion.so/api/v3/enqueueTask",
			new StringContent(taskPayload.ToString(), Encoding.UTF8, "application/json"));
		var taskContent = await taskResult.Content.ReadAsStringAsync();
		if (!taskResult.IsSuccessStatusCode)
		{
			Console.WriteLine($"Failed to enqueue task: {taskResult.StatusCode} {taskContent}");
			await CleanupBlockAsync(newBlockId, targetParentPageId);
			return null;
		}

		var taskId = JObject.Parse(taskContent)["taskId"]?.ToString();
		if (string.IsNullOrWhiteSpace(taskId))
		{
			Console.WriteLine("No taskId returned");
			await CleanupBlockAsync(newBlockId, targetParentPageId);
			return null;
		}

		Console.WriteLine($"Task enqueued: {taskId}");

		// Step 3: Poll getTasks until success
		for (var i = 0; i < timeoutSeconds; i++)
		{
			await Task.Delay(1000);
			var pollPayload = new JObject { ["taskIds"] = new JArray { taskId } };
			var pollResult = await this.V3_HTTP_CLIENT.PostAsync("https://www.notion.so/api/v3/getTasks",
				new StringContent(pollPayload.ToString(), Encoding.UTF8, "application/json"));

			if (!pollResult.IsSuccessStatusCode)
			{
				Console.WriteLine($"  [{i + 1}s] poll failed: {pollResult.StatusCode}");
				continue; // Retry on transient poll failure
			}

			var pollContent = await pollResult.Content.ReadAsStringAsync();
			var pollData = JObject.Parse(pollContent);
			var result = pollData["results"]?[0];
			var state = result?["state"]?.ToString();
			var complete = result?["status"]?["completeBlocks"]?.ToString() ?? "?";
			var total = result?["status"]?["totalBlocks"]?.ToString() ?? "?";
			Console.WriteLine($"  [{i + 1}s] state={state} blocks={complete}/{total}");

			if (state is "success")
			{
				Console.WriteLine($"Duplication complete! New page ID: {newBlockId}");
				return newBlockId;
			}

			if (state is "failure")
			{
				var error = result?["error"]?.ToString();
				Console.WriteLine($"Duplication failed: {error}");
				await CleanupBlockAsync(newBlockId, targetParentPageId);
				return null;
			}
		}

		Console.WriteLine("Duplication timed out");
		await CleanupBlockAsync(newBlockId, targetParentPageId);
		return null;
	}

	/// <summary>
	/// Cleans up a failed copy_indicator/duplicated block by setting alive=false.
	/// </summary>
	private async Task CleanupBlockAsync(string blockId, string parentId)
	{
		if (this.V3_HTTP_CLIENT is null) return;

		Console.WriteLine($"Cleaning up block {blockId}");
		try
		{
			var spaceId = this.CONFIG.NotionSpaceId!;
			var cleanupPayload = new JObject
			{
				["requestId"] = Guid.NewGuid().ToString(),
				["transactions"] = new JArray
				{
					new JObject
					{
						["id"] = Guid.NewGuid().ToString(),
						["spaceId"] = spaceId,
						["operations"] = new JArray
						{
							new JObject
							{
								["pointer"] = new JObject { ["table"] = "block", ["id"] = blockId, ["spaceId"] = spaceId },
								["path"] = new JArray(),
								["command"] = "update",
								["args"] = new JObject { ["alive"] = false }
							},
							new JObject
							{
								["pointer"] = new JObject { ["table"] = "block", ["id"] = parentId, ["spaceId"] = spaceId },
								["path"] = new JArray { "content" },
								["command"] = "listRemove",
								["args"] = new JObject { ["id"] = blockId }
							}
						}
					}
				}
			};
			await this.V3_HTTP_CLIENT.PostAsync("https://www.notion.so/api/v3/submitTransaction",
				new StringContent(cleanupPayload.ToString(), Encoding.UTF8, "application/json"));
			Console.WriteLine("Cleanup done");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Cleanup failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Waits for a duplicated page to become visible via the public API, with retries.
	/// </summary>
	internal async Task<bool> WaitForPageVisibilityAsync(string pageId, int maxRetries = 10)
	{
		for (var i = 0; i < maxRetries; i++)
		{
			try
			{
				var result = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/pages/{pageId}");
				if (result.IsSuccessStatusCode)
				{
					Console.WriteLine($"Page {pageId} visible after {i + 1} attempts");
					return true;
				}
			}
			catch { /* Retry */ }

			await Task.Delay(1000 * (i + 1)); // Linear backoff
		}

		Console.WriteLine($"Page {pageId} not visible after {maxRetries} attempts");
		return false;
	}

	/// <summary>
	/// Renames a page via the public API with retry on failure.
	/// </summary>
	internal async Task<(bool Success, JObject Result)> RenamePageAsync(string pageId, string newTitle)
	{
		Console.WriteLine($"Renaming page {pageId} to '{newTitle}'");
		var payload = new JObject
		{
			["properties"] = new JObject
			{
				["title"] = new JArray
				{
					new JObject { ["text"] = new JObject { ["content"] = newTitle } }
				}
			}
		};

		for (var attempt = 0; attempt < 3; attempt++)
		{
			var result = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/pages/{pageId}",
				new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));
			var content = await result.Content.ReadAsStringAsync();

			if (result.IsSuccessStatusCode)
			{
				Console.WriteLine($"Page renamed successfully");
				return (true, JObject.Parse(content));
			}

			Console.WriteLine($"Rename attempt {attempt + 1} failed: {result.StatusCode} {content}");
			await Task.Delay(2000 * (attempt + 1));
		}

		return (false, new JObject());
	}

	/// <summary>
	/// Updates the description callout (💡) on a tracking page with retry.
	/// Returns true if the callout was found and updated.
	/// </summary>
	internal async Task<bool> UpdateDescriptionCalloutAsync(string pageId, string newDescription)
	{
		Console.WriteLine($"Updating description callout on page {pageId}");

		for (var attempt = 0; attempt < 3; attempt++)
		{
			var children = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{pageId}/children");
			if (!children.IsSuccessStatusCode)
			{
				Console.WriteLine($"Failed to get children (attempt {attempt + 1}): {children.StatusCode}");
				await Task.Delay(2000 * (attempt + 1));
				continue;
			}

			var childrenJson = JObject.Parse(await children.Content.ReadAsStringAsync());
			var blocks = childrenJson["results"] as JArray ?? [];

			foreach (var block in blocks)
			{
				if (block["type"]?.ToString() is not "callout")
					continue;

				var icon = block["callout"]?["icon"];
				if (icon?["type"]?.ToString() is "emoji" && icon?["emoji"]?.ToString() is "💡")
				{
					var blockId = block["id"]?.ToString();
					if (string.IsNullOrWhiteSpace(blockId))
						break;

					var updatePayload = new JObject
					{
						["callout"] = new JObject
						{
							["rich_text"] = new JArray
							{
								new JObject { ["type"] = "text", ["text"] = new JObject { ["content"] = newDescription } }
							}
						}
					};
					var updateResult = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/blocks/{blockId}",
						new StringContent(updatePayload.ToString(), Encoding.UTF8, "application/json"));

					if (updateResult.IsSuccessStatusCode)
					{
						Console.WriteLine($"Description callout updated successfully");
						return true;
					}

					Console.WriteLine($"Description callout update failed: {updateResult.StatusCode}");
					return false;
				}
			}

			Console.WriteLine($"Description callout not found (attempt {attempt + 1})");
			await Task.Delay(2000 * (attempt + 1));
		}

		Console.WriteLine("Description callout not found after all attempts");
		return false;
	}

	/// <summary>
	/// Finds the Libraries data source ID by searching all data sources
	/// and matching by schema + resolved page ownership.
	/// Retries with delay to handle search index lag after duplication.
	/// </summary>
	internal async Task<(string? DatabaseId, string? DataSourceId)> FindLibrariesDataSourceAsync(string pageId)
	{
		Console.WriteLine($"Finding Libraries data source for page {pageId}");
		var normalizedPageId = pageId.Replace("-", "");

		for (var attempt = 0; attempt < 5; attempt++)
		{
			if (attempt > 0)
			{
				Console.WriteLine($"  Retry {attempt}/4 — waiting for search index...");
				await Task.Delay(3000 * attempt);
			}

			var allDataSources = await SearchAllDataSourcesAsync();
			foreach (var ds in allDataSources)
			{
				var dbId = ds.Parent?.DatabaseId;
				if (string.IsNullOrWhiteSpace(dbId))
					continue;

				var resolvedPageId = await ResolvePageIdForDataSourceAsync(ds);
				if (resolvedPageId?.Replace("-", "").Equals(normalizedPageId, StringComparison.OrdinalIgnoreCase) is true)
				{
					Console.WriteLine($"Found Libraries data source: DS={ds.Id}, DB={dbId}, Page={resolvedPageId}");
					return (dbId, ds.Id);
				}
			}
		}

		Console.WriteLine("Libraries data source not found after retries");
		return (null, null);
	}

	/// <summary>
	/// Moves a block from one parent to another using the v3 internal API.
	/// Optionally places it after a specific sibling block.
	/// </summary>
	internal async Task<bool> MoveBlockAsync(string blockId, string fromParentId, string toParentId, string? afterBlockId = null)
	{
		if (this.V3_HTTP_CLIENT is null)
			throw new InvalidOperationException("V3 API client not configured.");

		var spaceId = this.CONFIG.NotionSpaceId!;
		Console.WriteLine($"Moving block {blockId} from {fromParentId} to {toParentId}");

		var operations = new JArray
		{
			// Remove from old parent
			new JObject
			{
				["pointer"] = new JObject { ["table"] = "block", ["id"] = fromParentId, ["spaceId"] = spaceId },
				["path"] = new JArray { "content" },
				["command"] = "listRemove",
				["args"] = new JObject { ["id"] = blockId }
			},
			// Update block's parent
			new JObject
			{
				["pointer"] = new JObject { ["table"] = "block", ["id"] = blockId, ["spaceId"] = spaceId },
				["path"] = new JArray(),
				["command"] = "update",
				["args"] = new JObject { ["parent_id"] = toParentId, ["parent_table"] = "block" }
			}
		};

		// Add to new parent (after specific sibling or at end)
		var listAddArgs = new JObject { ["id"] = blockId };
		if (!string.IsNullOrWhiteSpace(afterBlockId))
			listAddArgs["after"] = afterBlockId;

		operations.Add(new JObject
		{
			["pointer"] = new JObject { ["table"] = "block", ["id"] = toParentId, ["spaceId"] = spaceId },
			["path"] = new JArray { "content" },
			["command"] = "listAfter",
			["args"] = listAddArgs
		});

		var txPayload = new JObject
		{
			["requestId"] = Guid.NewGuid().ToString(),
			["transactions"] = new JArray
			{
				new JObject
				{
					["id"] = Guid.NewGuid().ToString(),
					["spaceId"] = spaceId,
					["operations"] = operations
				}
			}
		};

		var result = await this.V3_HTTP_CLIENT.PostAsync("https://www.notion.so/api/v3/submitTransaction",
			new StringContent(txPayload.ToString(), Encoding.UTF8, "application/json"));

		if (result.IsSuccessStatusCode)
		{
			Console.WriteLine("Block moved successfully");
			return true;
		}

		Console.WriteLine($"Failed to move block: {result.StatusCode} {await result.Content.ReadAsStringAsync()}");
		return false;
	}

	/// <summary>
	/// Finds the last child block ID of a parent (for placing new blocks at the end).
	/// </summary>
	internal async Task<string?> FindLastChildBlockAsync(string parentBlockId)
	{
		var children = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{parentBlockId}/children?page_size=100");
		if (!children.IsSuccessStatusCode) return null;

		var childrenJson = JObject.Parse(await children.Content.ReadAsStringAsync());
		var blocks = childrenJson["results"] as JArray ?? [];
		return blocks.LastOrDefault()?["id"]?.ToString();
	}

	/// <summary>
	/// Finds the "Implementation Tracking" toggle heading block in the DLD page.
	/// This is where phase pages should be placed.
	/// </summary>
	internal async Task<string?> FindPhasesToggleBlockAsync(string pageId)
	{
		Console.WriteLine($"Looking for Implementation Tracking toggle block in {pageId}");
		var children = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{pageId}/children?page_size=100");
		if (!children.IsSuccessStatusCode) return null;

		var childrenJson = JObject.Parse(await children.Content.ReadAsStringAsync());
		var blocks = childrenJson["results"] as JArray ?? [];

		foreach (var block in blocks)
		{
			var type = block["type"]?.ToString();
			if (type is "heading_1" or "heading_2" or "heading_3")
			{
				var heading = block[type];
				var isToggleable = heading?["is_toggleable"]?.Value<bool>() is true;
				var richText = heading?["rich_text"] as JArray;
				var text = richText?.FirstOrDefault()?["plain_text"]?.ToString();

				if (isToggleable && text?.Contains("Implementation Tracking", StringComparison.OrdinalIgnoreCase) is true)
				{
					var id = block["id"]?.ToString();
					Console.WriteLine($"Found Implementation Tracking toggle: '{text}' id={id}");
					return id;
				}
			}
		}

		Console.WriteLine("Implementation Tracking toggle block not found");
		return null;
	}

	#endregion
}
