using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.UpdateAggregates.Framework;

/// <summary>Collects data from mod pages to build an aggregate file.</summary>
internal interface IAggregateBuilder
{
	/*********
	** Public methods
	*********/
	/// <summary>Add a mod page to the aggregate.</summary>
	/// <param name="modPage">The mod page to add.</param>
	void Collect(ModPageRecord modPage);

	/// <summary>Get the collected aggregate data.</summary>
	AggregateData GetAggregate();
}
