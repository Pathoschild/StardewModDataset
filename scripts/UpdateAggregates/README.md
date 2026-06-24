The **UpdateAggregates** app updates the files in `dataset/indexes` and `dataset/stats`.

## Contents
* [What this app does](#what-this-app-does)
* [Usage](#usage)
  * [First-time setup](#first-time-setup)
  * [Run](#run)
* [See also](#see-also)

## What this app does
When run, the app will automatically:

1. Analyze the mod data files in `<repo root>/dataset/data` and `<repo root>/dataset/downloads`.
2. Update the `<repo root>/dataset/indexes` and `<repo root>/dataset/stats` files based on those data files.

## Usage
### First-time setup
1. If your game isn't in the default Steam location, open `Directory.Build.props` and edit `SmapiPath`.
2. _(Optional)_ Add the `<repo root>/dataset` folder to your antivirus exclusions if the tool seems slow.

### Run
1. Edit `appsettings.jsonc` as needed (e.g. to set which index and stats files are updated).
2. Launch the project to update the index and stats files.

## See also
* [Main README](../../docs/README.md)
