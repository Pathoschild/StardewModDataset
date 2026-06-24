namespace Pathoschild.ModData.UpdateAggregates.Framework;

/// <summary>The tool settings.</summary>
internal class Settings
{
	/*********
	** Accessors
	*********/
	/****
	** Aggregates to update
	****/
	/// <summary>Whether to update the 'errors on download' and 'errors on unpack' index files.</summary>
	public required bool UpdateErrorIndexes { get; init; }

	/// <summary>Whether to update the 'pages by mod ID' index file.</summary>
	public required bool UpdateModIdsIndex { get; init; }

	/// <summary>Whether to update the 'mods by type' stats file.</summary>
	public required bool UpdateModsByType { get; init; }

	/// <summary>Whether to update the 'Content Patcher format versions' stats file.</summary>
	public required bool UpdateContentPatcherFormatVersions { get; init; }
}
