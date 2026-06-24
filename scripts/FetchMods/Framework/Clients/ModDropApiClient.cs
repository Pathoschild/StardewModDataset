using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pathoschild.Http.Client;
using Pathoschild.ModData.Common;
using StardewModdingAPI.Toolkit.Framework.Clients.ModDropExport;
using StardewModdingAPI.Toolkit.Framework.Clients.ModDropExport.ResponseModels;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework.Clients;

/// <summary>A client which fetches mods from the ModDrop export API.</summary>
class ModDropApiClient : IModSiteClient
{
	/*********
	** Fields
	*********/
	/// <summary>The username with which to log in to the main API.</summary>
	private readonly string Username;

	/// <summary>The password with which to log in to the main API.</summary>
	private readonly string Password;

	/// <summary>The ModDrop API client.</summary>
	private readonly FluentClient MainApi = new("https://www.moddrop.com/api");

	/// <summary>The ModDrop export API client.</summary>
	private readonly ModDropExportApiClient ExportApi;

	/// <summary>The mod page fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludePageFields =
	[
		// promoted into generic fields
		nameof(ModDropModExport.Id),
		nameof(ModDropModExport.Title),
		nameof(ModDropModExport.TagLine),
		nameof(ModDropModExport.Description),
		nameof(ModDropModExport.UserName),
		nameof(ModDropModExport.AuthorName),
		nameof(ModDropModExport.PageUrl),
		nameof(ModDropModExport.Version),
		nameof(ModDropModExport.Files),

		// redundant info
		"gameID",
		"gameTitle",

		// transient fields which change without a mod update
		"downloads",
		"modpacksCount",
		"publishedModpacksCount",
		"ratingAverage",
		"ratings",
		"ratingsDown",
		"ratingsUp",
		"subscribers"
	];

	/// <summary>The download fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludeDownloadFields =
	[
		// promoted into generic fields
		nameof(ModDropFileExport.Id),
		nameof(ModDropFileExport.Name),
		nameof(ModDropFileExport.FileName),
		nameof(ModDropFileExport.Version),
		nameof(ModDropFileExport.DateCreated),
		nameof(ModDropFileExport.Description),
		nameof(ModDropFileExport.Size),

		// transient fields which change without a mod update
		"downloads",
		"modID"
	];


	/*********
	** Accessors
	*********/
	/// <inheritdoc />
	public ModSite SiteKey { get; } = ModSite.ModDrop;


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="exportApiUrl">The base URL for the ModDrop export API.</param>
	/// <param name="userAgent">The user agent sent to the mod site API.</param>
	/// <param name="username">The username with which to log in, if any.</param>
	/// <param name="password">The password with which to log in, if any.</param>
	public ModDropApiClient(string exportApiUrl, string userAgent, string username, string password)
	{
		this.ExportApi = new ModDropExportApiClient(userAgent, exportApiUrl);
		this.Username = username;
		this.Password = password;
	}

	/// <summary>Authenticate with the mod site if needed.</summary>
	public async Task AuthenticateAsync()
	{
		var response = await this.MainApi
			.PostAsync("v1/auth/login")
			.WithBasicAuthentication(this.Username, this.Password)
			.AsRawJsonObject();

		string? apiToken = response["apiToken"]?.Value<string>();
		if (string.IsNullOrEmpty(apiToken))
			throw new InvalidOperationException($"Authentication with the ModDrop API failed:\n{response}");

		this.MainApi.AddDefault(p => p.WithHeader("Authorization", apiToken));
	}

	/// <inheritdoc />
	public async Task<ExportResult> GetAllModsAsync()
	{
		ModDropFullExport export = await this.ExportApi.FetchExportAsync();

		ModPageRecord[] mods = export.Mods.Values
			.Where(p => p is { IsDeleted: false, IsPublished: true })
			.Select(this.Parse)
			.ToArray();

		return new ExportResult(export.CacheHeaders.LastModified, mods);
	}

