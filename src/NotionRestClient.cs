// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Net;
using System.Reflection;
using System.Text;

using AITSYS.Discord.LibraryDevelopmentTracking.Entities;
using AITSYS.Discord.LibraryDevelopmentTracking.Entities.Notion;

using Newtonsoft.Json;

namespace AITSYS.Discord.LibraryDevelopmentTracking;

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
		var result = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/blocks/{notion}/children");
		var content = await result.Content.ReadAsStringAsync();
		return JsonConvert.DeserializeObject<NotionBlockResult>(content);
	}

	internal async Task<NotionPageResult?> GetPageAsync(string notion)
	{
		var result = await this.HTTP_CLIENT.GetAsync($"https://api.notion.com/v1/pages/{notion}");
		var content = await result.Content.ReadAsStringAsync();
		return JsonConvert.DeserializeObject<NotionPageResult>(content);
	}

	internal async Task<NotionSearchDataSourceResult.DataSourceResult?> GetDataSourceBySearchAsync(string notion)
	{
		var targetDataSource = this.CONFIG.ImplementationTrackingConfig.FirstOrDefault(x => x.PageId.Equals(notion, StringComparison.InvariantCultureIgnoreCase))?.DataSourceId;
		if (string.IsNullOrWhiteSpace(targetDataSource))
			throw new ArgumentException("The provided notion page ID does not have a corresponding data source ID in the configuration.", nameof(notion));
		var payload = $@"{{
			""query"": ""Libraries"",
			""filter"": {{
				""value"": ""data_source"",
				""property"": ""object""
			}}
		}}";
		var result = await this.HTTP_CLIENT.PostAsync("https://api.notion.com/v1/search", new StringContent(payload, Encoding.UTF8, "application/json"));
		var content = await result.Content.ReadAsStringAsync();
		var res = JsonConvert.DeserializeObject<NotionSearchDataSourceResult>(content);
		return res?.Results?.FirstOrDefault(x => x.Id.Equals(targetDataSource, StringComparison.InvariantCultureIgnoreCase));
	}

	internal async Task<NotionDataSourceQueryResult?> QueryDataSourceAsync(string dataSourceId, string libraryName)
	{
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
		return JsonConvert.DeserializeObject<NotionDataSourceQueryResult>(content);
	}

	internal async Task<string> UpdatePageAsync(string pageId, string status, string? prCommit, string? version, string? notes)
	{
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
				}
			}
		});
		var result = await this.HTTP_CLIENT.PatchAsync($"https://api.notion.com/v1/pages/{pageId}", new StringContent(payload, Encoding.UTF8, "application/json"));
		var res = await result.Content.ReadAsStringAsync();
		return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(res), Formatting.Indented);
	}

	internal async Task<Dictionary<string, List<NotionDataSourceQueryResult.Result>>> GetStatisticInfosAsync(string notion, IEnumerable<string> statuses, (string StatusId, string LanguageId) ids)
	{
		var targetDataSource = this.CONFIG.ImplementationTrackingConfig.FirstOrDefault(x => x.PageId.Equals(notion, StringComparison.InvariantCultureIgnoreCase))?.DataSourceId;
		if (string.IsNullOrWhiteSpace(targetDataSource))
			throw new ArgumentException("The provided notion page ID does not have a corresponding data source ID in the configuration.", nameof(notion));

		Dictionary<string, List<NotionDataSourceQueryResult.Result>> results = [];

		foreach (var status in statuses)
		{
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
		}
		return results;
	}
}
