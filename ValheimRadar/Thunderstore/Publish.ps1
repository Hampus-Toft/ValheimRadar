# Publishes the current PluginVersion to Thunderstore and Hexium. Human use only - agents must never
# run this (see AGENTS.md "Publishing"); the typed confirmation below blocks non-interactive runs.
#
# Refuses to upload unless the checked-out commit is exactly origin/master with no local changes, so
# only merged versions can ever be released, and skips a site that already has this version.
#
# Tokens come from user environment variables, never from the repo:
#   THUNDERSTORE_TOKEN - thunderstore.io > Settings > Teams > HampusMods > Service Accounts
#   HEXIUM_TOKEN       - hexium.gg > settings icon next to your name > team page > API tokens
#
# Usage (from the repo root):
#   powershell -File ValheimRadar\Thunderstore\Publish.ps1 -DryRun    # every check + pack, no upload
#   powershell -File ValheimRadar\Thunderstore\Publish.ps1            # publish to both sites
#   powershell -File ValheimRadar\Thunderstore\Publish.ps1 -Site Hexium
param(
    [ValidateSet("All", "Thunderstore", "Hexium")][string]$Site = "All",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$Team = "HampusMods"
$PackageName = "ValheimRadar"
$Community = "valheim"

# Category slugs as each site's /api/experimental/community/valheim/category/ lists them. Hexium's
# "Client-only" tag is protected (not submittable) and is kept on the listing automatically.
$Sites = @(
    @{ Name = "Thunderstore"; Api = "https://thunderstore.io/api/experimental"; TokenVar = "THUNDERSTORE_TOKEN"
       Categories = @("mods", "client-side", "utility", "ai-generated", "deep-north-update")
       PageUrl = "https://thunderstore.io/c/valheim/p/$Team/$PackageName/" },
    @{ Name = "Hexium"; Api = "https://hexium.gg/api/experimental"; TokenVar = "HEXIUM_TOKEN"
       Categories = @("Quality of Life", "Valheim 1.0")
       PageUrl = "https://valheim.hexium.gg/mods/$Team/$PackageName" }
) | Where-Object { $Site -eq "All" -or $_.Name -eq $Site }

$repoRoot = (git rev-parse --show-toplevel).Trim()
$projectDir = Join-Path $repoRoot "ValheimRadar"

function Fail([string]$message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

# --- 1. Only merged code: on master, clean, identical to origin/master -------------------------
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "master") { Fail "On branch '$branch'. Check out master after the release PR is merged." }

if (git status --porcelain --untracked-files=no) { Fail "Uncommitted changes to tracked files. Commit, stash or discard them first." }

git fetch --quiet origin master
if ($LASTEXITCODE -ne 0) { Fail "git fetch origin master failed." }
$head = (git rev-parse HEAD).Trim()
$remote = (git rev-parse origin/master).Trim()
if ($head -ne $remote) { Fail "HEAD ($($head.Substring(0, 7))) is not origin/master ($($remote.Substring(0, 7))). Pull (or push) so they match." }

# --- 2. Version and release notes ----------------------------------------------------------------
$pluginCs = Get-Content (Join-Path $projectDir "RadarPlugin.cs") -Raw
$versionMatch = [regex]::Match($pluginCs, 'PluginVersion\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) { Fail "Could not read PluginVersion from RadarPlugin.cs." }
$version = $versionMatch.Groups[1].Value

$changelogPath = Join-Path $projectDir "Thunderstore\CHANGELOG.md"
if (-not (Test-Path $changelogPath) -or -not (Select-String -Path $changelogPath -Pattern "^## $([regex]::Escape($version))(\s|$)" -Quiet)) {
    Fail "CHANGELOG.md has no '## $version' entry."
}

# --- 3. Which sites still need this version ------------------------------------------------------
$targets = @()
foreach ($s in $Sites) {
    $published = $null
    try {
        $published = (Invoke-RestMethod "$($s.Api)/package/$Team/$PackageName/").latest.version_number
    } catch {
        Fail "$($s.Name): could not read the published version ($($_.Exception.Message))."
    }

    if ([version]$published -eq [version]$version) { Write-Host "$($s.Name): $version is already published - skipping."; continue }
    if ([version]$published -gt [version]$version) { Fail "$($s.Name) has $published, newer than PluginVersion $version." }

    if (-not $DryRun -and -not [Environment]::GetEnvironmentVariable($s.TokenVar)) {
        Fail "$($s.Name): environment variable $($s.TokenVar) is not set (restart the terminal after setting it)."
    }

    Write-Host "$($s.Name): $published -> $version"
    $targets += $s
}
if ($targets.Count -eq 0) { Write-Host "Nothing to publish."; exit 0 }

# --- 4. Build the package (Release: deploys the DLL locally, never relaunches Valheim) -----------
dotnet build (Join-Path $projectDir "ValheimRadar.csproj") -c Release -t:ThunderstorePack -p:ThunderstoreNamespace=$Team -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { Fail "ThunderstorePack build failed." }

$zipPath = Join-Path $projectDir "bin\Thunderstore\$Team-$PackageName-$version.zip"
if (-not (Test-Path $zipPath)) { Fail "Expected package not found: $zipPath" }
$zipBytes = [IO.File]::ReadAllBytes($zipPath)
Write-Host "Package: $zipPath ($([math]::Round($zipBytes.Length / 1KB)) KB)"

if ($DryRun) { Write-Host "Dry run - all checks passed, nothing uploaded." -ForegroundColor Green; exit 0 }

# --- 5. Explicit confirmation --------------------------------------------------------------------
$siteNames = ($targets | ForEach-Object { $_.Name }) -join " and "
$answer = Read-Host "Publish $PackageName $version to $siteNames? Type the version to confirm"
if ($answer -ne $version) { Fail "Confirmation did not match - nothing uploaded." }

# --- 6. Upload: initiate -> PUT parts to presigned URLs -> finish -> submit -----------------------
# Same flow on both sites (Hexium implements Thunderstore's submission API).
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromMinutes(5)

function Send-Json([string]$method, [string]$url, [string]$token, $body) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($method), $url)
    $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $token)
    if ($null -ne $body) {
        $json = ConvertTo-Json -InputObject $body -Depth 10 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw "$method $url -> $([int]$response.StatusCode): $text" }
    if ($text) { return $text | ConvertFrom-Json }
}

