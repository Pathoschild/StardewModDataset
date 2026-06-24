The **Stardew mod dataset** has open data about every [Stardew Valley][] mod uploaded to CurseForge, ModDrop, and Nexus
Mods. That data is used to build other tools like the [mod stats][mod stats] and [dependency graph][mod dependency
graph].

## Contents
* [Overview](#overview)
* [Using this data](#using-this-data)
  * [In your code](#in-your-code)
  * [Licensing](#licensing)
  * [Versioning](#versioning)
  * [Data updates](#data-updates)
* [Dataset](#dataset)
  * [Data](#data)
  * [Downloads](#downloads-1)
  * [Indexes](#indexes)
  * [Stats](#stats)
  * [Reference data](#reference-data)
* [Maintenance](#maintenance)
* [See also](#see-also)

## Overview
The dataset is regularly updated with [structured data files](#dataset) about:
- mod pages;
- their download files;
- the mods detected within each download (using the same code which SMAPI uses to load them);
- and related reference data (e.g. SMAPI web traffic and costs).

The dataset has [an open license](#licensing) and [versioning policy](#versioning), so anyone can use it to create their
own tools and analyses.

Some example tools using this data include:
- the [mod stats dashboard][mod stats];
- the [mod compatibility list][];
- and the [mod dependency graph][].

## Using this data
### In your code
* If you're using C# and install [SMAPI][], the `smapi-internal/SMAPI.Toolkit.dll` assembly in your game folder includes
  the relevant data models. That lets you read a metadata file directly using `System.Text.Json`.

  For example:
  ```c#
  string dataPath = @"C:\source\StardewModData\dataset\data";
  foreach (string filePath in Directory.GetFiles(dataPath, "*.json", SearchOption.AllDirectories))
  {
      using var fileStream = File.OpenRead(filePath);
      var modPage = JsonSerializer.Deserialize<ModPageRecord>(fileStream);
  }
  ```

  You can also access any of the fields directly. For example, with `modPage` in the code above:
  ```c#
  foreach (var download in modPage.Downloads)
  {
      foreach (var mod in download.Mods)
      {
          if (mod.Manifest?.ContentPackFor?.UniqueId == "Pathoschild.ContentPatcher")
              ...;
      }
  }
  ```
* For other tech stacks, the [included JSON schema](../dataset/data/json-schema.json) provides a machine-readable
  summary of the JSON structure. Most ecosystems have packages which can auto-generate data models from a JSON schema.

  For example:

  language   | package
  ---------- | -------
  C#         | [NJsonSchema](https://www.nuget.org/packages/NJsonSchema)
  Python     | [datamodel-code-generator](https://pypi.org/project/datamodel-code-generator)
  Rust       | [typify](https://github.com/oxidecomputer/typify)
  TypeScript | [NJsonSchema](https://github.com/RicoSuter/NJsonSchema)

See [_dataset_](#dataset) for an explanation of the data in this repository.

### Licensing
This repository is dual-licensed under the [MIT license](../LICENSE) and [CC-BY-SA 4.0 license][]. You can use the data
under either license. The data is publicly available with explicit permission from CurseForge, ModDrop, and Nexus Mods.

> [!IMPORTANT]  
> The `dataset/data` folder includes content authored by third parties (e.g. mod page descriptions). Where applicable,
> that content remains the property of its respective copyright owners. This license doesn't apply to any such
> copyrightable content from third parties.

### Versioning
> [!IMPORTANT]  
> This section describes the versioning once 1.0.0 is released. The dataset may change without warning while it's in 0.x
> alpha.

The dataset's _structure and format_ are versioned and tracked in the [changelog](CHANGELOG.md). Breaking changes to
that structure or format will increment the major version (e.g. `1.3.0` → `2.0.0`).

For automated tools which parse the dataset, you can use the Git tags to avoid breaking changes. For example, reference
the `v1` tag to get the latest data that was published in the 1.x.x format. Note that when a new major version is
published, all later data updates are only published for the newer version. The Git tags are only intended to avoid
breaking tools until they're updated.

The dataset's [_data_ is updated regularly](#data-updates), but data updates don't change the version number.

### Data updates
Near the end of each month, the full dataset is updated in one new Git commit. You can see the previous months'
snapshots in the Git history if you want to run historical analyses.

The dataset might also be updated more often, but those intermediate updates are later merged into the end-of-month
snapshot to minimize the repo size and make month-by-month analysis easier.

## Dataset
The `dataset` folder has the actual dataset. This is comprised of four subfolders:

folder              | contains
------------------- | ---------
`dataset/data`      | The data for mod pages, their downloads, and the detected mods within those downloads.
`dataset/downloads` | The raw downloaded mod files (not stored in Git).
`dataset/indexes`   | JSON files which summarize the current data (e.g. to quickly find mod pages for a unique mod ID).
`dataset/stats`     | [JSONL][] files with statistical data over time (e.g. breakdown of mods by type for each month).

### Data
The `dataset/data` folder has the data for mod pages, their downloads, and the detected mods within those downloads.

The files for each site are grouped into 'bucket' folders by mod ID. For example, `dataset/data/Nexus/2` has mod IDs
2000–2999. The range is 10,000 per bucket for CurseForge and ModDrop (since their mod IDs are global), and 1,000 per
bucket for Nexus Mods.

For example:
```
Nexus/            <-- mod site
   2/             <-- bucket (IDs 2000–2999)
      2400.json   <-- mod data
```

Each file has metadata in JSON format (see also [_use in your code_](#use-in-your-code)). The fields are listed below.

#### Mod page
The top-level fields describe the _mod page_ (not the mod):

field         | type    | purpose
------------- | ------- | -------
`Site`        | string  | The mod site which has the mod. One of `CurseForge`, `ModDrop`, or `Nexus`.
`Id`          | integer | The mod page ID within the mod site.
`Name`        | string  | The mod name based on the mod page.
`Author`      | string  | The author's canonical name based on the mod page (usually the username). This is identical for all mods uploaded to the same site by a user.
`AuthorLabel` | string  | The author's display name based on the mod page, if different from `Author`. This may be different across mods by the same author. It may include free text entered by the uploader on Nexus, or include multiple names on CurseForge.
`TagLine`     | string  | A short blurb which describes the mod page, if available.
`Description` | string  | The full mod page description body, if available. The format depends on the mod site (e.g. ModDrop uses Markdown and Nexus uses BBCode).
`PageUrl`     | string  | The full URL to the mod's web page.
`Version`     | string  | The version number based on the mod page, if available. This is the version as shown on the mod page, which may not be a valid semantic version.
`Updated`     | string  | When the mod metadata last changed, as an ISO 8601 UTC date/time. To the extent supported by each mod site, this covers any change to the mod page (including mod page edits, file uploads, hiding or republishing, etc).
`Downloads`   | array   | See [_downloads_](#downloads).
`OtherFields` | dictionary | The remaining fields which weren't mapped to one of the other fields.

#### Downloads
A [mod page](#mod-page)'s `Downloads` field describes the 'active' downloads available on that mod page. This omits hidden, deleted,
or obsolete downloads where possible.

The selected files depend on the mod site:
- CurseForge: all downloads (since there's no concept of outdated files).
- ModDrop: downloads not marked 'old', 'hidden', or 'deleted'.
- Nexus: downloads not marked 'old', 'archived', or 'deleted'.

Each download has these fields:

field           | type   | purpose
--------------- | ------ | -------
`Id`            | number | The unique ID for this file within the mod site.
`Type`          | enum   | The download type. One of `Main` or `Optional`. The meaning depends on the mod site:<ul><li>CurseForge: 'release' downloads are `Main`; 'alpha' or 'beta' downloads are `Optional`.</li><li>ModDrop: Non-prerelease and non-alternate downloads are `Main`; prerelease or alternate downloads are `Optional`.</li><li>Nexus: files in the 'main' category are `Main` and those in the 'optional' category are `Optional`. Special case: if the mod page only has files in non-main and non-optional categories, any remaining files which aren't in the archived/old/deleted categories are `Optional`.</li></ul>
`DisplayName`   | string | The download's title as entered by the uploader.
`FileName`      | string | The name of the file when downloaded (usually a `.zip` file).
`Description`   | string | The description text for the file. This may be BBCode on Nexus.
`Version`       | string | The version number based on the download info, if any. This is the version as shown on the mod page, which may not be a valid semantic version.
`SizeInBytes`   | number | The size of the downloaded file in bytes.
`Uploaded`      | string | When the file was uploaded, as an ISO 8601 UTC date/time.
`Mods`          | array  | See [_mods_](#mods).
`DownloadError` | string | An error message indicating why the file could not be downloaded, if applicable. This is usually an issue on the mod site (e.g. they quarantined the file).
`UnpackError`   | string | An error message indicating why the mods could not be unpacked from the downloaded archive, if applicable. This is usually an issue with the file itself (e.g. the author uploaded a corrupted archive).
`OtherFields`   | dictionary | The remaining fields which weren't mapped to one of the other fields.

#### Mods
A [download](#downloads)'s `Mods` field describes the mods detected within the download, based on analysis using the SMAPI toolkit
(the same code which SMAPI itself uses to load the mod). This is always non-empty, unless `DownloadError` or
`UnpackError` are set.

field            | type   | purpose
---------------- | ------ | -------
`Id`             | string | The normalized mod ID from the mod's `manifest.json`, if found.
`DisplayName`    | string | The display name for the mod. This is usually the normalized name from the `manifest.json` (if it was parseable), else generated based on the folder path in the download.
`Type`           | enum   | The detected mod type. One of: <ul><li>`ContentPack`: The mod contains files loaded as a content pack by another SMAPI mod.</li><li>`Smapi`: The mod uses SMAPI directly (e.g. a C# mod).</li><li>`Xnb`: The mod is a legacy type which replaces game files directly.</li><li>`Ignored`: The folder is ignored by convention (e.g. because it's prefixed with a dot).</li><li>`Invalid`: The mod is invalid and its type could not be determined.</li></ul>
`RelativePath`   | string | The relative path to this mod within the download.
`Manifest`       | object | See [_mod manifest_](#mod-manifest).
`ManifestParseError` | enum | If the mod's `manifest.json` couldn't be parsed, the error type. One of: <ul><li>`EmptyFolder`: The folder is empty or contains only ignored files.</li><li>`EmptyVortexFolder`: The folder is an empty folder managed by the Vortex mod manager.</li><li>`IgnoredFolder`: The folder is ignored by convention.</li><li>`ManifestInvalid`: The mod's `manifest.json` could not be parsed.</li><li>`ManifestMissing`: The folder contains non-ignored and non-XNB files, but none of them are `manifest.json`.</li><li>`XnbMod`: The folder is an XNB mod.</li></ul>
`ManifestParseErrorText` | string | If the mod's `manifest.json` couldn't be parsed, a human-readable message indicating why (e.g. the message shown in the SMAPI console window).

#### Mod manifest
A [mod](#mods)'s `Manifest` field has the mod's parsed [`manifest.json` file][manifest], if the file is valid. This is a
normalized JSON structure, not the raw text (e.g. any JSON comments in the file aren't included).

field            | type   | purpose
---------------- | ------ | -------
`Name`           | string | The mod name.
`Description`    | string | A brief description of the mod.
`Author`         | string | The mod author's name.
`Version`        | string | The mod version. This is guaranteed to be a semantic version (since an invalid version results in a manifest parse error).
`MinimumApiVersion`  | string | The minimum SMAPI version required by this mod, if specified. This is guaranteed to be a semantic version.
`MinimumGameVersion` | string | The minimum Stardew Valley version required by this mod, if specified. This is guaranteed to be a semantic version.
`EntryDll`       | string | The name of the DLL to load in the mod directory, if it's a SMAPI (C#) mod.
`ContentPackFor` | object | The mod which will read this as a content pack, if it's a content pack.
`Dependencies`   | array  | The other mods that must be loaded before this mod.
`UpdateKeys`     | array  | The namespaced mod IDs to query for updates (like `"Nexus:541"`).
`UniqueId`       | string | The unique mod ID.
`OtherFields`    | dictionary | The remaining fields which weren't mapped to one of the other fields.

The manifest's `ContentPackFor` field is an object with these fields:

field            | type   | purpose
---------------- | ------ | -------
`UniqueId`       | string | The unique ID of the mod which can read this content pack.
`MinimumVersion` | string | The minimum required version (if any).

The `Dependencies` field is an array of objects with these fields:

field            | type   | purpose
---------------- | ------ | -------
`UniqueId`       | string | The unique mod ID to require.
`MinimumVersion` | string | The minimum required version (if any).
`IsRequired`     | bool   | Whether the dependency must be installed to use the mod.

### Downloads
The local `dataset/downloads` folder has the raw downloaded mod files. The downloads have the [same file structure as
`data`](#data), but with a subfolder per download instead of metadata files.

These aren't versioned or stored in the Git repo. Instead, they're part of a separate 'mod dump' export stored in Azure
Blob Storage. You can [download the latest mod dump][mod dump], but note that it's fairly large (over 100GB zipped) and
updated monthly.

For example:
```
Nexus/            <-- mod site
   2/             <-- bucket (IDs 2000–2999)
      2400/       <-- mod ID
         160380/  <-- download ID
            ...   <-- mod files inside download
```

### Indexes
The `dataset/indexes` folder has JSON files which summarize the _current_ mod [data](#data) and [downloads](#downloads-1).

The current indexes are:

file name                 | contains
------------------------- | --------
`errors on download.json` | For each site and mod page ID, the distinct download errors encountered analyzing its data.
`errors on unpack.json`   | For each site and mod page ID, the distinct unpack errors encountered analyzing its data.
`pages by mod ID.json`    | For each mod manifest ID, the mod pages whose downloads contain a mod with that ID.

### Stats
The `dataset/stats` folder has [JSONL][] files with statistical data over time (e.g. a monthly breakdown of mods by
type).

The current stats are:

file name                                       | contains
----------------------------------------------- | ---------
`Content Patcher packs by format version.jsonl` | The number of [Content Patcher][] content packs by their `Format` version, excluding invalid values.
`mods by type.jsonl`                            | The number of mods by type: SMAPI (C#), [XNB][XNB mods], or by content pack framework (restricted to frameworks with 100+ content packs).

### Reference data
The `dataset/reference-data` folder has manually-maintained data which isn't strictly part of the mod dataset, but is either
indirectly related (e.g. SMAPI hosting costs) or widely useful when analyzing the dataset.

The current reference data files are:

file name                | contains
------------------------ | --------
`SMAPI costs.jsonl`      | The monthly costs related to hosting or maintaining SMAPI and its services (e.g. web hosting and domains for the update-check servers).
`SMAPI DNS queries.json` | The number of DNS queries received by the SMAPI server domains each month, which serves as a rough indicator of player and mod author activity (since SMAPI doesn't collect telemetry).

## Maintenance
The `scripts` folder has the .NET solution used to update the dataset. It has two projects:

- [`FetchMods`](../scripts/FetchMods/README.md) fetches and analyzes the latest mods from each site, and saves them to
  `dataset/data` and `dataset/downloads`.
- [`UpdateAggregates`](../scripts/UpdateAggregates/README.md) updates the `dataset/indexes` and `dataset/stats` files
  based on the fetched data & downloads.

If you already completed the first-time setup for each project, you can run them in sequence:
```
cd scripts
dotnet run --project FetchMods
dotnet run --project UpdateAggregates
```

## See also
* [Mod dump][] (downloaded mod files)

[mod compatibility list]: https://smapi.io/mods
[mod dependency graph]: https://stardew.button.gay/stats/dependencies
[mod dump]: https://smapistorage.blob.core.windows.net/exports/index.html
[mod stats]: https://smapi.io/stats

[Content Patcher]: https://www.nexusmods.com/stardewvalley/mods/1915
[SMAPI]: https://github.com/Pathoschild/SMAPI
[Stardew Valley]: https://www.stardewvalley.net
[manifest]: https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Manifest
[XNB mods]: https://stardewvalleywiki.com/Modding:Using_XNB_mods

[CC-BY-SA 4.0 license]: https://creativecommons.org/licenses/by-sa/4.0/deed.en
[JSONL]: https://jsonlines.org
