param(
    [Parameter(Mandatory = $true)][string]$ProjectDir,
    [Parameter(Mandatory = $true)][string]$TargetPath,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [Parameter(Mandatory = $true)][string]$Namespace
)

$ErrorActionPreference = "Stop"

# PluginVersion in RadarPlugin.cs is the single source of truth for the mod's version - read it
# here instead of duplicating the value in manifest.json, so the Thunderstore listing can never
# drift out of sync with the version BepInEx actually loads.
$pluginCsPath = Join-Path $ProjectDir "RadarPlugin.cs"
$pluginCs = Get-Content $pluginCsPath -Raw
$versionMatch = [regex]::Match($pluginCs, 'PluginVersion\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) {
    throw "Could not find PluginVersion in $pluginCsPath"
}
$version = $versionMatch.Groups[1].Value

$thunderstoreDir = Join-Path $ProjectDir "Thunderstore"
$templatePath = Join-Path $thunderstoreDir "manifest.template.json"
$manifestJson = (Get-Content $templatePath -Raw).Replace("__VERSION__", $version)
$manifestObj = $manifestJson | ConvertFrom-Json

$stagingDir = Join-Path $OutDir "staging"
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir | Out-Null

Set-Content -Path (Join-Path $stagingDir "manifest.json") -Value $manifestJson -NoNewline -Encoding utf8
Copy-Item (Join-Path $thunderstoreDir "icon.png") $stagingDir
Copy-Item (Join-Path $thunderstoreDir "README.md") $stagingDir
Copy-Item $TargetPath $stagingDir

# Native SQLite for the pin database, copied next to the DLL by the CopySqliteNative target.
# e_sqlite3.pdb must ship next to e_sqlite3.dll: mod hosts (Hexium) reject native DLLs without it.
$targetDir = Split-Path $TargetPath -Parent
foreach ($native in @("e_sqlite3.dll", "e_sqlite3.pdb", "libe_sqlite3.so")) {
    $nativePath = Join-Path $targetDir $native
    if (-not (Test-Path $nativePath)) {
        throw "Missing $nativePath - build the project first so CopySqliteNative runs"
    }
    Copy-Item $nativePath $stagingDir
}

$zipName = "$Namespace-$($manifestObj.name)-$version.zip"
$zipPath = Join-Path $OutDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath

Write-Host "Thunderstore package written to $zipPath (version $version)"
