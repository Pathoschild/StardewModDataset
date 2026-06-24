using System;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.FetchMods.Framework.Clients;

/// <summary>A collection of mod pages returned by a mod site's export API.</summary>
/// <param name="LastModified">When this export was last updated by the mod site.</param>
/// <param name="Mods">The mod pages received from the export API.</param>
internal record ExportResult(DateTimeOffset LastModified, ModPageRecord[] Mods);