	/// <summary>Get the download URLs for a file. If this returns multiple URLs, they're assumed to be mirrors and the first working URL will be used.</summary>
	/// <param name="mod">The mod for which to get download URLs.</param>
	/// <param name="file">The file for which to get download URLs.</param>
	/// <exception cref="RateLimitedException">The API client has exceeded the API's rate limits.</exception>
	public async Task<Uri[]> GetDownloadUrlsAsync(ModPageRecord mod, ModPageDownloadRecord file)
	{
		// get from mod data
		Exception? exception = null;
		try
		{
			var response = await this.MainApi
				.PostAsync($"v1/mod-{mod.Id}/file-{file.Id}/download")
				.AsRawJsonObject();

			string? url = response["url"]?.Value<string>();
			if (url != null)
				return [new Uri(url)];
		}
		catch (Exception ex)
		{
			exception = ex;
		}

		// log error if not found
		string error = $"Can't fetch download URL for \"{mod.Name}\" (#{mod.Id}) > file \"{file.DisplayName} {file.Version}\" (#{file.Id}).";
		if (exception is not null)
		{
			if (exception is ApiException apiEx)
				error += $"\n\nHTTP {apiEx.Response.Status}: {await apiEx.Response.AsString()}";
			error += $"\n\n{exception}";
		}

		ConsoleHelper.WriteWarningLine(error);

		return [];
	}

	/// <inheritdoc />
	public void Dispose()
	{
		this.MainApi.Dispose();
		this.ExportApi.Dispose();
	}


	/*********
	** Private methods
	*********/
	/// <summary>Parse raw mod data from the ModDrop export API.</summary>
	/// <param name="mod">The raw mod data.</param>
	private ModPageRecord Parse(ModDropModExport mod)
	{
		// get author names
		string? author = mod.UserName?.Trim();
		string? authorLabel = mod.AuthorName?.Trim();
		if (author?.Equals(authorLabel, StringComparison.InvariantCultureIgnoreCase) ?? authorLabel is null)
			authorLabel = null;

		// get last updated
		DateTimeOffset lastUpdated = DateTimeOffset.FromUnixTimeMilliseconds(mod.DateUpdated);
		{
			DateTimeOffset published = DateTimeOffset.FromUnixTimeMilliseconds(mod.DatePublished);
			if (published > lastUpdated)
				lastUpdated = published;
		}

		// get files
		List<ModPageDownloadRecord> files = [];
		foreach (ModDropFileExport rawFile in mod.Files)
		{
			if (rawFile.IsOld || rawFile.IsDeleted || rawFile.IsHidden)
				continue;

			ModPageDownloadRecord file = this.ParseFile(rawFile);

			files.Add(file);

			if (file.Uploaded > lastUpdated)
				lastUpdated = file.Uploaded;
		}

		// get model
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(mod, this.ExcludePageFields);
		this.NormalizeRawFields(otherFields);
		return new ModPageRecord(
			site: ModSite.ModDrop,
			id: mod.Id,
			name: mod.Title,
			tagLine: mod.TagLine,
			description: mod.Description,
			author: author,
			authorLabel: authorLabel,
			pageUrl: mod.PageUrl ?? $"https://www.moddrop.com/stardew-valley/mod/{mod.Id}",
			version: mod.Version,
			updated: lastUpdated,
			downloads: files.ToArray(),
			otherFields: otherFields
		);
	}

	/// <summary>Parse raw download data from the ModDrop export API.</summary>
	/// <param name="file">The raw download data.</param>
	private ModPageDownloadRecord ParseFile(ModDropFileExport file)
	{
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(file, this.ExcludeDownloadFields);
		return new ModPageDownloadRecord(
			id: file.Id,
			type: file is { IsPreRelease: false, IsAlternative: false }
				? ModDownloadType.Main
				: ModDownloadType.Optional,
			displayName: file.Name,
			fileName: file.FileName,
			description: file.Description,
			version: file.Version,
			uploaded: DateTimeOffset.FromUnixTimeMilliseconds(file.DateCreated),
			sizeInBytes: file.Size,
			otherFields: otherFields
		);
	}

	/// <summary>Normalize the raw field values received from the ModDrop API.</summary>
	/// <param name="rawFields">The raw fields to normalize.</param>
	private void NormalizeRawFields(Dictionary<string, JsonNode?>? rawFields)
	{
		if (rawFields is null)
			return;

		// strip transient cache-busting argument in icon URLs
		if (rawFields.TryGetValue("icons", out JsonNode? iconsNode) && iconsNode is JsonObject iconsObj)
		{
			foreach ((string propertyName, JsonNode? rawValue) in iconsObj)
			{
				string? uri = rawValue?.GetValue<string>();
				if (uri is null)
					continue;

				if (uri.Contains("?T="))
				{
					string[] parts = uri.Split("?T=");
					if (long.TryParse(parts[1], out _))
						iconsObj[propertyName] = parts[0];
				}
			}
		}
	}
}
