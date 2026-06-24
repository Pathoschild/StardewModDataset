namespace Pathoschild.ModData.UpdateAggregates.Framework;

/// <summary>The data for an aggregate.</summary>
/// <param name="Type">The aggregate type.</param>
/// <param name="Name">The name of the aggregate, used in the file name.</param>
/// <param name="Data">The data to write to the file.</param>
internal record AggregateData(AggregateType Type, string Name, object Data);
