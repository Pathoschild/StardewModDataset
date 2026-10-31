# configure
$modDumpPath = $PSScriptRoot
$datasetPath = 'E:\source\_Stardew\_ModData\dataset'
$archivePath = Join-Path $modDumpPath 'stardew-mod-dataset.zip'

# skip if archive exists
if (Test-Path $archivePath) {
    Write-Error "An archive already exists at $archivePath. To create a new one, delete that file first."
    exit 1
}

# zip dataset folders
# -bb: log processed files
# -bt: show execution time stats
# -mx7: compression level (7 = 'maximum')
# a: add files to archive
# -xr exclude files matching pattern
Write-Host 'Zipping dataset...'
Write-Host '------------------'
Push-Location $datasetPath
& 'C:\Program Files\7-Zip\7z.exe' -bb -bt -mx7 a $archivePath data indexes reference-data stats
Pop-Location

# add downloads + README
Write-Host 'Zipping mod dump...'
Write-Host '-------------------'
Push-Location $modDumpPath
& 'C:\Program Files\7-Zip\7z.exe' -bb -bt -mx7 '-xr!*.psd' '-xr!*.zip' '-xr!*.rar' '-xr!*.7z' a $archivePath downloads README.md
Pop-Location
