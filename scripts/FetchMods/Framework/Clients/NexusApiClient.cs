using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Pathoschild.FluentNexus;
using Pathoschild.FluentNexus.Models;
using Pathoschild.Http.Client;
using StardewModdingAPI.Toolkit.Framework.Clients.NexusExport;
using StardewModdingAPI.Toolkit.Framework.Clients.NexusExport.ResponseModels;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework.Clients;

/// <summary>A client which fetches mods from the Nexus Mods API.</summary>
class NexusApiClient : IModSiteClient
{
	/*********
	** Fields
	*********/
	/// <summary>The Nexus Mods game key for Stardew Valley.</summary>
	private const string GameKey = "stardewvalley";

	/// <summary>The API client for the main Nexus API.</summary>
	private readonly NexusClient MainApi;

	/// <summary>The Nexus export API client.</summary>
	private readonly NexusExportApiClient ExportApi;

	/// <summary>The mod page fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludePageFields =
	[
		// promoted into generic fields
		nameof(NexusModExport.Name),
		nameof(NexusModExport.Author),
		nameof(NexusModExport.Uploader),
		nameof(NexusModExport.Version),
		nameof(NexusModExport.Description),
		nameof(NexusModExport.Files),

		// redundant (since hidden mods are filtered out)
		nameof(NexusModExport.AllowView),
		nameof(NexusModExport.Moderated),
		nameof(NexusModExport.Published)
	];

	/// <summary>The download fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludeDownloadFields =
	[
		// promoted into generic fields
		nameof(NexusFileExport.Name),
		nameof(NexusFileExport.FileName),
		nameof(NexusFileExport.Version),
		nameof(NexusFileExport.Description),
		nameof(NexusFileExport.UploadedAt),
		nameof(NexusFileExport.SizeInBytes)
	];


	/*********
	** Accessors
	*********/
	/// <inheritdoc />
	public ModSite SiteKey { get; } = ModSite.Nexus;


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="exportApiUrl">The base URL for the Nexus export API.</param>
	/// <param name="userAgent">The user agent sent to the mod site API.</param>
	/// <param name="apiKey">The Nexus API key with which to authenticate.</param>
	/// <param name="appName">An arbitrary name for the app/script using the client, reported to the Nexus Mods API and used in the user agent.</param>
	/// <param name="appVersion">An arbitrary version number for the <paramref name="appName" /> (ideally a semantic version).</param>
	public NexusApiClient(string exportApiUrl, string userAgent, string apiKey, string appName, string appVersion)
	{
		this.MainApi = new NexusClient(apiKey, appName, appVersion);
		this.ExportApi = new NexusExportApiClient(userAgent, exportApiUrl);
	}

	/// <inheritdoc />
	public Task AuthenticateAsync()
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<ExportResult> GetAllModsAsync()
	{
		NexusFullExport export = await this.ExportApi.FetchExportAsync();

		ModPageRecord[] mods = export.Data
			.Where(p => p.Value is { Published: true, AllowView: true, Moderated: false })
			.Select(p => this.Parse(p.Key, p.Value))
			.ToArray();

		return new ExportResult(export.CacheHeaders.LastModified, mods);
	}

