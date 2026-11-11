# Spellcaster

A retro, DOOM-style first-person shooter where you are a wizard. Spells are cast Helldivers-2
"stratagem" style: hold a key, type an arrow sequence, click to unleash. Built in Unity 6
(6000.3, Built-in render pipeline). The whole game is built from code at runtime: no hand-made
scenes or prefabs.

## Run it

```
play.bat
```

`play.bat` (or `.\play.ps1`) builds the Windows player when sources changed (the first build takes a
few minutes) and launches it. Options: `-Rebuild`, `-BuildOnly`, `-AutoTest` (scripted self-test:
screenshots + PASS/FAIL log in `AutoTest\`). Set `UNITY_EDITOR` if your Unity.exe is not in the
default Hub location.

### Optional Asset Store art and sound

The game looks and sounds complete with its built-in procedural pixel art and synth sounds. If you
own these packs, download them in Unity's Package Manager (My Assets > Download), then run
`tools\import-assets.ps1`:

| Pack | Used for |
|---|---|
| Pixel Monsters Vol.2 (Ismartal) | Imp = "monster 8", ranged enemy = "monster 16" (floating eye) |
| SpellBook Megapack (Rexard) | Spell icons |
| Monster Sounds Pack (Cafofo) | Enemy hit / death / attack sounds |
| Lightning Bolt Effect (Digital Ruby) | Lightning bolts (used via reflection; built-in fallback) |

Only the needed files are copied into `Assets/ThirdParty/` (gitignored: Asset Store content may not
be redistributed).

## Controls

| Input | Action |
|---|---|
| Mouse | Look |
| W A S D | Move |
| Hold **Left Ctrl** or **Right Mouse** | Channel (hands raise, movement slows to 30%; WASD still steers) |
| Arrow keys while channeling | Enter ↑ ← ↓ → |
| **Left Click** | Cast the readied spell, or fire the basic bolt (hold for auto-fire) |
| **Tab** | Open the Spell Forge (game pauses) |
| Enter | Start the next wave early during a break |
| Esc | Release the mouse (pauses); click to resume |
| R | Restart after death |
| K | Debug: spawn 10 extra enemies |

Casting rules: a full sequence **readies** the spell (hands glow in its colour); releasing Ctrl keeps
it readied; only one spell can be readied. Input that matches no available spell **fizzles** (0.3 s
lockout). Spells on cooldown can't be readied.

| Spell | Sequence | Effect | Cooldown |
|---|---|---|---|
| Fireball | ↓ → ↑ | Projectile, explodes for area damage | 3 s |
| Lightning | → → ↓ → | Instant hit, chains to 2 nearby enemies | 6 s |
| Frost Nova | ↓ ↓ ← → | Freezes everything around you for 3 s | 10 s |
| Heal | ↑ ↓ ↑ ↓ | Restore 40 HP | 15 s |

## Spell Forge: make your own spells

Press **Tab**, pick two spells with the number keys, check the preview, press **Enter**. The new
spell:

- uses the first spell's **delivery** (projectile / instant bolt / nova / self) and carries **both
  payloads** at 85% strength,
- gets a cooldown of 80% of both cooldowns combined and a new, longer arrow sequence (starting with
  the first arrow of each parent; it never clashes with a known sequence),
- unlocks **synergies** when elements react:

| Elements | Synergy | Effect |
|---|---|---|
| Fire + Frost | Shatter | Frozen targets take +45 damage (consumes the freeze) |
| Fire + Storm | Ignite | Targets burn for 12/s over 4 s |
| Frost + Storm | Thunderclap | Knocks targets back |
| Life + any damage | Lifesteal | Heal 25% of the damage dealt |

Forged spells can be forged again (tier 3, 4...), so combinations keep getting deeper, up to 12
effects and 9 known spells. Forged spells survive a restart but not quitting the game.

Example: *Fireball + Frost Nova = "Frozen Fireball"*: an exploding projectile that freezes everything
in the blast and shatters enemies that were already frozen.

## How spells work (and how to add one)

A spell = **sequence + cooldown + delivery + payload of effects** (`Assets/Scripts/Spells/`).

- `SpellBook.cs`: **the config file**, one entry per base spell.
- `SpellEffects.cs`: effect building blocks. Each has a role:
  *Direct* (applied to every enemy hit: damage, freeze, ignite, shatter, knockback),
  *Spread* (finds more targets and applies the Direct effects to them: explosion, chain), or
  *Caster* (applied once to you: heal, lifesteal).
- `SpellDeliveries.cs`: projectile, hitscan, nova, self.
- `SpellSynergies.cs`: element reaction rules for the Forge.

**Add a new spell:** add an entry to `SpellBook.cs`, for example

```csharp
new SpellDefinition
{
    Id = "poison", Name = "Poison Cloud", Noun = "Cloud", Adjective = "Toxic",
    Sequence = Arrows.Parse("←←↑"), Cooldown = 8f, IconKey = "poison",
    Color = new Color(0.5f, 1f, 0.2f),
    Delivery = new ProjectileDelivery(speed: 12f, size: 1.2f),
    Payload = { new DamageEffect(10f, Element.Life), new ExplodeEffect(radius: 5f, falloff: 1f) },
},
```

If no existing effect does what you want, add one effect class to `SpellEffects.cs` (subclass
`SpellEffect`, pick a `Role`, implement `ApplyTo` / `Spread` / `ApplyToCaster`, `Scaled` and
`Describe`). It then works with every delivery, in the Forge, and in synergy rules automatically.
Icons load from `Resources/SpellIcons/<IconKey>.png`; without one, a rune icon is generated.
The sequence must not be a prefix of another spell's sequence (or vice versa).

## Code map

| Area | Files |
|---|---|
| Startup / flow | `Core/Bootstrap.cs`, `Core/GameState.cs` (waves, death, restart), `Core/AutoTest.cs` |
| Rendering | `Rendering/RetroView.cs` (200 px tall internal resolution, nearest-neighbour upscale), `Billboard.cs` (Y-axis-only facing), `PixelArt.cs`, `Sprites.cs`, `SpriteAnim.cs`, shaders in `Resources/Shaders` (banded fog) |
| Player | `Player/Player.cs`, `Player/WizardHands.cs` |
| Spells | `Spells/*` (see above), `UI/SpellForgeUI.cs` |
| Enemies | `Enemies/EnemyType.cs`, `Enemy.cs`, `EnemySpawner.cs` |
| HUD / audio | `UI/HUD.cs`, `Audio/Sfx.cs` (synth arrow ticks / chimes, monster sounds) |
