# Uploads the game to Steam (SteamPipe) so testers get updates through their Steam Library.
#
#   .\tools\steam\steam-upload.ps1 -User <steamworks account>            rebuild, self-test, upload
#   .\tools\steam\steam-upload.ps1 -User <account> -Branch playtest      ...and set it live on the "playtest" beta branch
#   .\tools\steam\steam-upload.ps1 -SkipBuild -User <account>            upload the current Build\ as it is
#   .\tools\steam\steam-upload.ps1 -NoUpload                             only write the build scripts and show what would run
#
# The first run asks for your Steam password and Steam Guard code in steamcmd itself (this script never
# sees or stores them); steamcmd remembers the login after that.
# The default branch can't be set live from here: Valve requires that on the Steamworks site
# (SteamPipe > Builds). Beta branches (-Branch) must already exist there.
param(
    [string]$User = $env:STEAM_BUILD_USER,
    [string]$Branch,
    [string]$Desc,
    [switch]$SkipBuild,
    [switch]$NoUpload,
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent

# ---- settings: check the depot ID in Steamworks > SteamPipe > Depots ----
$AppId = 3965800
$DepotId = 3965801
# --------------------------------------------------------------------------

if ($Branch -eq 'default') { throw "The default branch can only be set live on the Steamworks site. Leave -Branch off and do it there." }

$version = (Select-String -Path (Join-Path $root 'ProjectSettings\ProjectSettings.asset') -Pattern '^\s*bundleVersion:\s*(\S+)').Matches[0].Groups[1].Value
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
$dirty = (& git -C $root status --porcelain --untracked-files=no 2>$null)
if (-not $Desc) { $Desc = "v$version ($commit$(if ($dirty) { ', uncommitted changes' }))" }

# 1. Build and self-test (same as a release).
if (-not $SkipBuild -and -not $NoUpload) {
    Write-Host "Rebuilding and running the self-test..." -ForegroundColor Cyan
    & (Join-Path $root 'play.ps1') -AutoTest -Rebuild | Out-Null
    $log = Join-Path $root 'AutoTest\player.log'
    $summary = if (Test-Path $log) { Select-String -Path $log -Pattern 'AUTOTEST SUMMARY passed=(\d+) failed=(\d+)' | Select-Object -Last 1 } else { $null }
    if (-not $summary) { throw "The self-test didn't finish (no AUTOTEST SUMMARY in $log). Not uploading." }
    $failed = [int]$summary.Matches[0].Groups[2].Value
    Write-Host $summary.Line
    if ($failed -gt 0) { throw "$failed self-test check(s) failed. Not uploading." }
}
$content = Join-Path $root 'Build'
if (-not (Test-Path (Join-Path $content 'Spellcaster.exe'))) { throw "No build in $content. Run .\play.ps1 -BuildOnly first (or drop -SkipBuild)." }

# 2. The SteamPipe build script (written fresh each time, into Logs\steam, which git ignores).
$out = Join-Path $root 'Logs\steam'
New-Item -ItemType Directory -Force (Join-Path $out 'output') | Out-Null
# Forward slashes and no quotes, so nothing in the strings needs escaping.
function Q([string]$s) { '"' + ($s -replace '\\', '/' -replace '"', "'") + '"' }
$vdf = @"
"AppBuild"
{
	"AppID" "$AppId"
	"Desc" $(Q $Desc)
	"ContentRoot" $(Q ($content + '\'))
	"BuildOutput" $(Q ((Join-Path $out 'output') + '\'))
	"Preview" "$(if ($Preview) { 1 } else { 0 })"
$(if ($Branch) { "`t`"SetLive`" $(Q $Branch)" })
	"Depots"
	{
		"$DepotId"
		{
			"FileMapping"
			{
				"LocalPath" "*"
				"DepotPath" "."
				"recursive" "1"
			}
			"FileExclusion" "steam_appid.txt"
			"FileExclusion" "*_DoNotShip*"
			"FileExclusion" "*.pdb"
		}
	}
}
"@
$vdfPath = Join-Path $out 'app_build.vdf'
Set-Content -Path $vdfPath -Value $vdf -Encoding ascii
Write-Host "Build script: $vdfPath"
Write-Host "  App $AppId, depot $DepotId, `"$Desc`"$(if ($Branch) { ", live on '$Branch'" } else { ', not set live' })"

# 3. steamcmd (Valve's command-line uploader).
$steamcmd = @($env:STEAMCMD, (Join-Path $here 'steamcmd\steamcmd.exe'), 'C:\steamcmd\steamcmd.exe') |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($NoUpload) {
    Write-Host ''
    Write-Host 'Would run:' -ForegroundColor Cyan
    Write-Host "  $(if ($steamcmd) { $steamcmd } else { '<steamcmd.exe not found yet>' }) +login $(if ($User) { $User } else { '<user>' }) +run_app_build `"$vdfPath`" +quit"
    exit 0
}
if (-not $User) { throw "Pass -User <your Steamworks account name> (or set `$env:STEAM_BUILD_USER)." }
if (-not $steamcmd) {
    $dir = Join-Path $here 'steamcmd'
    Write-Host "steamcmd not found; downloading Valve's official copy into $dir ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir 'steamcmd.zip'
    Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $dir -Force
    Remove-Item $zip
    $steamcmd = Join-Path $dir 'steamcmd.exe'
}

Write-Host "Uploading with steamcmd (it may ask for your password and Steam Guard code)..." -ForegroundColor Cyan
& $steamcmd +login $User +run_app_build $vdfPath +quit
if ($LASTEXITCODE -ne 0) {
    if ($LASTEXITCODE -eq 6) {
        Write-Host ''
        Write-Host '"Failed to commit build" means the files reached Steam but the build could not be registered. Usually:' -ForegroundColor Yellow
        if ($Branch) { Write-Host "  - the branch '$Branch' doesn't exist yet (create it in Steamworks > SteamPipe > Builds), or this account can't set builds live" -ForegroundColor Yellow }
        Write-Host "  - depot $DepotId isn't one of the app's depots, or the depot setup wasn't published (SteamPipe > Depots, then Publish)" -ForegroundColor Yellow
        Write-Host "  - this account lacks the 'Edit App Metadata' permission for the app" -ForegroundColor Yellow
        Write-Host 'Uploaded files are kept: retrying with -SkipBuild is fast.' -ForegroundColor Yellow
    }
    throw "steamcmd failed (exit $LASTEXITCODE). Its logs: $(Join-Path $out 'output')"
}
Write-Host ''
Write-Host 'Uploaded.' -ForegroundColor Green
if ($Branch) { Write-Host "It's live on the '$Branch' branch: testers on that branch get the update automatically." }
else { Write-Host 'Next: Steamworks > SteamPipe > Builds, find this build, and set it live on a branch (default or a beta).' }
