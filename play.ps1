# One command to build (when sources changed) and play Spellcaster.
#   .\play.ps1             build if needed, then play
#   .\play.ps1 -Rebuild    force a rebuild
#   .\play.ps1 -BuildOnly  build without launching
#   .\play.ps1 -AutoTest   run the scripted self-test (screenshots + FPS log in .\AutoTest)
param([switch]$Rebuild, [switch]$BuildOnly, [switch]$AutoTest)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$unity = $env:UNITY_EDITOR
if (-not $unity) {
    $version = '6000.3.5f1'
    $versionFile = Join-Path $root 'ProjectSettings\ProjectVersion.txt'
    if (Test-Path $versionFile) {
        $m = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)'
        if ($m) { $version = $m.Matches[0].Groups[1].Value }
    }
    $unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}
if (-not (Test-Path $unity)) { Write-Error "Unity Editor not found at $unity (set `$env:UNITY_EDITOR)"; exit 1 }

$exe = Join-Path $root 'Build\Spellcaster.exe'
$needsBuild = $Rebuild -or -not (Test-Path $exe)
if (-not $needsBuild) {
    $built = (Get-Item $exe).LastWriteTime
    $newer = Get-ChildItem (Join-Path $root 'Assets'), (Join-Path $root 'Packages') -Recurse -File |
        Where-Object { $_.LastWriteTime -gt $built } | Select-Object -First 1
    if ($newer) { $needsBuild = $true }
}

if ($needsBuild) {
    # The game uses the classic Input Manager; make sure it is enabled (0 = old, 2 = both).
    $ps = Join-Path $root 'ProjectSettings\ProjectSettings.asset'
    if (Test-Path $ps) {
        $text = Get-Content $ps -Raw
        if ($text -match 'activeInputHandler: 1') {
            ($text -replace 'activeInputHandler: 1', 'activeInputHandler: 2') | Set-Content $ps -NoNewline -Encoding utf8
        }
    }
    New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
    $log = Join-Path $root 'Logs\build.log'
    Write-Host "Building Spellcaster (first build takes a few minutes)... log: $log"
    $p = Start-Process -FilePath $unity -Wait -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-quit', '-projectPath', "`"$root`"",
        '-executeMethod', 'Spellcaster.EditorTools.BuildScript.BuildWindows',
        '-logFile', "`"$log`"")
    if ($p.ExitCode -ne 0 -or -not (Test-Path $exe)) {
        Write-Host "Build FAILED (exit $($p.ExitCode)). Errors:" -ForegroundColor Red
        Select-String -Path $log -Pattern 'error CS|SPELLCASTER BUILD FAILED|No valid Unity Editor license' | Select-Object -Last 25 | ForEach-Object { $_.Line }
        exit 1
    }
    Write-Host 'Build OK.' -ForegroundColor Green
}

if ($BuildOnly) { exit 0 }

if ($AutoTest) {
    $out = Join-Path $root 'AutoTest'
    New-Item -ItemType Directory -Force $out | Out-Null
    Remove-Item (Join-Path $out '*') -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @(
        '-autotest', "`"$out`"", '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', "`"$out\player.log`"")
    Select-String -Path "$out\player.log" -Pattern 'AUTOTEST|Exception' | ForEach-Object { $_.Line }
    exit $p.ExitCode
}

Start-Process -FilePath $exe
