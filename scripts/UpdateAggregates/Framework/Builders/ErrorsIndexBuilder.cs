using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Toolkit.Framework.ModDataset;

namespace Pathoschild.ModData.UpdateAggregates.Framework.Builders;

/// <summary>Builds an index lookup of errors by site and page ID.</summary>
internal class ErrorsIndexBuilder : IAggregateBuilder
{
	/*********
	** Fields
	*********/
	/// <summary>The name of the index, used in the file name.</summary>
	private readonly string Name;

	/// <summary>Get the error to save for a mod download, or <c>null</c> if it has none.</summary>
	private readonly Func<ModPageDownloadRecord, string?> GetError;

	/// <summary>The errors indexed by mod site and page ID.</summary>
	private readonly Dictionary<ModSite, Dictionary<long, string>> Data = [];

	/// <summary>A pooled set of errors for one mod page.</summary>
	private readonly HashSet<string> PooledErrors = new(StringComparer.OrdinalIgnoreCase);


	/*********
	** Public methods
	*********/
	/// <summary>Construct an instance.</summary>
	/// <param name="name"><inheritdoc cref="Name" path="/summary"/></param>
	/// <param name="getError"><inheritdoc cref="GetError" path="/summary"/></param>
	public ErrorsIndexBuilder(string name, Func<ModPageDownloadRecord, string?> getError)
	{
		this.Name = name;
		this.GetError = getError;
	}

	/// <inheritdoc />
	public void Collect(ModPageRecord modPage)
	{
		// collect errors
		this.PooledErrors.Clear();
		foreach (ModPageDownloadRecord download in modPage.Downloads)
		{
			string? error = this.GetError(download);
			if (error is not null)
				this.PooledErrors.Add(error);
		}

		// save to index
		if (this.PooledErrors.Count > 0)
		{
			if (!this.Data.TryGetValue(modPage.Site, out var data))
				this.Data[modPage.Site] = data = [];

			data[modPage.Id] = string.Join(Environment.NewLine, this.PooledErrors.Order(StringComparer.OrdinalIgnoreCase));
		}
	}

	/// <inheritdoc />
	public AggregateData GetAggregate()
	{
		return new AggregateData(AggregateType.Index, this.Name, this.Data);
	}
}
