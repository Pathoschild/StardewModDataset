using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pathoschild.ModData.Common;
using Pathoschild.ModData.UpdateAggregates.Framework;
using Pathoschild.ModData.UpdateAggregates.Framework.Builders;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.UpdateAggregates;

/// <summary>The tool entry point.</summary>
internal static class Program
{
	/*********
	** Public methods
	*********/
	/// <summary>Run the tool.</summary>
	public static async Task Main()
	{
		ConsoleHelper.WriteLine("Building aggregates...");

		// init
		Settings settings = ConfigHelper.LoadAppSettings<Settings>();
		IAggregateBuilder[] builders = GetAggregateBuilders(settings).ToArray();

		// get sites to scan
		(ModSite Site, DirectoryInfo[] BucketDirs)[] sites = FileHelper
			.EnumerateDataSites()
			.Select(p => (p.Site, FileHelper.EnumerateBuckets(p.Directory).ToArray()))
			.ToArray();

		// scan sites
		await ConsoleHelper.RunWithProgressBarsAsync(
			labels: sites.Select(p => p.Site.ToString()),
			run: progress =>
			{
				foreach ((ModSite site, DirectoryInfo[] bucketDirs) in sites)
				{
					string progressLabel = site.ToString();
					progress.Start(progressLabel, bucketDirs.Length);

					foreach (DirectoryInfo bucketDir in bucketDirs)
					{
						ConsoleHelper.WriteLine($"  Processing {site} {bucketDir.Name}...");

						foreach (FileInfo modFile in FileHelper.EnumerateMetadataModsInBucket(bucketDir))
						{
							// parse data
							ModPageRecord? modPage = FileHelper.TryReadModPageFile(modFile.FullName);
							if (modPage is null)
								continue;

							// add to builders
							foreach (IAggregateBuilder builder in builders)
								builder.Collect(modPage);
						}

						progress.Increment(progressLabel);
					}
				}

				return Task.CompletedTask;
			}
		);

		// save aggregate files
		ConsoleHelper.WriteLine("Saving aggregates...");
		foreach (IAggregateBuilder builder in builders)
		{
			AggregateData aggregate = builder.GetAggregate();

			switch (aggregate.Type)
			{
				case AggregateType.Index:
					{
						string path = Path.Combine(FileHelper.IndexesPath, $"{aggregate.Name}.json");
						await FileHelper.WriteJsonFileAsync(path, aggregate.Data);
					}
					break;

				case AggregateType.Stats:
					{
						string path = Path.Combine(FileHelper.StatsPath, $"{aggregate.Name}.jsonl");
						await FileHelper.AppendJsonLineAsync(path, aggregate.Data);
					}
					break;

				default:
					throw new InvalidOperationException($"Aggregate builder '{aggregate.GetType().Name}' returned unsupported aggregate type '{aggregate.Type}'.");
			}
		}
		ConsoleHelper.WriteLine();
	}


	/*********
	** Private methods
	*********/
	/// <summary>Get the aggregate builders to apply based on the settings.</summary>
	/// <param name="settings">The settings to apply.</param>
	private static IEnumerable<IAggregateBuilder> GetAggregateBuilders(Settings settings)
	{
		bool hasDownloadsDir = Directory.Exists(FileHelper.DownloadsPath);

		/****
		** Indexes
		****/
		if (settings.UpdateErrorIndexes)
		{
			yield return new ErrorsIndexBuilder("errors on download", download => download.DownloadError);
			yield return new ErrorsIndexBuilder("errors on unpack", download => download.UnpackError);
		}

		if (settings.UpdateModIdsIndex)
			yield return new ModIdsIndexBuilder();

		/****
		** Stats
		****/
		if (settings.UpdateContentPatcherFormatVersions)
		{
			if (hasDownloadsDir)
				yield return new ContentPatcherFormatVersionsStatBuilder();
			else
				ConsoleHelper.WriteWarningLine($"  NOTE: disabled {nameof(settings.UpdateContentPatcherFormatVersions)} because it requires the '{Path.GetFileName(FileHelper.DownloadsPath)}' folder, which wasn't found.");
		}

		if (settings.UpdateModsByType)
			yield return new ModsByTypeStatBuilder();
	}
}
