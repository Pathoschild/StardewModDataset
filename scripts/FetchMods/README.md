The **FetchMods** app updates the `dataset/data` and `dataset/downloads` files in the repository.

## Contents
* [What this app does](#what-this-app-does)
* [Usage](#usage)
  * [First-time setup](#first-time-setup)
  * [Run](#run)
* [See also](#see-also)

## What this app does
When run, the app will automatically:

1. Fetch all mod metadata from the mod sites' export APIs.
2. For each fetched mod:
   1. Compare it to the stored metadata in `<repo root>/dataset/data`, and skip the mod if they match.
   2. Download and unpack all active downloads from the mod page into `<repo root>/dataset/downloads`.
   3. Analyze mods in the downloads using the SMAPI toolkit.
   4. Add or update the mod's file in `<repo root>/dataset/data`.
3. Delete all stored mods which are no longer on the mod site.

## Usage
> [!IMPORTANT]  
> **This app needs private endpoints and special permissions from all three mod sites.**  
> There's no way to fetch the mod data without contacting CurseForge, ModDrop, and Nexus to arrange access.

### First-time setup
1. If your game isn't in the default Steam location, open `Directory.Build.props` and edit `SmapiPath`.
2. Create your local settings:
   1. Copy & paste `appsettings.jsonc` to `appsettings.local.jsonc`.
   2. Delete all non-null fields (they'll be inherited from `appsettings.jsonc`).
   3. Fill in all `null` fields. (All fields are required.)
3. _(Optional)_ By default, the app will store downloaded mod files in a `<repo root>/dataset/downloads` folder, which
   is ignored via `.gitignore`. This folder may get fairly large (over 130GB).

   You can optionally symlink it to an external folder. For example, in PowerShell:
   ```ps
   $type = if ($env:OS -eq 'Windows_NT') { 'Junction' } else { 'SymbolicLink' }
   New-Item -ItemType $type -Path dataset/downloads -Target E:\dev\mod-dump
   ```
4. _(Optional)_ Add the `<repo root>/dataset` folder to your antivirus exclusions if the tool seems slow.

### Run
1. Edit `appsettings.jsonc` as needed (e.g. to set whether deleted mods are removed).
2. Launch the project to update the metadata files and downloads.

## See also
* [Main README](../../docs/README.md)
