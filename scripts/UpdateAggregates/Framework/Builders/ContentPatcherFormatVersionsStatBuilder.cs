using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Pathoschild.ModData.Common;
using StardewModdingAPI;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Framework.ModDataset;
using StardewModdingAPI.Toolkit.Framework.ModScanning;
using StardewModdingAPI.Toolkit.Serialization;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Pathoschild.ModData.UpdateAggregates.Framework.Builders;

/// <summary>Builds stats for the number of Content Patcher content packs by their 'Format' version.</summary>
internal class ContentPatcherFormatVersionsStatBuilder : IAggregateBuilder
{
	/*********
	** Fields
	*********/
	/// <summary>The minimum content packs required for a format version to be tracked separately (e.g. to avoid tracking typos).</summary>
	private const int MinContentPacks = 5;

	/// <summary>The aggregate name.</summary>
	private const string Name = "Content Patcher packs by format version";

	/// <summary>The valid format versions to keep regardless of <see cref="MinContentPacks"/>.</summary>
	private readonly HashSet<string> MinContentPackExceptions = ["1.2"];

	/// <summary>The options to apply when reading a <c>content.json</c> file using System.Text.Json.</summary>
	private readonly JsonSerializerOptions JsonReadOptions = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = true };

	/// <summary>The SMAPI toolkit JSON helper to use for files which can't be parsed as standard JSON.</summary>
	private readonly JsonHelper JsonHelper = new();

	/// <summary>The highest format version detected for each mod ID.</summary>
	private readonly Dictionary<string, FormatVersion> FormatVersionsByModId = new(StringComparer.OrdinalIgnoreCase);


	/*********
	** Public methods
	*********/
	/// <inheritdoc />
	public void Collect(ModPageRecord modPage)
	{
		string? modDownloadsPath = null;

		foreach (ModPageDownloadRecord download in modPage.Downloads)
		{
			foreach (ModFolderRecord mod in download.Mods)
			{
				// skip if not a Content Patcher pack
				if (mod.Type is not ModType.ContentPack || mod.Id is null || !string.Equals(mod.Manifest?.ContentPackFor?.UniqueId?.Trim(), "Pathoschild.ContentPatcher", StringComparison.OrdinalIgnoreCase))
					continue;

				// get path to content.json
				modDownloadsPath ??= FileHelper.GetDownloadDirPath(modPage.Site, modPage.Id);
				string contentJsonPath = Path.Combine(modDownloadsPath, download.Id.ToString(), mod.RelativePath ?? string.Empty, "content.json");
				if (!File.Exists(contentJsonPath))
					continue;

				// get format version
				FormatVersion? formatVersion = this.TryReadFormatVersion(contentJsonPath);
				if (formatVersion is null)
					continue;

				// track the highest version
				if (!this.FormatVersionsByModId.TryGetValue(mod.Id, out FormatVersion? trackedVersion) || formatVersion.IsNewerThan(trackedVersion))
					this.FormatVersionsByModId[mod.Id] = formatVersion;
			}
		}
	}

	/// <inheritdoc />
	public AggregateData GetAggregate()
	{
		// get counts
		Dictionary<string, int> counts = this.FormatVersionsByModId
			.OrderBy(p => p.Value.Major)
			.ThenBy(p => p.Value.Minor)
			.GroupBy(p => $"{p.Value.Major}.{p.Value.Minor}")
			.ToDictionary(
				p => p.Key,
				p => p.Count()
			);

		// ignore counts below minimum
		foreach ((string version, int count) in counts)
		{
			if (count < MinContentPacks && !MinContentPackExceptions.Contains(version))
				counts.Remove(version);
		}

		// build aggregate
		JsonObject line = new JsonObject
		{
			["date"] = DateTime.Now.ToString("yyyy-MM-dd")
		};
		foreach ((string type, int count) in counts)
			line.Add(type, count);

		return new AggregateData(AggregateType.Stats, Name, line);
	}


	/*********
	** Private methods
	*********/
	/// <summary>Try to read the format version from a content pack's <c>content.json</c> file.</summary>
	/// <param name="contentJsonPath">The path to the <c>content.json</c> file.</param>
	private FormatVersion? TryReadFormatVersion(string contentJsonPath)
	{
		string? rawVersion = this.TryReadRawFormatVersion(contentJsonPath);
		if (rawVersion is null || !SemanticVersion.TryParse(rawVersion, allowNonStandard: true, out ISemanticVersion? version))
			return null;

		// get simplified version (Content Patcher ignores anything after x.y)
		return new FormatVersion(version.MajorVersion, version.MinorVersion);
	}

	/// <summary>Try to read a content pack's <c>content.json</c> file.</summary>
	/// <param name="contentJsonPath">The path to the <c>content.json</c> file.</param>
	private string? TryReadRawFormatVersion(string contentJsonPath)
	{
		try
		{
			// use System.Text.Json for most files
			try
			{
				using FileStream readStream = File.OpenRead(contentJsonPath);
				ContentDataModel? data = JsonSerializer.Deserialize<ContentDataModel>(readStream, this.JsonReadOptions);
				if (data?.Format != null) // STJ is case-sensitive, so fallback to Newtonsoft if the field is null
					return data.Format;
			}
			catch (JsonException) { /* fallback */ }

			// fallback to slower Newtonsoft.Json read
			string json = File.ReadAllText(contentJsonPath);
			try
			{
				ContentDataModel? data = JsonConvert.DeserializeObject<ContentDataModel>(json);
				return data?.Format;
			}
			catch (Newtonsoft.Json.JsonException ex)
			{
				// last ditch attempt: apply SMAPI's special-case handling (e.g. replacing curly quotes)
				try
				{
					ContentDataModel? data = this.JsonHelper.Deserialize<ContentDataModel>(json);
					if (data?.Format is not null)
						return data.Format;
				}
				catch { /* fallback */ }

				ConsoleHelper.WriteWarningLine($"    [{Name}] Skipped {Path.GetRelativePath(FileHelper.DownloadsPath, contentJsonPath)}: file has invalid JSON: {ex.Message}");
				return null;
			}
		}
		catch (Exception ex)
		{
			ConsoleHelper.WriteWarningLine($"    [{Name}] Skipped {Path.GetRelativePath(FileHelper.DownloadsPath, contentJsonPath)}: reading the file failed with an unexpected error.\n{ex}");
			return null;
		}
	}

	/// <summary>The minimal data model for a content pack's <c>content.json</c> file.</summary>
	private class ContentDataModel
	{
		/// <summary>The format version.</summary>
		public string? Format { get; init; }
	}

	/// <summary>A Content Patcher format version.</summary>
	/// <param name="Major">The major version.</param>
	/// <param name="Minor">The minor version.</param>
	private record FormatVersion(int Major, int Minor)
	{
		/// <summary>Get whether this format version is higher than a specified one.</summary>
		/// <param name="other">The other format version to compare against.</param>
		public bool IsNewerThan(FormatVersion other)
		{
			return
				this.Major > other.Major
				|| (
					this.Major == other.Major
					&& this.Minor > other.Minor
				);
		}
	}
}
