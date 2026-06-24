using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework;

/// <summary>The result of downloading a mod page download and scanning it for mods.</summary>
/// <param name="FileSizeInBytes">The size of the downloaded file in bytes.</param>
/// <param name="Mods">The mods detected within the download.</param>
/// <param name="DownloadError">The error message indicating why the file could not be downloaded, if applicable.</param>
/// <param name="UnpackError">The error message indicating why the mods could not be unpacked from the downloaded archive, if applicable.</param>
internal record DownloadScanResult(long FileSizeInBytes, ModFolderRecord[] Mods, string? DownloadError = null, string? UnpackError = null)
{
	/// <summary>Whether the mods were successfully downloaded and unpacked.</summary>
	public bool FullyAnalyzed => this.DownloadError is null && this.UnpackError is null && this.Mods.Length > 0;
}
