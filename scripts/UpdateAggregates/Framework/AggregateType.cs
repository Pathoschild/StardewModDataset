namespace Pathoschild.ModData.UpdateAggregates.Framework;

/// <summary>The type for an aggregate file.</summary>
internal enum AggregateType
{
	/// <summary>An index file which summarizes the current repo data. Each builder update overwrites the entire file.</summary>
	Index,

	/// <summary>A stats file which contains JSONL lines for historical data over time. Each builder update appends a line to the file.</summary>
	Stats
}
