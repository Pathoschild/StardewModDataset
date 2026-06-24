using System;
using System.Threading.Tasks;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework.Clients;

/// <summary>A client which fetches mods from a particular mod site.</summary>
internal interface IModSiteClient : IDisposable
{
	/*********
	** Accessors
	*********/
	/// <summary>The identifier for this mod site used in update keys.</summary>
	ModSite SiteKey { get; }


	/*********
	** Methods
	*********/
	/// <summary>Authenticate with the mod site if needed.</summary>
	Task AuthenticateAsync();

	/// <summary>Get every mod currently available on this site.</summary>
	Task<ExportResult> GetAllModsAsync();

	/// <summary>Get the download URLs for a file. If this returns multiple URLs, they're assumed to be mirrors and the first working URL will be used.</summary>
	/// <param name="mod">The mod for which to get download URLs.</param>
	/// <param name="file">The file for which to get download URLs.</param>
	/// <exception cref="RateLimitedException">The API client has exceeded the API's rate limits.</exception>
	Task<Uri[]> GetDownloadUrlsAsync(ModPageRecord mod, ModPageDownloadRecord file);
}
