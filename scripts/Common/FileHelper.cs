using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.Common;

/// <summary>Provides utilities for reading and writing files in the repository.</summary>
public static class FileHelper
{
	/*********
	** Fields
	*********/
	/// <summary>The number of mod IDs to include in each bucket when saving mod metadata files.</summary>
	private static readonly Dictionary<ModSite, int> BucketSizes = new()
	{
		[ModSite.CurseForge] = 10_000,
		[ModSite.ModDrop] = 10_000,
		[ModSite.Nexus] = 1_000
	};

	/// <summary>The serializer options to use when writing JSON.</summary>
	private static readonly JsonSerializerOptions JsonWriteOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>The serializer options to use when writing a JSONL line.</summary>
	private static readonly JsonSerializerOptions JsonLineWriteOptions = new()
	{
		WriteIndented = false,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};


	/*********
	** Accessors
	*********/
	/// <summary>The full path to the dataset folder in the repo.</summary>
	public static readonly string RootPath = Path.Combine(FileHelper.FindRepoRoot(), "dataset");

	/// <summary>The full path to the folder in which to store the mod data JSON files.</summary>
	public static readonly string DataPath = Path.Combine(RootPath, "data");

	/// <summary>The full path to the folder in which to store the raw mod downloads.</summary>
	public static readonly string DownloadsPath = Path.Combine(RootPath, "downloads");

	/// <summary>The full path to the folder in which to store the JSON index files.</summary>
	public static readonly string IndexesPath = Path.Combine(RootPath, "indexes");

	/// <summary>The full path to the folder in which to store the JSONL stats files.</summary>
	public static readonly string StatsPath = Path.Combine(RootPath, "stats");


	/*********
	** Public methods
	*********/
	/****
	** File reads
	****/
	/// <summary>Get the absolute path to the folder containing a mod's downloaded files.</summary>
	/// <param name="site">The mod site which has the mod page.</param>
	/// <param name="modId">The mod page ID within the site.</param>
	public static string GetDownloadDirPath(ModSite site, long modId)
	{
		return Path.Combine(DownloadsPath, GetRelativePath(site, modId));
	}

	/// <summary>Get the absolute path to the folder containing a mod's data.</summary>
	/// <param name="site">The mod site which has the mod page.</param>
	/// <param name="modId">The mod page ID within the site.</param>
	public static string GetDataFilePath(ModSite site, long modId)
	{
		return Path.Combine(DataPath, $"{GetRelativePath(site, modId)}.json");
	}

	/// <summary>Try to deserialize a mod page file.</summary>
	/// <param name="path">The path to the file to read.</param>
	/// <returns>Returns the parsed model if valid, else <c>null</c>.</returns>
	public static ModPageRecord? TryReadModPageFile(string path)
	{
		if (!File.Exists(path))
			return null;

		try
		{
			using FileStream readStream = File.OpenRead(path);
			return JsonSerializer.Deserialize<ModPageRecord>(readStream);
		}
		catch (Exception ex)
		{
			ConsoleHelper.WriteWarningLine($"  Failed deserializing mod page at {Path.GetRelativePath(RootPath, path)}.\n{ex}");

			return null;
		}
	}

	/// <summary>Enumerate the site directories in the <see cref="DataPath"/>.</summary>
	public static IEnumerable<(ModSite Site, DirectoryInfo Directory)> EnumerateDataSites()
	{
		foreach (DirectoryInfo siteDir in new DirectoryInfo(DataPath).GetDirectories())
		{
			// parse site key
			if (!Enum.TryParse(siteDir.Name, out ModSite site))
			{
				ConsoleHelper.WriteWarningLine($"  Ignored invalid folder '{siteDir.Name}' which doesn't match a known site.");
				continue;
			}

			yield return (site, siteDir);
		}
	}

	/// <summary>Enumerate the bucket directories in a <see cref="DataPath"/> or <see cref="DownloadsPath"/> site directory.</summary>
	/// <param name="siteDir">The site directory whose mod pages to enumerate.</param>
	public static IEnumerable<DirectoryInfo> EnumerateBuckets(DirectoryInfo siteDir)
	{
		return siteDir.GetDirectories().OrderBy(GetNumericSortKey);
	}

