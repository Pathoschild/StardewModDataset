#
# This script creates the monthly backup of the dataset downloads, which is shared with tool authors and on Patreon.
#
# Usage:
#   zip-monthly-dataset.ps1 -archivePath 'path/to/archive.zip'
#

# configure
param(
    [Parameter(Mandatory)] [String] $archivePath
)
$datasetPath = Join-Path $PSScriptRoot '../dataset'
$readme = @'
This folder has every Stardew Valley mod from CurseForge, ModDrop, and Nexus.

This is part of the Stardew mod dataset: https://github.com/Pathoschild/StardewModDataset.
See its README for more info.

This folder is meant to run analysis and search scripts, it's **not** meant to archive mods. Mods
which are hidden or deleted from the mod site are deleted automatically from the dataset.
'@
$ErrorActionPreference = 'Stop'

# skip if archive exists
$archivePath = [System.IO.Path]::GetFullPath($archivePath, (Get-Location).Path)
if (Test-Path $archivePath) {
    Write-Error "An archive already exists at $archivePath. To create a new one, delete that file first."
    exit 1
}

# create zip
# -bb: log processed files
# -bt: show execution time stats
# -mx7: compression level (7 = 'maximum')
# a: add files to archive
# -xr exclude files matching pattern
Write-Host 'Zipping dataset...'
Write-Host '------------------'
$readme | & 'C:\Program Files\7-Zip\7z.exe' -bb -bt -mx7 a $archivePath '-siREADME.md'
if ($LASTEXITCODE -ne 0) {
    throw "7-Zip failed with exit code $LASTEXITCODE."
}
& 'C:\Program Files\7-Zip\7z.exe' -bb -bt -mx7 '-xr!*.psd' '-xr!*.zip' '-xr!*.rar' '-xr!*.7z' a $archivePath `
    (Join-Path $datasetPath data) `
    (Join-Path $datasetPath downloads) `
    (Join-Path $datasetPath indexes) `
    (Join-Path $datasetPath reference-data) `
    (Join-Path $datasetPath stats)
if ($LASTEXITCODE -ne 0) {
    throw "7-Zip failed with exit code $LASTEXITCODE."
}
