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
        'Assets/Pixel Monsters Vol2/Sprites/monster 18/m18_Fly.png' = 'Resources/Monsters/skull_fly.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 18/m18_Hit.png' = 'Resources/Monsters/skull_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 18/m18_Dead.png' = 'Resources/Monsters/skull_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Walk.png' = 'Resources/Monsters/beetle_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Run.png' = 'Resources/Monsters/beetle_run.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Alert.png' = 'Resources/Monsters/beetle_alert.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Attack.png' = 'Resources/Monsters/beetle_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Hit.png' = 'Resources/Monsters/beetle_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 4/m4_Dead.png' = 'Resources/Monsters/beetle_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 11/m11_Fly.png' = 'Resources/Monsters/hornet_fly.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 11/m11_Attack.png' = 'Resources/Monsters/hornet_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 11/m11_Hit.png' = 'Resources/Monsters/hornet_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 11/m11_Dead_Air.png' = 'Resources/Monsters/hornet_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 13/m13_Walk.png' = 'Resources/Monsters/shade_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 13/m13_Attack.png' = 'Resources/Monsters/shade_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 13/m13_Hit.png' = 'Resources/Monsters/shade_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 13/m13_Dead.png' = 'Resources/Monsters/shade_dead.png'
        # floor 2 and 3 enemies, hazards and bosses
        'Assets/Pixel Monsters Vol2/Sprites/monster 1/m1_Walk.png' = 'Resources/Monsters/slug_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 1/m1_Hit.png' = 'Resources/Monsters/slug_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 1/m1_Dead.png' = 'Resources/Monsters/slug_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 2/m2_Fly.png' = 'Resources/Monsters/moth_fly.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 2/m2_Attack.png' = 'Resources/Monsters/moth_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 2/m2_Hit.png' = 'Resources/Monsters/moth_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 2/m2_Dead.png' = 'Resources/Monsters/moth_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 12/m12_Idle.png' = 'Resources/Monsters/frog_idle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 12/m12_Run.png' = 'Resources/Monsters/frog_run.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 12/m12_Attack.png' = 'Resources/Monsters/frog_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 12/m12_Hit.png' = 'Resources/Monsters/frog_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 12/m12_Dead.png' = 'Resources/Monsters/frog_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 14/m14_Walk.png' = 'Resources/Monsters/crawler_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 14/m14_Attack.png' = 'Resources/Monsters/crawler_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 14/m14_Hit1.png' = 'Resources/Monsters/crawler_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 14/m14_Dead1.png' = 'Resources/Monsters/crawler_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 17/m17_Walk.png' = 'Resources/Monsters/robot_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 17/m17_Attack.png' = 'Resources/Monsters/robot_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 17/m17_Hit.png' = 'Resources/Monsters/robot_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 17/m17_Dead.png' = 'Resources/Monsters/robot_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Appear.png' = 'Resources/Monsters/mimic_appear.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Idle.png' = 'Resources/Monsters/mimic_idle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Run.png' = 'Resources/Monsters/mimic_run.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Attack.png' = 'Resources/Monsters/mimic_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Hit.png' = 'Resources/Monsters/mimic_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 19/m19_Dead.png' = 'Resources/Monsters/mimic_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 6/m6b_Tentacle.png' = 'Resources/Monsters/pod_tentacle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 6/m6b_Attack.png' = 'Resources/Monsters/pod_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Idle.png' = 'Resources/Monsters/burrower_idle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Appear.png' = 'Resources/Monsters/burrower_appear.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Disappear.png' = 'Resources/Monsters/burrower_disappear.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Attack.png' = 'Resources/Monsters/burrower_attack.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Hit.png' = 'Resources/Monsters/burrower_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 20/m20_Dead.png' = 'Resources/Monsters/burrower_dead.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Idle.png' = 'Resources/Monsters/behemoth_idle.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Walk.png' = 'Resources/Monsters/behemoth_walk.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Roar.png' = 'Resources/Monsters/behemoth_roar.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_RoarAnticipation.png' = 'Resources/Monsters/behemoth_roar_wind.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_RollAttackAnticipation.png' = 'Resources/Monsters/behemoth_roll_wind.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_RollAttack.png' = 'Resources/Monsters/behemoth_roll.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_RollAttackRecoil.png' = 'Resources/Monsters/behemoth_roll_end.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_SpikeAttackAnticipation.png' = 'Resources/Monsters/behemoth_spike_wind.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_SpikeAttack.png' = 'Resources/Monsters/behemoth_spike.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_SpikeAttackRecoil.png' = 'Resources/Monsters/behemoth_spike_end.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_TakeOff.png' = 'Resources/Monsters/behemoth_takeoff.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Fall.png' = 'Resources/Monsters/behemoth_fall.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Landing.png' = 'Resources/Monsters/behemoth_landing.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Tired.png' = 'Resources/Monsters/behemoth_tired.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_HitGround.png' = 'Resources/Monsters/behemoth_hit.png'
        'Assets/Pixel Monsters Vol2/Sprites/monster 7/m7_Dead.png' = 'Resources/Monsters/behemoth_dead.png'
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
