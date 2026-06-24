using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StardewModdingAPI.Toolkit.Framework.ModDataset;
using StardewModdingAPI.Toolkit.Framework.ModScanning;

namespace Pathoschild.ModData.UpdateAggregates.Framework.Builders;

/// <summary>Builds stats for the number of mods by type.</summary>
internal class ModsByTypeStatBuilder : IAggregateBuilder
{
	/*********
	** Fields
	*********/
	/// <summary>The valid mod type values.</summary>
	private static class ModTypes
	{
		/// <summary>The mod type value for a SMAPI (C#) mod.</summary>
		public const string Smapi = "SMAPI";

		/// <summary>The mod type value for an XNB mod.</summary>
		public const string Xnb = "XNB";

		/// <summary>The mod type value for a Content Patcher content pack.</summary>
		public const string ContentPatcher = "Pathoschild.ContentPatcher";

		/// <summary>The mod type prefix for a content pack which doesn't use Content Patcher.</summary>
		public const char OtherPrefix = '@';
	}

	/// <summary>The minimum content packs required for a framework to be tracked separately in the stats.</summary>
	private const int MinContentPacks = 100;

	/// <summary>The number of mods by type.</summary>
	private readonly Dictionary<string, string> ModTypesById = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The number of mods by type.</summary>
	private readonly Dictionary<string, string> PooledModTypesOnPage = new(StringComparer.OrdinalIgnoreCase);


	/*********
	** Public methods
	*********/
	/// <inheritdoc />
	public void Collect(ModPageRecord modPage)
	{
		// track mod types on this page
		Dictionary<string, string> typesOnPage = this.PooledModTypesOnPage;
		typesOnPage.Clear();
		bool hasXnb = false;
		bool hasNonXnb = false;
		foreach (ModPageDownloadRecord download in modPage.Downloads)
		{
			foreach (ModFolderRecord mod in download.Mods)
			{
				string? modId = mod.Id;
				if (string.IsNullOrWhiteSpace(modId))
					modId = null;

				switch (mod.Type)
				{
					case ModType.Ignored:
					case ModType.Invalid:
						continue;

					case ModType.Smapi:
						if (modId != null)
						{
							this.TrackModType(modId, ModTypes.Smapi);
							hasNonXnb = true;
						}

						break;

					case ModType.Xnb:
						hasXnb = true;
						break;

					default:
						if (modId != null)
						{
							string? contentPackFor = mod.Manifest?.ContentPackFor?.UniqueId?.Trim();
							if (!string.IsNullOrWhiteSpace(contentPackFor))
							{
								string type = string.Equals(contentPackFor, ModTypes.ContentPatcher, StringComparison.OrdinalIgnoreCase)
									? ModTypes.ContentPatcher
									: ModTypes.OtherPrefix + contentPackFor;

								this.TrackModType(modId, type);
								hasNonXnb = true;
							}
						}
						break;
				}
			}
		}

		// if the page only has XNB files (which have no mod ID), track the XNB mod using the mod page ID
		if (hasXnb && !hasNonXnb)
			this.TrackModType($"___{modPage.Site}:{modPage.Id}", ModTypes.Xnb);
	}

	/// <inheritdoc />
	public AggregateData GetAggregate()
	{
		// get stats line
		Dictionary<string, int> counts = this.ModTypesById
			.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(p => p.Key == ModTypes.Smapi)
			.ThenByDescending(p => p.Key == ModTypes.ContentPatcher)
			.ThenByDescending(p => p.Key == ModTypes.Xnb)
			.ThenBy(p => p.Key)
			.ToDictionary(p => p.Key, p => p.Count(), StringComparer.OrdinalIgnoreCase);

		// merge content packs with < min usages
		{
			int mergedSum = 0;

			foreach ((string type, int count) in counts)
			{
				if (count < MinContentPacks && type.StartsWith(ModTypes.OtherPrefix))
				{
					mergedSum += count;
					counts.Remove(type);
				}
			}

			if (mergedSum > 0)
				counts["other"] = mergedSum;
		}

		// build aggregate
		JsonObject line = new JsonObject
		{
			["date"] = DateTime.Now.ToString("yyyy-MM-dd")
		};
		foreach ((string type, int count) in counts)
		{
			line.Add(
				type.StartsWith(ModTypes.OtherPrefix) ? type[1..] : type,
				count
			);
		}

		return new AggregateData(AggregateType.Stats, "mods by type", line);
	}


	/*********
	** Private methods
	*********/
	/// <summary>Add a mod's type to the aggregate.</summary>
	/// <param name="modId">The mod ID to track.</param>
	/// <param name="type">The detected mod type.</param>
	private void TrackModType(string modId, string type)
	{
		// skip if it's already tracked with a higher-priority type
		if (this.ModTypesById.TryGetValue(modId, out string? previousType))
		{
			if (type == previousType || this.GetModTypePriority(type) <= this.GetModTypePriority(previousType))
				return;
		}

		// else save type
		this.ModTypesById[modId] = type;
	}

	/// <summary>Get the priority to use when tracking a mod type.</summary>
	/// <param name="type">The mod type.</param>
	private int GetModTypePriority(string type)
	{
		return type switch
		{
			ModTypes.Smapi => 4,
			ModTypes.ContentPatcher => 3,
			ModTypes.Xnb => 1,
			_ => type.StartsWith(ModTypes.OtherPrefix) ? 2 : -1
		};
	}
}
