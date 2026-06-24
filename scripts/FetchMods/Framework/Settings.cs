using System.ComponentModel.DataAnnotations;

namespace Pathoschild.ModData.FetchMods.Framework;

/// <summary>The tool settings.</summary>
internal class Settings
{
	/*********
	** Accessors
	*********/
	/****
	** Behavior
	****/
	/// <summary>Whether to delete mods which no longer exist on the mod sites. If false, they'll show warnings instead.</summary>
	public required bool DeleteRemovedMods { get; init; }

	/// <summary>Whether to try redownloading mods which couldn't be downloaded in the past (e.g. due to being quarantined).</summary>
	public required bool RetryPreviousFailedDownloads { get; init; }

	/// <summary>The maximum age in hours for which a mod export is considered fresh.</summary>
	/// <remarks>Older exports are still used but will log warnings.</remarks>
	public required int MaxExportAgeInHours { get; init; }

	/****
	** File paths
	****/
	/// <summary>The path to the 7-zip install directory (containing 7z.dll and 7z.exe).</summary>
	[Required]
	public required string SevenZipPath { get; init; }

	/****
	** Generic HTTP
	****/
	/// <summary>The user agent sent to the mod site APIs.</summary>
	[Required]
	public required string UserAgent { get; init; }

	/****
	** CurseForge
	****/
	/// <summary>The base URL for the CurseForge export API.</summary>
	[Required]
	public required string CurseForgeExportUrl { get; init; }

	/****
	** ModDrop
	****/
	/// <summary>The base URL for the ModDrop export API.</summary>
	[Required]
	public required string ModDropExportUrl { get; init; }

	/// <summary>The ModDrop username with which to authenticate.</summary>
	[Required]
	public required string ModDropUser { get; init; }

	/// <summary>The ModDrop password with which to authenticate.</summary>
	[Required]
	public required string ModDropPassword { get; init; }

	/****
	** Nexus
	****/
	/// <summary>The base URL for the Nexus export API.</summary>
	[Required]
	public required string NexusExportUrl { get; init; }

	/// <summary>The Nexus API key with which to authenticate.</summary>
	[Required]
	public required string NexusApiKey { get; init; }
}
