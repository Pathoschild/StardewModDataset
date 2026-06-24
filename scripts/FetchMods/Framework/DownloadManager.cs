using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Pathoschild.ModData.Common;
using SevenZip;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Framework.ModDataset;
using StardewModdingAPI.Toolkit.Framework.ModScanning;

namespace Pathoschild.ModData.FetchMods.Framework;

/// <summary>Handles downloading, extracting, and scanning mod files into the <c>downloads</c> folder.</summary>
internal class DownloadManager : IDisposable
{
	/*********
	** Fields
	*********/
	/****
	** Constants
	****/
	/// <summary>A prefix added to downloaded files before they're unpacked, to avoid conflicting with the unpacked files.</summary>
	private const string DownloadTempPrefix = "__tempdownload__";

	/// <summary>The file extensions which can be downloaded directly from a mod site which aren't packed archives.</summary>
	private readonly HashSet<string> NonPackedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".dll",
		".exe"
	};

	/****
	** State
	****/
	/// <summary>The full path to the folder in which to store the raw mod downloads.</summary>
	private readonly string DownloadsRoot;

	/// <summary>The path to the 7-zip install directory (containing 7z.dll and 7z.exe).</summary>
	private readonly string SevenZipPath;

	/// <summary>The mod toolkit with which to analyze mod page downloads.</summary>
	private readonly ModToolkit ModToolkit;

	/// <summary>The HTTP client to use when fetching mod downloads.</summary>
	private readonly HttpClient HttpClient;


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="downloadsRoot"><inheritdoc cref="DownloadsRoot" path="/summary"/></param>
	/// <param name="sevenZipPath"><inheritdoc cref="SevenZipPath" path="/summary"/></param>
	/// <param name="userAgent">The user agent sent to the mod site APIs.</param>
	public DownloadManager(string downloadsRoot, string sevenZipPath, string userAgent)
	{
		this.DownloadsRoot = downloadsRoot;
		this.SevenZipPath = sevenZipPath;
		this.ModToolkit = new ModToolkit();

		this.HttpClient = new HttpClient();
		this.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);

		SevenZipBase.SetLibraryPath(Path.Combine(sevenZipPath, "7z.dll"));
	}

	/// <summary>Download, extract, and analyze all files for a mod.</summary>
	/// <param name="modPage">The mod page whose downloads to fetch.</param>
	/// <param name="modDownloadsDir">The full path to the mod's folder within the downloads.</param>
	/// <param name="getDownloadUrls">Get the URLs from which a mod download can be fetched.</param>
	/// <returns>Returns the download and scan results, indexed by download ID.</returns>
	public async Task<Dictionary<long, DownloadScanResult>> DownloadAndScanAsync(ModPageRecord modPage, string modDownloadsDir, Func<ModPageDownloadRecord, Task<Uri[]>> getDownloadUrls)
	{
		// reset download folder
		if (Directory.Exists(modDownloadsDir))
			FileHelper.ForceDelete(new DirectoryInfo(modDownloadsDir));
		Directory.CreateDirectory(modDownloadsDir);

		// process each download
		Dictionary<long, DownloadScanResult> results = [];
		foreach (ModPageDownloadRecord file in modPage.Downloads)
		{
			DirectoryInfo extractInto = new DirectoryInfo(Path.Combine(modDownloadsDir, file.Id.ToString()));
			FileInfo? downloadedFile = null;
			bool success = false;

			try
			{
				// download file
				DownloadResult rawDownload = await this.DownloadFileAsync(file, modDownloadsDir, () => getDownloadUrls(file), this.HttpClient);
				if (!rawDownload.Downloaded)
				{
					ConsoleHelper.WriteWarningLine($"  Warning: can't download {modPage.Id} > {file.Id}: {rawDownload.DownloadError}");
					results[file.Id] = new DownloadScanResult(rawDownload.FileSizeInBytes, [], DownloadError: rawDownload.DownloadError);
					continue;
				}
				downloadedFile = new FileInfo(rawDownload.FilePath);

				// skip zero-byte archive
				if (rawDownload.FileSizeInBytes == 0)
				{
					ConsoleHelper.WriteWarningLine($"  Warning: can't unpack {modPage.Id} > {file.Id}: downloaded file is empty.");
					results[file.Id] = new DownloadScanResult(rawDownload.FileSizeInBytes, [], UnpackError: "Downloaded file is empty (zero bytes).");
					continue;
				}

				// extract download
				ExtractResult extract = await this.ExtractDownload(downloadedFile, extractInto);
				if (!extract.Extracted)
				{
					ConsoleHelper.WriteWarningLine($"  Warning: can't unpack {modPage.Id} > {file.Id}: {extract.Error}");
					results[file.Id] = new DownloadScanResult(rawDownload.FileSizeInBytes, [], UnpackError: extract.Error);
					continue;
				}

				// detect mods in extracted download
				ModFolderRecord[] folders = this.ModToolkit
					.GetModFolders(rootPath: modDownloadsDir, modPath: extractInto.FullName, useCaseInsensitiveFilePaths: true)
					.Select(folder => this.CreateModFolderRecord(folder, file.Id, extractInto.FullName))
					.ToArray();

				results[file.Id] = new DownloadScanResult(rawDownload.FileSizeInBytes, folders);
				success = true;
			}
			finally
			{
				this.TryCleanup(downloadedFile, "download");
				if (!success)
					this.TryCleanup(extractInto, "unpack");
			}
		}

		return results;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		this.HttpClient.Dispose();
	}


	/*********
	** Private methods
	*********/
	/// <summary>Download a file from a mod page.</summary>
	/// <param name="file">The mod file to download.</param>
	/// <param name="modDir">The full path to the directory in which to download the file.</param>
	/// <param name="getDownloadUrls">Get the download URLs for the mod file.</param>
	/// <param name="httpClient">The HTTP client to use when fetching mod downloads.</param>
	/// <returns>Returns the download result, or <c>null</c> if it couldn't be downloaded.</returns>
	private async Task<DownloadResult> DownloadFileAsync(ModPageDownloadRecord file, string modDir, Func<Task<Uri[]>> getDownloadUrls, HttpClient httpClient)
	{
		// get download URLs
		Uri[] urls;
		try
		{
			urls = await getDownloadUrls();
		}
		catch (Exception ex)
		{
			return new DownloadResult(0, null, $"Could not get download URLs: {ex.Message}");
		}

		if (urls.Length == 0)
			return new DownloadResult(0, null, "No download URLs found.");

		// get target path
		string saveToPath;
		{
			string fileExtension = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
			if (string.IsNullOrEmpty(fileExtension))
				fileExtension = ".zip";

			saveToPath = Path.Combine(modDir, $"{DownloadTempPrefix}{file.Id}{fileExtension}");
		}

		// download from first working URL
		bool downloaded = false;
		long downloadedSize = 0;
		string? downloadError = null;
		foreach (Uri url in urls)
		{
			try
			{
				using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
				response.EnsureSuccessStatusCode();

				await using FileStream fileStream = File.Create(saveToPath);
				await response.Content.CopyToAsync(fileStream);

				downloaded = true;
				downloadedSize = fileStream.Length;
				break;
			}
			catch (Exception ex)
			{
				string displayUrl = url.GetLeftPart(UriPartial.Path).TrimEnd('/'); // omit transient arguments (e.g. token, user ID, expiry, etc)
				if (downloadError is null)
					downloadError = $"Download failed from {displayUrl}: {ex.Message}";
				else
					downloadError += $"\nDownload failed from {displayUrl}: {ex.Message}";
			}
		}

		return downloaded
			? new DownloadResult(downloadedSize, saveToPath, null)
			: new DownloadResult(downloadedSize, null, downloadError ?? "Download failed with unknown reason.");
	}

	/// <summary>Extract a downloaded file to the given folder, and delete the packed archive file.</summary>
	/// <param name="file">The archive file to extract.</param>
	/// <param name="extractInto">The directory into which to extract the files.</param>
	private async Task<ExtractResult> ExtractDownload(FileInfo file, DirectoryInfo extractInto)
	{
		extractInto.Create();

		// handle valid non-packed file types
		if (NonPackedFileExtensions.Contains(file.Extension))
		{
			string newFileName = file.Name;
			if (newFileName.StartsWith(DownloadTempPrefix))
				newFileName = newFileName[DownloadTempPrefix.Length..];

			file.MoveTo(Path.Combine(extractInto.FullName, newFileName));

			return new ExtractResult(true, null);
		}

		// else unpack using 7-zip
		string? error = await this.ExtractFileUsing7ZipImpl(file, extractInto);
		return new ExtractResult(Extracted: error is null, Error: error);
	}

	/// <summary>Extract a file using 7-zip.</summary>
	/// <param name="file">The archive file to extract.</param>
	/// <param name="extractInto">The directory into which to extract the files.</param>
	/// <returns>Returns an error message if extraction fails, else <c>null</c>.</returns>
	/// <remarks>This is a low-level wrapper around 7-zip; most code should use <see cref="ExtractDownload"/> instead.</remarks>
	private async Task<string?> ExtractFileUsing7ZipImpl(FileInfo file, DirectoryInfo extractInto)
	{
		try
		{
			using SevenZipExtractor unpacker = new SevenZipExtractor(file.FullName);
			await unpacker.ExtractArchiveAsync(extractInto.FullName);
			return null;
		}
		catch (Exception ex)
		{
			// The 7-zip managed DLL applies less sanitization than the command-line version (e.g. for trailing spaces in
			// directory names). If the managed DLL failed, try using the command-line version instead. This is only a fallback
			// since invoking an external process is much slower.
			try
			{
				using Process process = Process.Start(new ProcessStartInfo
				{
					FileName = Path.Combine(this.SevenZipPath, "7z.exe"),
					ArgumentList = { "x", file.FullName, $"-o{extractInto.FullName}", "-y", "-bso0" },
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				})!;

				Task<string> readOutput = process.StandardOutput.ReadToEndAsync();
				Task<string> readError = process.StandardError.ReadToEndAsync();
				await Task.WhenAll(readOutput, readError);
				await process.WaitForExitAsync();

				if (process.ExitCode != 0)
				{
					string error = readError.Result.Trim();
					if (string.IsNullOrEmpty(error))
						error = readOutput.Result.Trim();
					return $"7-zip exited with code {process.ExitCode}: {this.Normalize7ZipCliError(error, file.FullName)}";
				}

				return null;
			}
			catch (Exception innerEx)
			{
				string error = ex.Message != innerEx.Message
					? $"{this.NormalizeSevenZipSharpError(ex.Message)}\n{innerEx.Message}"
					: ex.Message;

				return error;
			}
		}
	}

	/// <summary>Normalize a raw error message from the 7-zip managed DLL.</summary>
	/// <param name="error">The raw 7-zip error message.</param>
	private string NormalizeSevenZipSharpError(string error)
	{
		string[] errors = error.Split(Environment.NewLine, StringSplitOptions.TrimEntries);

		switch (errors)
		{
			case ["Invalid archive: open/read error! Is it encrypted and a wrong password was provided?", "If your archive is an exotic one, it is possible that SevenZipSharp has no signature for its format and thus decided it is TAR by mistake."]:
				return "Invalid archive: may be password-protected or corrupted.";

			default:
				return error;
		}
	}

	/// <summary>Normalize a raw error message from the 7-zip command-line tool.</summary>
	/// <param name="error">The raw 7-zip error message.</param>
	/// <param name="archiveFilePath">The archive file that couldn't be extracted.</param>
	private string Normalize7ZipCliError(string error, string archiveFilePath)
	{
		// strip redundant prefix
		if (error.StartsWith("ERROR:"))
			error = error["ERROR:".Length..].TrimStart();
		else if (error.StartsWith("ERRORS:"))
			error = error["ERRORS:".Length..].TrimStart();

		// strip temporary file path
		if (error.StartsWith(archiveFilePath))
			error = error[archiveFilePath.Length..].TrimStart();

		// simplify error messages
		string newLine = Environment.NewLine;
		string[] errors = error.Split(newLine, StringSplitOptions.RemoveEmptyEntries);
		switch (errors)
		{
			case ["Unexpected end of archive", _] when errors[1].StartsWith("ERROR: Data Error :"):
				return errors[0] + '.'; // omit specific file at which it failed (not relevant for mod metadata)

			case ["Cannot open the file as archive", "Operation did not complete successfully because the file contains a virus or potentially unwanted software."]:
				return "The file contains a virus or potentially unwanted software."; // show error directly

			default:
				return error;
		}
	}

	/// <summary>Try to delete a file or directory that's no longer needed after processing.</summary>
	/// <param name="entry">The file or directory to delete.</param>
	/// <param name="label">A name for the file or directory for any logged error messages (e.g. 'download' or 'unpack').</param>
	private void TryCleanup(FileSystemInfo? entry, string label)
	{
		if (entry is null)
			return;

		try
		{
			FileHelper.ForceDelete(entry);
		}
		catch (Exception ex)
		{
			ConsoleHelper.WriteWarningLine($"Failed to delete {label} at {entry.FullName}.\n{ex}");
		}
	}

	/// <summary>Create a mod folder record for a raw SMAPI toolkit mod scan result.</summary>
	/// <param name="folder">The SMAPI toolkit scan scan result.</param>
	/// <param name="fileId">The file ID within the mod site.</param>
	/// <param name="extractDir">The directory containing the extracted mod files.</param>
	private ModFolderRecord CreateModFolderRecord(ModFolder folder, long fileId, string extractDir)
	{
		// if the mod is invalid, strip the repo root paths from the mod metadata
		string displayName = folder.DisplayName;
		if (folder.Type is ModType.Invalid or ModType.Ignored or ModType.Xnb)
		{
			string prefix = fileId.ToString() + Path.DirectorySeparatorChar;
			if (displayName.StartsWith(prefix))
				displayName = displayName[prefix.Length..];
		}

		// get manifest data
		ModManifestRecord? manifest;
		try
		{
			manifest = folder.Manifest is not null
				? new ModManifestRecord(folder.Manifest)
				: null;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed parsing manifest for file {fileId}.", ex);
		}

		string? relativePath = Path.GetRelativePath(extractDir, folder.Directory.FullName);
		if (relativePath == ".")
			relativePath = null;

		// build record
		return new ModFolderRecord(
			id: folder.Manifest?.UniqueID?.Trim(),
			displayName: displayName.Trim(),
			type: folder.Type,
			relativePath: relativePath,
			manifest: manifest,
			manifestParseError: folder.ManifestParseError is ModParseError.None ? null : folder.ManifestParseError,
			manifestParseErrorText: folder.ManifestParseErrorText
		);
	}

	/// <summary>The result of a mod page download operation.</summary>
	/// <param name="FileSizeInBytes">The size of the downloaded file in bytes.</param>
	/// <param name="FilePath">The full path to the downloaded file, if it was downloaded.</param>
	/// <param name="DownloadError">The error message indicating why the file could not be downloaded, if applicable.</param>
	internal record DownloadResult(long FileSizeInBytes, string? FilePath, string? DownloadError)
	{
		/// <summary>Whether the file was downloaded successfully.</summary>
		[MemberNotNullWhen(true, nameof(FilePath))]
		[MemberNotNullWhen(false, nameof(DownloadError))]
		public bool Downloaded => this is { FilePath: not null, DownloadError: null };
	}

	/// <summary>The result of a file extract operation.</summary>
	/// <param name="Extracted">Whether the files were successfully extracted (or moved into place if the file isn't an archive).</param>
	/// <param name="Error">The error message indicating why the file could not be extracted, if applicable.</param>
	internal record ExtractResult([property: MemberNotNullWhen(false, "Error")] bool Extracted, string? Error);
}
