# Copies the handful of Asset Store files Spellcaster uses out of your downloaded .unitypackage
# files (Package Manager > My Assets > Download) into Assets/ThirdParty.
# Asset Store content may not be redistributed, so Assets/ThirdParty is gitignored; without it the
# game falls back to its built-in procedural art and synth sounds.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$store = Join-Path $env:APPDATA 'Unity\Asset Store-5.x'
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'

$packages = @(
    @{ File = 'Ismartal\Textures Materials2D Characters\Pixel Monsters Vol2.unitypackage'; Map = @{
        'Assets/Pixel Monsters Vol2/Sprites/monster 8/m8_Run.png'     = 'Resources/Monsters/imp_run.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 8/m8_Attack.png'  = 'Resources/Monsters/imp_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 8/m8_Hit.png'     = 'Resources/Monsters/imp_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 8/m8_Dead.png'    = 'Resources/Monsters/imp_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 16/m16_Idle.png'  = 'Resources/Monsters/watcher_idle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 16/m16_Attack.png'= 'Resources/Monsters/watcher_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 16/m16_Hit.png'   = 'Resources/Monsters/watcher_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 16/m16_Dead.png'  = 'Resources/Monsters/watcher_dead.png'
    } },
    @{ File = 'REXARD\Textures MaterialsIcons UI\SpellBook Megapack.unitypackage'; Map = @{
        'Assets/SpellBookMegapack/Skill_Icon_Pack/red/red_16.PNG'     = 'Resources/SpellIcons/fireball.png'
        'Assets/SpellBookMegapack/Skill_Icon_Pack/blue/blue_23.PNG'   = 'Resources/SpellIcons/lightning.png'
        'Assets/SpellBookMegapack/Skill_Icon_Pack/blue/blue_04.png'   = 'Resources/SpellIcons/frost.png'
        'Assets/SpellBookMegapack/Skill_Icon_Pack/green/green_20.PNG' = 'Resources/SpellIcons/heal.png'
        'Assets/SpellBookMegapack/Skill_Icon_Pack/red/red_20.PNG'     = 'Resources/SpellIcons/burn.png'
        'Assets/SpellBookMegapack/Skill_Icon_Pack/blue/blue_06.png'   = 'Resources/SpellIcons/shatter.png'
    } },
    @{ File = 'Cafofo\AudioSound FXCreatures\Monster Sounds Pack.unitypackage'; Map = @{
        'Assets/Monster Sounds Pack/Demon 1/Demon 1 - Short 01.wav'                 = 'Resources/MonsterSounds/imp_attack_1.wav'
        'Assets/Monster Sounds Pack/Demon 1/Demon 1 - Short 02.wav'                 = 'Resources/MonsterSounds/imp_attack_2.wav'
        'Assets/Monster Sounds Pack/Demon 1/Demon 1 - Short 03.wav'                 = 'Resources/MonsterSounds/imp_attack_3.wav'
        'Assets/Monster Sounds Pack/Humanoid Small 1/Humanoid Small - Short 1.wav'  = 'Resources/MonsterSounds/imp_hit_1.wav'
        'Assets/Monster Sounds Pack/Humanoid Small 1/Humanoid Small - Short 2.wav'  = 'Resources/MonsterSounds/imp_hit_2.wav'
        'Assets/Monster Sounds Pack/Humanoid Small 1/Humanoid Small - Short 3.wav'  = 'Resources/MonsterSounds/imp_hit_3.wav'
        'Assets/Monster Sounds Pack/Demon 1/Demon 1 - Long 01.wav'                  = 'Resources/MonsterSounds/imp_death_1.wav'
        'Assets/Monster Sounds Pack/Demon 1/Demon 1 - Long 02.wav'                  = 'Resources/MonsterSounds/imp_death_2.wav'
        'Assets/Monster Sounds Pack/Ghost/Ghost 1_1.wav'                            = 'Resources/MonsterSounds/watcher_attack_1.wav'
        'Assets/Monster Sounds Pack/Ghost/Ghost 1_2.wav'                            = 'Resources/MonsterSounds/watcher_attack_2.wav'
        'Assets/Monster Sounds Pack/Ooze/Ooze 1_1.wav'                              = 'Resources/MonsterSounds/watcher_hit_1.wav'
        'Assets/Monster Sounds Pack/Ooze/Ooze 1_2.wav'                              = 'Resources/MonsterSounds/watcher_hit_2.wav'
        'Assets/Monster Sounds Pack/Ghost/Ghost 2_1.wav'                            = 'Resources/MonsterSounds/watcher_death_1.wav'
        'Assets/Monster Sounds Pack/Ghost/Ghost 2_2.wav'                            = 'Resources/MonsterSounds/watcher_death_2.wav'
    } },
    @{ File = 'Digital Ruby Jeff Johnson\ScriptingEffects\Lightning Bolt Effect for Unity.unitypackage'; Map = @{
        'Assets/LightningBolt/LightningBoltScript.cs'               = 'LightningBolt/LightningBoltScript.cs'
        'Assets/LightningBolt/Textures/LightningBoltTexture.png'    = 'Resources/Lightning/bolt.png'
    } }
)

$dest = Join-Path $root 'Assets\ThirdParty'
$work = Join-Path $env:TEMP ('spellcaster-import-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    foreach ($pkg in $packages) {
        $file = Join-Path $store $pkg.File
        if (-not (Test-Path $file)) { Write-Warning "Not downloaded, skipping: $($pkg.File)"; continue }
        $dir = Join-Path $work ([IO.Path]::GetFileNameWithoutExtension($file) -replace '\W', '')
        New-Item -ItemType Directory -Force $dir | Out-Null
        & $tar -xzf $file -C $dir --include '*/pathname'
        $wanted = @{}
        foreach ($pn in Get-ChildItem $dir -Recurse -Filter pathname) {
            $path = (Get-Content $pn.FullName -TotalCount 1).Trim()
            if ($pkg.Map.ContainsKey($path)) { $wanted[$pn.Directory.Name] = $pkg.Map[$path] }
        }
        if ($wanted.Count -eq 0) { Write-Warning "Nothing matched in $($pkg.File)"; continue }
        $members = $wanted.Keys | ForEach-Object { "$_/asset" }
        & $tar -xzf $file -C $dir @members
        foreach ($guid in $wanted.Keys) {
            $out = Join-Path $dest $wanted[$guid]
            New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
            Copy-Item (Join-Path $dir "$guid\asset") $out -Force
        }
        Write-Host ("{0}: {1}/{2} files" -f (Split-Path $pkg.File -Leaf), $wanted.Count, $pkg.Map.Count)
    }
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
