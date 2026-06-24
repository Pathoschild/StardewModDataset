using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StardewModdingAPI.Toolkit.Framework.Clients.CurseForgeExport;
using StardewModdingAPI.Toolkit.Framework.Clients.CurseForgeExport.ResponseModels;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework.Clients;

/// <summary>A client which fetches mods from the CurseForge export API.</summary>
partial class CurseForgeApiClient : IModSiteClient
{
	/*********
	** Fields
	*********/
	/// <summary>A regex pattern which matches a version number in a CurseForge mod file name.</summary>
	[GeneratedRegex(@"^(?:.+? | *)v?(\d+\.\d+(?:\.\d+)?(?:-.+?)?) *(?:\.(?:zip|rar|7z))?$", RegexOptions.Compiled)]
	private partial Regex VersionInNamePattern { get; }

	/// <summary>The CurseForge export API client.</summary>
	private readonly CurseForgeExportApiClient CurseForge;

	/// <summary>The mod page fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludePageFields =
	[
		// promoted into generic fields
		nameof(CurseForgeModExport.Id),
		nameof(CurseForgeModExport.Name),
		nameof(CurseForgeModExport.ModPageUrl),
		nameof(CurseForgeModExport.Files)
	];

	/// <summary>The download fields which should be excluded from raw data.</summary>
	private readonly HashSet<string> ExcludeDownloadFields =
	[
		// promoted into generic fields
		nameof(CurseForgeFileExport.Id),
		nameof(CurseForgeFileExport.DisplayName),
		nameof(CurseForgeFileExport.FileName),
		nameof(CurseForgeFileExport.FileDate)
	];


	/*********
	** Accessors
	*********/
	/// <inheritdoc />
	public ModSite SiteKey { get; } = ModSite.CurseForge;


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="exportApiUrl">The base URL for the CurseForge export API.</param>
	/// <param name="userAgent">The user agent sent to the mod site API.</param>
	public CurseForgeApiClient(string exportApiUrl, string userAgent)
	{
		this.CurseForge = new CurseForgeExportApiClient(userAgent, exportApiUrl);
	}

	/// <inheritdoc />
	public Task AuthenticateAsync()
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<ExportResult> GetAllModsAsync()
	{
		CurseForgeFullExport export = await this.CurseForge.FetchExportAsync();

		ModPageRecord[] mods = export.Mods.Values
			.Select(this.Parse)
			.ToArray();

		return new ExportResult(export.CacheHeaders.LastModified, mods);
	}

	/// <inheritdoc />
	public Task<Uri[]> GetDownloadUrlsAsync(ModPageRecord mod, ModPageDownloadRecord file)
	{
		return Task.FromResult(
			GetDownloadUrls(mod, file).Select(url => new Uri(url)).ToArray()
		);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		this.CurseForge.Dispose();
	}


	/*********
	** Private methods
	*********/
	/// <summary>Get the download URLs for a CurseForge file.</summary>
	/// <param name="mod">The mod which has the file to download.</param>
	/// <param name="file">The file for which to get download URLs.</param>
	private IEnumerable<string> GetDownloadUrls(ModPageRecord mod, ModPageDownloadRecord file)
	{
		// get API download URL
		if (file.OtherFields?.GetValueOrDefault("downloadUrl")?.GetValue<string>() is { } apiDownloadUrl)
			yield return apiDownloadUrl;

		// build CDN URL manually (API doesn't always return a download URL)
		yield return $"https://www.curseforge.com/api/v1/mods/{mod.Id}/files/{file.Id}/download";
	}

	/// <summary>Parse raw mod data from the CurseForge API.</summary>
	/// <param name="mod">The raw mod data.</param>
	private ModPageRecord Parse(CurseForgeModExport mod)
	{
		// get author names
		string[] authorNames = mod.Authors.Select(p => p.Name).Where(p => p != null).ToArray()!;

		// get last updated
		DateTimeOffset lastUpdated;
		{
			lastUpdated = mod.DateCreated;
			if (mod.DateModified > lastUpdated)
				lastUpdated = mod.DateModified;
			if (mod.DateReleased > lastUpdated)
				lastUpdated = mod.DateReleased;
		}

		// get files
		List<ModPageDownloadRecord> files = [];
		foreach (CurseForgeFileExport rawFile in mod.Files)
		{
			ModPageDownloadRecord file = this.ParseFile(rawFile);

			files.Add(file);

			if (file.Uploaded > lastUpdated)
				lastUpdated = file.Uploaded;
		}

		// get model
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(mod, this.ExcludePageFields);
		return new ModPageRecord(
			site: ModSite.CurseForge,
			id: mod.Id,
			name: mod.Name,
			tagLine: null,
			description: null,
			author: authorNames.FirstOrDefault(),
			authorLabel: authorNames.Length > 1 ? string.Join(", ", authorNames) : null,
			pageUrl: mod.ModPageUrl ?? $"https://www.curseforge.com/projects/{mod.Id}",
			version: null,
			updated: lastUpdated,
			downloads: files.ToArray(),
			otherFields: otherFields
		);
	}

	/// <summary>Parse raw file data from the CurseForge API.</summary>
	/// <param name="file">The raw mod file data.</param>
	private ModPageDownloadRecord ParseFile(CurseForgeFileExport file)
	{
		Dictionary<string, JsonNode?>? otherFields = DataHelper.PropertiesToRawData(file, this.ExcludeDownloadFields);
		return new ModPageDownloadRecord(
			id: file.Id,
			type: file.ReleaseType == 1 ? ModDownloadType.Main : ModDownloadType.Optional, // FileReleaseType: 1=release, 2=beta, 3=alpha
			displayName: file.DisplayName,
			fileName: file.FileName,
			version: this.GetFileVersion(file.DisplayName, file.FileName),
			description: null,
			uploaded: file.FileDate,
			sizeInBytes: 0, // default to detected download size
			otherFields: otherFields
		);
	}

	/// <summary>Get a raw version string for a mod file, if available.</summary>
	/// <param name="displayName">The file's display name.</param>
	/// <param name="fileName">The filename.</param>
	private string? GetFileVersion(string? displayName, string? fileName)
	{
		Match? match = null;

		if (displayName != null)
			match = this.VersionInNamePattern.Match(displayName);

		if ((match is null || !match.Success) && fileName != null)
			match = this.VersionInNamePattern.Match(fileName);

		return match is not null && match.Success
			? match.Groups[1].Value
			: null;
	}
}
