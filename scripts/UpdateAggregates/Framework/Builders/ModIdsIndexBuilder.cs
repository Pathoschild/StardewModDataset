using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.UpdateAggregates.Framework.Builders;

/// <summary>Builds an index lookup of page IDs by mod ID.</summary>
internal class ModIdsIndexBuilder : IAggregateBuilder
{
	/*********
	** Fields
	*********/
	/// <summary>The page update keys by mod ID.</summary>
	private readonly Dictionary<string, HashSet<string>> Data = [];


	/*********
	** Public methods
	*********/
	/// <inheritdoc />
	public void Collect(ModPageRecord modPage)
	{
		foreach (ModPageDownloadRecord download in modPage.Downloads)
		{
			foreach (ModFolderRecord mod in download.Mods)
			{
				if (string.IsNullOrEmpty(mod.Id))
					continue;

				if (!this.Data.TryGetValue(mod.Id, out HashSet<string>? pageKeys))
					this.Data[mod.Id] = pageKeys = [];

				pageKeys.Add($"{modPage.Site}:{modPage.Id}");
			}
		}
	}

	/// <inheritdoc />
	public AggregateData GetAggregate()
	{
		// sort by mod ID
		Dictionary<string, HashSet<string>> sortedModIdsToPageIdsBySite = [];
		foreach ((string modId, HashSet<string> pageKeys) in this.Data.OrderBy(p => p.Key.Replace(".", "").Replace("_", ""), StringComparer.OrdinalIgnoreCase))
			sortedModIdsToPageIdsBySite[modId] = pageKeys;

		// build data
		return new AggregateData(AggregateType.Index, "pages by mod ID", sortedModIdsToPageIdsBySite);
	}
}
