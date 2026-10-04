# Builds win-x64/e_sqlite3.dll + e_sqlite3.pdb from the official SQLite amalgamation.
#
# Why not the DLL from the SQLitePCLRaw.lib.e_sqlite3 NuGet package: it ships without a PDB, and mod
# hosts such as Hexium reject native binaries that don't have their PDB next to them. A PDB only
# matches the exact build that produced it, so we compile SQLite ourselves and commit both files.
# The plugin keeps P/Invoking "e_sqlite3", so nothing else changes. The Linux libe_sqlite3.so still
# comes from the NuGet package (see ValheimRadar.csproj).
#
# Requires PowerShell 7+ (for SHA3-256) and Visual Studio with the "Desktop development with C++"
# workload (MSVC x64 tools + Windows SDK). Run from anywhere:
#   pwsh ValheimRadar/Native/Build-ESqlite3.ps1
# then commit the updated win-x64/e_sqlite3.dll and win-x64/e_sqlite3.pdb.

$ErrorActionPreference = "Stop"

# Pinned SQLite release. Hash is the SHA3-256 published on https://sqlite.org/download.html.
$SqliteVersion = "3.53.4"
$SqliteUrl = "https://sqlite.org/2026/sqlite-amalgamation-3530400.zip"
$SqliteSha3 = "628a44cfe82c66aed1ccbbe85a562d2e33ebe64b3288981ed76285612227934e"

$outDir = Join-Path $PSScriptRoot "win-x64"
$workDir = Join-Path ([IO.Path]::GetTempPath()) "ValheimRadar-e_sqlite3-$SqliteVersion"

if (-not [System.Security.Cryptography.SHA3_256]::IsSupported) {
    throw "SHA3-256 is not available - run this script with PowerShell 7+ on Windows 10 1903 or later"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found - install Visual Studio" }
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    throw "No Visual Studio with the MSVC x64 tools found - add the 'Desktop development with C++' workload"
}
$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"

if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force }
New-Item -ItemType Directory -Path $workDir | Out-Null
$zipPath = Join-Path $workDir "sqlite.zip"
Invoke-WebRequest $SqliteUrl -OutFile $zipPath
$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA3_256]::HashData([IO.File]::ReadAllBytes($zipPath))).ToLowerInvariant()
if ($hash -ne $SqliteSha3) { throw "SHA3-256 mismatch for $SqliteUrl`: expected $SqliteSha3, got $hash" }
Expand-Archive $zipPath -DestinationPath $workDir
$srcDir = (Get-ChildItem $workDir -Directory -Filter "sqlite-amalgamation-*" | Select-Object -First 1).FullName

# Compile options follow SQLitePCLRaw's e_sqlite3 where they affect behavior (threadsafe, foreign keys
# on by default) so existing pin databases behave the same.
# /MT links the C runtime statically (no VC++ redistributable needed on players' machines).
# /PDBALTPATH:%_PDB% records just "e_sqlite3.pdb" in the DLL instead of this machine's absolute path,
# and /Brepro makes the output deterministic.
$defines = @(
    "SQLITE_API=__declspec(dllexport)",
    "SQLITE_THREADSAFE=1",
    "SQLITE_DEFAULT_FOREIGN_KEYS=1",
    "SQLITE_ENABLE_COLUMN_METADATA",
    "SQLITE_ENABLE_FTS4",
    "SQLITE_ENABLE_FTS5",
    "SQLITE_ENABLE_RTREE",
    "SQLITE_ENABLE_MATH_FUNCTIONS",
    "SQLITE_ENABLE_SNAPSHOT"
) | ForEach-Object { "/D`"$_`"" }

$cl = "cl /nologo /O2 /Zi /MT /GS /Brepro /W1 $($defines -join ' ') sqlite3.c /LD /Fe:e_sqlite3.dll " +
      "/link /DEBUG:FULL /PDB:e_sqlite3.pdb /PDBALTPATH:%_PDB% /OPT:REF /OPT:ICF /Brepro"
cmd /c "call `"$vcvars`" >nul && cd /d `"$srcDir`" && $cl"
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Copy-Item (Join-Path $srcDir "e_sqlite3.dll") $outDir -Force
Copy-Item (Join-Path $srcDir "e_sqlite3.pdb") $outDir -Force
Remove-Item $workDir -Recurse -Force

Write-Host "Built SQLite $SqliteVersion -> $outDir\e_sqlite3.dll + e_sqlite3.pdb"
