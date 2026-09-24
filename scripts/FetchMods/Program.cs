using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pathoschild.ModData.Common;
using Pathoschild.ModData.FetchMods.Framework;
using Pathoschild.ModData.FetchMods.Framework.Clients;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods;

/// <summary>The tool entry point.</summary>
internal static class Program
{
	/*********
	** Fields
	*********/
	/// <inheritdoc cref="FileHelper.RootPath" />
	private static readonly string RootPath = FileHelper.RootPath;

	/// <inheritdoc cref="FileHelper.DataPath" />
	private static readonly string DataPath = FileHelper.DataPath;

	/// <inheritdoc cref="FileHelper.DownloadsPath" />
	private static readonly string DownloadsPath = FileHelper.DownloadsPath;


	/*********
	** Public methods
	*********/
	/// <summary>Run the tool.</summary>
	public static async Task Main()
	{
		ConsoleHelper.WriteLine("Initializing...");

		// init
		Settings settings = ConfigHelper.LoadAppSettings<Settings>();
		using DownloadManager downloadManager = new DownloadManager(DownloadsPath, settings.SevenZipPath, settings.UserAgent);

		// process sites
		ConsoleHelper.WriteLine();
		await ConsoleHelper.RunWithProgressBarsAsync(
			labels: [
				nameof(ModSite.CurseForge),
				nameof(ModSite.ModDrop),
				nameof(ModSite.Nexus)
			],
			run: async progress =>
			{
				await ProcessSiteAsync(() => new CurseForgeApiClient(settings.CurseForgeExportUrl, settings.UserAgent), downloadManager, settings, progress);
				await ProcessSiteAsync(() => new ModDropApiClient(settings.ModDropExportUrl, settings.UserAgent, settings.ModDropUser, settings.ModDropPassword), downloadManager, settings, progress);
				await ProcessSiteAsync(() => new NexusApiClient(settings.NexusExportUrl, settings.UserAgent, settings.NexusApiKey, "StardewModDataset", "1.0.0"), downloadManager, settings, progress);
			}
		);
		ConsoleHelper.WriteLine("Done.");
	}


	/*********
	** Private methods
	*********/
	/// <summary>Fetch mods from a mod site and update the data files and downloads to match.</summary>
	/// <param name="buildClient">Create a new instance of the mod site's API client. This will be disposed automatically after use.</param>
	/// <param name="downloadManager">The mod download manager with which to download and analyze mod files.</param>
	/// <param name="settings">The tool settings to apply.</param>
	/// <param name="progress">The progress bars to update.</param>
	private static async Task ProcessSiteAsync(Func<IModSiteClient> buildClient, DownloadManager downloadManager, Settings settings, ConsoleProgress progress)
	{
		// init client
		using IModSiteClient client = buildClient();
		ConsoleHelper.WriteLine($"Processing {client.SiteKey}...");
		await client.AuthenticateAsync();

		// fetch new/updated mods
		int added = 0, updated = 0, skipped = 0;
		HashSet<long> exportedIds = [];
		{
			// fetch export
			ConsoleHelper.WriteLine("  Fetching new or updated mods...");
			ExportResult export = await client.GetAllModsAsync();
			TimeSpan exportAge = DateTimeOffset.UtcNow - export.LastModified;
			if (exportAge.TotalHours > settings.MaxExportAgeInHours)
				ConsoleHelper.WriteWarningLine($"The data from the {client.SiteKey} export API is more than {settings.MaxExportAgeInHours} hours old (updated {Math.Round(exportAge.TotalHours, 2)} hours ago).");

			// init progress bar
			string progressLabel = client.SiteKey.ToString();
			progress.Start(progressLabel, export.Mods.Length);

			// scan mods
			foreach (ModPageRecord modPage in export.Mods)
			{
				try
				{
					exportedIds.Add(modPage.Id);

					// get cached data
					string dataFilePath = FileHelper.GetDataFilePath(client.SiteKey, modPage.Id);
					string downloadDirPath = FileHelper.GetDownloadDirPath(client.SiteKey, modPage.Id);

					ModPageRecord? cached = FileHelper.TryReadModPageFile(dataFilePath);

					// skip if already up-to-date
					if (IsUpToDate(cached, modPage, settings.RetryPreviousFailedDownloads))
					{
						skipped++;
						continue;
					}
					ConsoleHelper.WriteLine($"  {(cached is null ? "Adding" : "Updating")} mod page {modPage.Id} ({modPage.Name})...");

					// fetch download data
					{
						Dictionary<long, DownloadScanResult> downloads = await downloadManager.DownloadAndScanAsync(modPage, downloadDirPath, file => GetDownloadUrlsAsync(client, modPage, file));

						for (int i = 0; i < modPage.Downloads.Length; i++)
						{
							ModPageDownloadRecord file = modPage.Downloads[i];
							DownloadScanResult analysis = downloads[file.Id];
							file.Mods.Clear(); // should already be empty since it's a fresh instance from the export

							// keep previous successful info if download failed
							if (!analysis.FullyAnalyzed && cached?.Downloads.FirstOrDefault(p => p.Id == file.Id) is { FullyAnalyzed: true } cachedResult)
							{
								file.Mods.AddRange(cachedResult.Mods);
								continue;
							}

							// else save new info
							{
								file.Mods.AddRange(analysis.Mods);

								long fileSizeInBytes = file.SizeInBytes > 0
									? file.SizeInBytes
									: analysis.FileSizeInBytes; // use download size if mod site didn't provide it

								if (file.DownloadError != analysis.DownloadError || file.UnpackError != analysis.UnpackError || file.SizeInBytes != fileSizeInBytes)
								{
									modPage.Downloads[i] = new ModPageDownloadRecord(
										id: file.Id,
										type: file.Type,
										displayName: file.DisplayName,
										fileName: file.FileName,
										description: file.Description,
										version: file.Version,
										sizeInBytes: fileSizeInBytes,
										uploaded: file.Uploaded,
										otherFields: file.OtherFields,
										mods: file.Mods,
										downloadError: analysis.DownloadError,
										unpackError: analysis.UnpackError
									);
								}
							}
						}
					}

					// save data
					await FileHelper.WriteJsonFileAsync(dataFilePath, modPage);
					if (cached is null)
						added++;
					else
						updated++;
				}
				finally
				{
					progress.Increment(progressLabel);
				}
			}
		}

		// handle removed mods
		int deleted = 0;
		{
			ConsoleHelper.WriteLine($"  Scanning {client.SiteKey} for deleted mods...");
			DirectoryInfo siteDataDir = new DirectoryInfo(Path.Combine(DataPath, client.SiteKey.ToString()));
			if (siteDataDir.Exists)
			{
				foreach (DirectoryInfo bucketDir in FileHelper.EnumerateBuckets(siteDataDir))
				{
					foreach (FileInfo file in FileHelper.EnumerateMetadataModsInBucket(bucketDir))
					{
						// parse mod page ID
						if (!long.TryParse(Path.GetFileNameWithoutExtension(file.Name), out long localId))
						{
							ConsoleHelper.WriteWarningLine($"  Filename at {Path.GetRelativePath(RootPath, file.FullName)} can't be parsed as a mod ID (is this a valid file?).");
							continue;
						}

						// skip if still exists
						if (exportedIds.Contains(localId))
							continue;

						// get display name for output
						string modLabel = Path.GetRelativePath(RootPath, file.FullName);
						if (FileHelper.TryReadModPageFile(file.FullName) is { } modPage)
							modLabel += $" ({modPage.Name})";

						// delete metadata + downloads
						if (settings.DeleteRemovedMods)
						{
							ConsoleHelper.WriteLine($"  Removing {modLabel}...");
							file.Delete();

							string downloadPath = FileHelper.GetDownloadDirPath(client.SiteKey, localId);
							if (Directory.Exists(downloadPath))
								FileHelper.ForceDelete(new DirectoryInfo(downloadPath));
						}
						else
							ConsoleHelper.WriteLine($"  Would have removed {modLabel} (enable {nameof(Settings.DeleteRemovedMods)} to delete it).");

						deleted++;
					}
				}
			}
		}

		ConsoleHelper.WriteLine($"  {added} added, {updated} updated, {skipped} unchanged, {deleted} {(settings.DeleteRemovedMods ? "deleted" : "can be deleted")}.");
		ConsoleHelper.WriteLine();
	}