$failed = @()
foreach ($s in $targets) {
    $token = [Environment]::GetEnvironmentVariable($s.TokenVar)
    $uuid = $null
    try {
        Write-Host "$($s.Name): uploading..."
        $init = Send-Json "POST" "$($s.Api)/usermedia/initiate-upload/" $token @{ filename = [IO.Path]::GetFileName($zipPath); file_size_bytes = $zipBytes.Length }
        $uuid = $init.user_media.uuid

        $parts = @()
        foreach ($part in $init.upload_urls) {
            $content = New-Object System.Net.Http.ByteArrayContent($zipBytes, [int]$part.offset, [int]$part.length)
            $put = $http.PutAsync($part.url, $content).GetAwaiter().GetResult()
            if (-not $put.IsSuccessStatusCode) { throw "Uploading part $($part.part_number) failed: $([int]$put.StatusCode)" }
            $parts += @{ ETag = ($put.Headers.ETag.Tag); PartNumber = [int]$part.part_number }
        }

        Send-Json "POST" "$($s.Api)/usermedia/$uuid/finish-upload/" $token @{ parts = $parts } | Out-Null

        $submission = @{
            author_name          = $Team
            categories           = @()
            communities          = @($Community)
            community_categories = @{ $Community = $s.Categories }
            has_nsfw_content     = $false
            upload_uuid          = $uuid
        }
        Send-Json "POST" "$($s.Api)/submission/submit/" $token $submission | Out-Null
        Write-Host "$($s.Name): published $version - $($s.PageUrl)" -ForegroundColor Green
    } catch {
        Write-Host "$($s.Name): FAILED - $($_.Exception.Message)" -ForegroundColor Red
        if ($uuid) { try { Send-Json "POST" "$($s.Api)/usermedia/$uuid/abort-upload/" $token $null | Out-Null } catch { } }
        $failed += $s.Name
    }
}

if ($failed.Count -gt 0) { Fail "Publishing failed for: $($failed -join ', '). Re-run with -Site to retry just those." }