	/// <summary>Enumerate the mod folders in a bucket directory returned by <see cref="EnumerateBuckets"/>.</summary>
	/// <param name="bucketDir">The bucket directory whose mod pages to enumerate.</param>
	public static IEnumerable<FileInfo> EnumerateMetadataModsInBucket(DirectoryInfo bucketDir)
	{
		return bucketDir.GetFiles().OrderBy(GetNumericSortKey);
	}


	/****
	** File writes
	****/
	/// <summary>Write a JSON file with the default settings.</summary>
	/// <param name="path">The path to the JSON file to save.</param>
	/// <param name="data">The data to save.</param>
	public static async Task WriteJsonFileAsync(string path, object data)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		await using FileStream stream = File.Create(path);
		await JsonSerializer.SerializeAsync(stream, data, JsonWriteOptions);
	}

	/// <summary>Append a data line to a JSONL file.</summary>
	/// <param name="path">The path to the JSONL file to save.</param>
	/// <param name="data">The data to save.</param>
	public static async Task AppendJsonLineAsync(string path, object data)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		byte[] line =
		[
			.. JsonSerializer.SerializeToUtf8Bytes(data, JsonLineWriteOptions),
			.. Encoding.UTF8.GetBytes(Environment.NewLine)
		];
		await File.AppendAllBytesAsync(path, line);
	}

	/****
	** File management
	****/
	/// <summary>Delete a file or folder regardless of file permissions, and block until deletion completes.</summary>
	public static void ForceDelete(FileSystemInfo entry)
	{
		const int errorMaxRetries = 2;
		const int errorPauseMilliseconds = 2000;

		const int staleMaxRetries = 10;
		const int stalePauseMilliseconds = 500;

		for (int errorRetry = 0; errorRetry <= errorMaxRetries; errorRetry++)
		{
			try
			{
				entry.Refresh();
				if (!entry.Exists)
					return;

				if (entry is DirectoryInfo folder)
				{
					foreach (FileSystemInfo child in folder.GetFileSystemInfos())
						ForceDelete(child);
				}

				entry.Attributes = FileAttributes.Normal;
				entry.Delete();

				for (int staleRetry = 0; staleRetry <= staleMaxRetries; staleRetry++)
				{
					entry.Refresh();
					if (!entry.Exists)
						break;

					Thread.Sleep(stalePauseMilliseconds);
				}

				entry.Refresh();
				if (entry.Exists)
					throw new IOException($"Timed out trying to delete {entry.FullName}");
			}
			catch (Exception ex) when (ex is not IOException || ex.Message != $"Timed out trying to delete {entry.FullName}")
			{
				if (errorRetry < errorMaxRetries)
				{
					Thread.Sleep(errorPauseMilliseconds);
					continue;
				}

				throw new InvalidOperationException($"Failed deleting {(entry is DirectoryInfo ? "folder" : "file")} {entry.FullName}", ex);
			}
		}
	}


	/*********
	** Private methods
	*********/
	/// <summary>Get the full path to the root of the current Git repository.</summary>
	/// <exception cref="InvalidOperationException">The root path couldn't be found.</exception>
	private static string FindRepoRoot()
	{
		for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
		{
			if (Directory.Exists(Path.Combine(dir, ".git")))
				return dir;
		}

		throw new InvalidOperationException("Could not find repository root.");
	}

	/// <summary>Get the relative path to a mod within the data or downloads folder.</summary>
	/// <param name="site">The mod site which has the mod page.</param>
	/// <param name="modId">The mod page ID within the site.</param>
	private static string GetRelativePath(ModSite site, long modId)
	{
		return BucketSizes.TryGetValue(site, out int bucketSize)
			? Path.Combine(site.ToString(), (modId / bucketSize).ToString(), modId.ToString())
			: throw new KeyNotFoundException($"Unknown mod site '{site}'.");
	}

	/// <summary>Get the sort key for a directory or file with a numeric name.</summary>
	/// <param name="entry">The entry to sort.</param>
	private static string GetNumericSortKey(FileSystemInfo entry)
	{
		return entry.Name.PadLeft(19, '0');
	}
}