	/// <summary>Get the URLs from which a mod download can be fetched.</summary>
	/// <param name="client">The mod site client.</param>
	/// <param name="mod">The mod page.</param>
	/// <param name="file">The download on the mod page for which to get URLs.</param>
	private static async Task<Uri[]> GetDownloadUrlsAsync(IModSiteClient client, ModPageRecord mod, ModPageDownloadRecord file)
	{
		const int maxRetries = 3;
		for (int attempt = 0; attempt < maxRetries; attempt++)
		{
			try
			{
				return await client.GetDownloadUrlsAsync(mod, file);
			}
			catch (RateLimitedException ex)
			{
				if (attempt >= maxRetries - 1)
					throw;

				ConsoleHelper.WriteLine($"  Rate limited: {ex.RateLimitSummary}. Resuming in {ex.TimeUntilRetry.TotalMinutes:0.0}m.");
				await Task.Delay(ex.TimeUntilRetry);
			}
		}

		return [];
	}

	/// <summary>Get whether the cached mod page data matches the fetched data.</summary>
	/// <param name="cached">The cached local data.</param>
	/// <param name="fresh">The fresh data fetched from the mod site.</param>
	/// <param name="retryFailedDownloads">Whether files which previously failed to download (e.g. due to being quarantined) should be retried.</param>
	private static bool IsUpToDate(ModPageRecord? cached, ModPageRecord fresh, bool retryFailedDownloads)
	{
		if (cached is null)
			return false;

		if (
			cached.Name != fresh.Name
			|| cached.Author != fresh.Author
			|| cached.AuthorLabel != fresh.AuthorLabel
			|| cached.Version != fresh.Version
			|| cached.Updated != fresh.Updated
			|| cached.Downloads.Length != fresh.Downloads.Length
		)
			return false;

		for (int i = 0; i < cached.Downloads.Length; i++)
		{
			var cachedFile = cached.Downloads[i];
			var freshFile = fresh.Downloads[i];

			if (
				cachedFile.Id != freshFile.Id
				|| cachedFile.DisplayName != freshFile.DisplayName
				|| cachedFile.FileName != freshFile.FileName
				|| cachedFile.Version != freshFile.Version
			)
				return false;

			if (retryFailedDownloads && !cachedFile.FullyAnalyzed)
				return false;
		}

		return true;
	}
}