	/// <inheritdoc />
	public async Task<Uri[]> GetDownloadUrlsAsync(ModPageRecord mod, ModPageDownloadRecord file)
	{
		try
		{
			ModFileDownloadLink[] downloadLinks = await this.MainApi.ModFiles.GetDownloadLinks(GameKey, (int)mod.Id, (int)file.Id);
			return downloadLinks.Select(p => p.Uri).ToArray();
		}
		catch (ApiException ex) when (ex.Status == (HttpStatusCode)429)
		{
			throw await this.GetRateLimitExceptionAsync();
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		this.MainApi.HttpClient.Dispose();
		this.ExportApi.Dispose();
	}


	/*********
	** Private methods
	*********/
	/// <summary>Parse raw mod data from the Nexus export API.</summary>
	/// <param name="modId">The mod ID.</param>
	/// <param name="mod">The raw mod data.</param>
	private ModPageRecord Parse(long modId, NexusModExport mod)
	{
		// get author
		string? author = mod.Uploader ?? mod.Author;
		string? authorLabel = mod.Author != null && !mod.Author.Equals(author, StringComparison.InvariantCultureIgnoreCase)
			? mod.Author
			: null;

		// get files
		DateTimeOffset lastUpdated = DateTimeOffset.MinValue;
		List<ModPageDownloadRecord> files = [];
		foreach ((long fileId, NexusFileExport rawFile) in mod.Files)
		{
			if (this.TryParseFileType(rawFile.CategoryId) is not { } fileType)
				continue;

			ModPageDownloadRecord file = this.ParseFile(fileId, fileType, rawFile);

			files.Add(file);

			if (file.Uploaded > lastUpdated)
				lastUpdated = file.Uploaded;
		}

		// special case: if a mod has zero main/optional files, get files from any non-archived/deleted/old category
		if (files.Count == 0)
		{
			foreach ((long fileId, NexusFileExport rawFile) in mod.Files)
			{
				if (rawFile.CategoryId is (int)FileCategory.Archived or (int)FileCategory.Deleted or (int)FileCategory.Old)
					continue;

				ModPageDownloadRecord file = this.ParseFile(fileId, ModDownloadType.Optional, rawFile);

				files.Add(file);

				if (file.Uploaded > lastUpdated)
					lastUpdated = file.Uploaded;
			}
		}

		// get model
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(mod, this.ExcludePageFields);
		return new ModPageRecord(
			site: ModSite.Nexus,
			id: modId,
			name: mod.Name,
			tagLine: null,
			description: mod.Description,
			author: author,
			authorLabel: authorLabel,
			pageUrl: $"https://www.nexusmods.com/stardewvalley/mods/{modId}",
			version: mod.Version,
			updated: lastUpdated,
			downloads: files.ToArray(),
			otherFields: otherFields
		);
	}

	/// <summary>Parse raw file data from the Nexus export API.</summary>
	/// <param name="fileId">The unique Nexus file ID.</param>
	/// <param name="fileType">The file type.</param>
	/// <param name="file">The raw file data.</param>
	private ModPageDownloadRecord ParseFile(long fileId, ModDownloadType fileType, NexusFileExport file)
	{
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(file, this.ExcludeDownloadFields);
		return new ModPageDownloadRecord(
			id: fileId,
			type: fileType,
			displayName: file.Name,
			fileName: file.FileName,
			version: file.Version,
			description: file.Description,
			uploaded: DateTimeOffset.FromUnixTimeSeconds(file.UploadedAt),
			sizeInBytes: file.SizeInBytes ?? 0,
			otherFields: otherFields
		);
	}

	/// <summary>Get a mod download type from its category ID, if known.</summary>
	/// <param name="categoryId">The raw file category ID from the Nexus API.</param>
	/// <returns>Returns the download type if it matches one of the expected types, else <c>null</c>.</returns>
	private ModDownloadType? TryParseFileType(uint categoryId)
	{
		return categoryId switch
		{
			(int)FileCategory.Main => ModDownloadType.Main,
			(int)FileCategory.Optional => ModDownloadType.Optional,
			_ => null
		};
	}

	/// <summary>Get an exception indicating that rate limits have been exceeded.</summary>
	private async Task<RateLimitedException> GetRateLimitExceptionAsync()
	{
		IRateLimitManager rateLimits = await this.MainApi.GetRateLimits();
		TimeSpan unblockTime = rateLimits.GetTimeUntilRenewal();
		string rateLimitSummary = $"{rateLimits.DailyRemaining}/{rateLimits.DailyLimit} daily resetting in {this.GetFormattedTime(rateLimits.DailyReset - DateTimeOffset.UtcNow)}, {rateLimits.HourlyRemaining}/{rateLimits.HourlyLimit} hourly resetting in {this.GetFormattedTime(rateLimits.HourlyReset - DateTimeOffset.UtcNow)}";

		return new RateLimitedException(unblockTime, rateLimitSummary);
	}

	/// <summary>Get a human-readable formatted time span.</summary>
	/// <param name="time">The time span to format.</param>
	private string GetFormattedTime(TimeSpan time)
	{
		if (time.TotalSeconds < 1)
			return $"{Math.Round(time.TotalMilliseconds, 0)} ms";

		StringBuilder formatted = new();
		void Format(int amount, string label)
		{
			if (amount > 0)
			{
				formatted.Append(' ');
				formatted.Append(amount);
				formatted.Append(' ');
				formatted.Append(label);
				if (amount != 1)
					formatted.Append('s');
			}
		}
		Format((int)time.TotalHours, "hour");
		Format(time.Minutes, "minute");
		Format(time.Seconds, "second");

		return formatted.ToString().TrimStart();
	}
}
