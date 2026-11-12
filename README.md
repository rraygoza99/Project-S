# Spellcaster

A retro, DOOM-style first-person roguelike where you are a wizard. Spells are cast Helldivers-2
"stratagem" style: hold a key, type an arrow sequence, click to unleash. Fight through three dungeon
floors room by room, collect spell runes, relics and items, and fuse your spells into new ones.
Built in Unity 6 (6000.3, Built-in render pipeline). The whole game is built from code at runtime:
no hand-made scenes or prefabs.

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
| Pixel Monsters Vol.2 (Ismartal) | Imp = "monster 8", ranged enemy and boss = "monster 16" (floating eye) |
| SpellBook Megapack (Rexard) | Spell icons |
| Monster Sounds Pack (Cafofo) | Enemy hit / death / attack sounds |
| Lightning Bolt Effect (Digital Ruby) | Lightning bolts (used via reflection; built-in fallback) |

Only the needed files are copied into `Assets/ThirdParty/` (gitignored: Asset Store content may not
be redistributed).

## Main menu

- **Start game**: a new run. The very first run opens with the tutorial.
- **Tutorial**: five short steps in the first room (walk, magic bolt, start a rune, draw Fireball,
  cast it) and a tips card. Enter skips it.
- **Practice tool**: learn the runes. Every spell is unlocked, there are no cooldowns, no damage and
  no fights, and three frozen training dummies respawn when killed. A trainer panel picks a target
  spell, shows its rune, and after each attempt tells you which strokes it read (N: new target).
- **Unlocks**: meta progression (below).
- **Options**: mouse sensitivity, invert mouse Y, rune drawing size (how far the mouse moves per
  stroke), field of view, volume, display mode, resolution, FPS counter, reset to defaults, and
  **Controls**: rebind every action (two keys each; mouse buttons work too; a key already in use
  swaps over). Everything is saved between sessions.
- **Exit game**.

## Controls

Defaults (rebind them in Options > Controls):

| Input | Action |
|---|---|
| Mouse | Look (draws runes while channeling) |
| W A S D | Move (also while channeling, at 30% speed) |
| Hold **Right Mouse** (or **Left Ctrl**) | Channel: the view stops turning and the mouse draws a rune in front of you |
| **Left Click** | Cast the readied spell, or fire the magic bolt (hold for auto-fire) |
| **1 2 3** | Use the item in that slot |
| **E** | Take / buy the item in front of you |
| Hold **M** | Full map, stats and items |
| **Tab** | Spell Forge (outside of fights; the game pauses) |
| Esc | Pause (click to resume, Q: main menu) |
| R / Esc | New run / main menu after death or victory |
| K | Debug: spawn 10 enemies in this room |

Every spell is a **rune**: a few straight up / down / left / right strokes drawn with the mouse
(Fireball is a U: down, right, up). Draw in one go; a stroke counts once you move far enough in a new
direction, carrying on in the same direction doesn't add strokes, and diagonal wobble is ignored. The
rune glows in front of you while you draw, and the spell panel shows each spell's shape.

Casting rules: a full sequence **readies** the spell (hands glow in its colour); releasing the button keeps
it readied; only one spell can be readied. Input that matches no available spell **fizzles** (0.3 s
lockout). Spells on cooldown can't be readied, and cooldowns only count down during a fight (they pause in
cleared rooms, so you can't wait out Heal between fights).

## The run

- **Floors.** Three floors of 10, 12 and 14 rooms on a grid (Isaac style), each with its own colours
  and tougher enemies. Every floor has a **treasure room** (free relic or item), a **shop** (spend
  gold), a **rune shrine** (a new spell) and a **boss room** at the far end.
- **Rooms.** Walking into an uncleared room drops iron gates over its doors and summons the fight.
  Clear it to open the doors; cleared rooms sometimes leave a reward.
- **Room sizes.** Most rooms are one grid cell (26 x 26 m). Some combat rooms are big: long halls
  (2x1 or 1x2) and 2x2 arenas, with a cover layout in each cell and more enemies (1.6x / 2.1x).
  The start room, its neighbours and the special rooms are always one cell.
- **Boss.** The Watcher fires fans of bolts and summons imps. Killing it opens a portal to the next
  floor (and drops an item and a heart). The portal on floor 3 wins the run.
- **Map.** The minimap shows visited rooms and their neighbours; T, $, R and B mark the special rooms.
- **Death** ends the run. Nothing carries over between runs.

### Unlocks (meta progression)

18 achievements, tracked across runs, each unlock one new spell or item. Unlocked things can then
appear in runs (runes, treasure, shops, drops). Unlocks never raise your stats; they only add
variety. Nothing counts in the practice tool or the tutorial.

| Goal | Unlocks |
|---|---|
| Clear 10 rooms | Poison Cloud (← ↓ →): toxic cloud that keeps poisoning |
| Defeat 100 enemies | Arcane Missiles (→ ↑ →): five homing missiles |
| Clear a room without getting hurt | Blink (↑ ← ↑): teleport forward |
| Defeat a boss | Quake (↓ ← ↓): stuns and damages everything around you |
| Heal 300 HP in total | Life Drain (← ↑ ←): beam that heals you for half its damage |
| Reach floor 3 | Meteor (→ ↓ ←): falls on the crosshair spot, 6 m blast |
| Die once | Thorned Robe: enemies that hit you up close take 15 damage |
| Die 5 times | Phoenix Feather: survive one lethal hit with half your HP |
| Defeat 50 enemies with the magic bolt | Bolt Splitter: the bolt fires 3 bolts |
| Collect 300 gold in total | Gold Tooth: every coin is worth 1 more gold |
| Defeat 30 enemies with poison | Toxic Vial: every spell hit poisons |
| Cast 150 spells | Storm Crown: every 5th spell has no cooldown |
| Defeat 3 enemies with one spell | Overcharge: a spell kept ready for 1.5 s hits 50% harder |
| Freeze 100 enemies | Frozen Heart: getting hurt freezes enemies around you |
| Forge a spell | Hex Mark: spells deal 30% more to burning or poisoned enemies |
| Use 10 items | Smoke Bomb: stun every nearby enemy for 3 s |
| Buy 5 items in shops | Time Scroll: enemies at half speed for 6 s |
| Learn 8 spells from runes | Rune Stone: learn a random spell you don't know |

New Forge synergies: Fire + Poison = **Combustion** (hits burst into flame), Earth + Storm =
**Tremor** (hits stun). Goals and rewards live in `Core/Progress.cs`; locked content is marked
`StartsLocked` in `Spells/SpellBook.cs` and `Loot/Items.cs`.

### Obstacles

| Obstacle | |
|---|---|
| Pillars and cover walls | Six room layouts block movement and shots |
| Explosive barrel | Breaks in one bolt; blasts enemies, you and other barrels (4 m) |
| Crate | Breakable; may drop gold, a heart or an item |
| Spike trap | Rises every few seconds after a short warning; hurts you and walking enemies |

### Loot

Enemies drop **gold** (it flies to you) and sometimes hearts or items. **Luck** raises every drop chance.

| Stat items | |
|---|---|
| Ruby Heart | +25 max HP |
| Swift Boots | +15% move speed |
| Arcane Focus | +20% spell power |
| Hourglass | Spell cooldowns -15% |
| Sharpened Wand | Magic bolt +5 damage |
| Quickcast Ring | Magic bolt fires 30% faster |
| Iron Robe | Take 15% less damage |
| Lucky Clover | More drops |
| Vampire Fang | Heal 2 HP per kill |
| Glass Cannon | +40% spell power, -25 max HP |

| Relics (change how spells behave) | |
|---|---|
| Splitting Prism | Projectile spells fire 2 extra projectiles |
| Conductive Coil | Chains jump to 2 more enemies |
| Volatile Soul | Enemies killed by spells explode |
| Permafrost Shard | Frozen enemies that die freeze their neighbours |
| Ember Brand | Every spell hit sets enemies burning |
| Echo Stone | 25% chance a spell casts twice |
| Leech Sigil | Spells heal you for 8% of their damage |
| Blast Core | Explosions and novas are 40% bigger |
| Frostbite Wand | Magic bolts may freeze enemies |

| Items (3 slots, keys 1 2 3) | |
|---|---|
| Health Potion | Restore 35 HP |
| Fire Bomb | Throw: explodes for 60 damage |
| Frost Scroll | Freeze every nearby enemy for 4 s |
| Haste Elixir | 10 s of faster movement and half cooldowns |
| Scroll of Renewal | Reset all spell cooldowns |

## Spells

Every run starts with **Fireball**; the others are learned from **runes** (rune shrines, shops).

| Spell | Rune | Shape | Effect | Cooldown |
|---|---|---|---|---|
| Fireball | ↓ → ↑ | U | Projectile, explodes for area damage | 3 s |
| Lightning | → ↓ → | Z bolt | Instant hit, chains to 2 nearby enemies | 6 s |
| Frost Nova | ↓ ← ↑ → | loop | Freezes everything around you for 3 s | 10 s |
| Heal | ↑ ↓ ↑ ↓ | zig-zag | Restore 25 HP | 15 s |
| Frost Shard | ← ↑ → | arch | Fast projectile, freezes for 1.2 s | 2.5 s |
| Thunder Ring | ↑ → ↓ | cap ∩ | Storm nova that throws enemies back | 8 s |

### Spell Forge: make your own spells

Press **Tab** when no fight is on, pick two spells with the number keys, check the preview, press
**Enter**. The new spell:

- uses the first spell's **delivery** (projectile / instant bolt / nova / self) and carries **both
  payloads** at 85% strength,
- gets a cooldown of 80% of both cooldowns combined and a new, longer arrow sequence (starting with
  the first stroke of each parent, never the same stroke twice in a row; it never clashes with a known spell or any rune you may find),
- unlocks **synergies** when elements react:

| Elements | Synergy | Effect |
|---|---|---|
| Fire + Frost | Shatter | Frozen targets take +45 damage (consumes the freeze) |
| Fire + Storm | Ignite | Targets burn for 12/s over 4 s |
| Frost + Storm | Thunderclap | Knocks targets back |
| Life + any damage | Lifesteal | Heal 25% of the damage dealt |

Forged spells can be forged again (tier 3, 4...), up to 12 effects and 9 known spells. Relics apply
to forged spells too, so a Splitting Prism on a "Frozen Fireball" fires three freezing fireballs.

## How it's built (and how to add things)

**Add a spell:** add an entry to `Spells/SpellBook.cs`, for example

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

It automatically becomes a rune you can find. If no existing effect does what you want, add a class
to `SpellEffects.cs` (subclass `SpellEffect`, pick a `Role`: *Direct* per enemy hit, *Spread* to find
more targets, *Caster* once for you; implement `Scaled` and `Describe`). It then works with every
delivery, the Forge, relics and synergy rules. The rune must not repeat a stroke twice in a row or be a prefix of another spell's rune.

**Add a stat item or relic:** add an entry to `Loot/Items.cs` with an `OnAcquire` that edits
`PlayerStats`. Relic effects read the modifier fields in `PlayerStats` from the spell code
(`SpellCast`, `SpellEffects`, `SpellDeliveries`). Pick an icon shape from `Loot/ItemIcons.cs`.

**Add a room layout:** add a case to `Level/RoomLayouts.cs`.

| Area | Files |
|---|---|
| Startup / flow | `Core/Bootstrap.cs`, `Core/GameState.cs` (title, death, victory, restart), `Core/Run.cs` (gold, items, slots), `Core/PlayerStats.cs`, `Core/AutoTest.cs` |
| Dungeon | `Level/Dungeon.cs` (floors, doors, encounters, boss, portal), `Level/Room.cs` (room grid generator), `Level/RoomLayouts.cs`, `Level/Obstacles.cs` (barrels, crates, spikes) |
| Loot | `Loot/Items.cs` (the loot table), `Loot/Pickup.cs`, `Loot/Loot.cs` (drop chances), `Loot/ItemIcons.cs` |
| Rendering | `Rendering/RetroView.cs` (200 px tall internal resolution, nearest-neighbour upscale), `Billboard.cs`, `PixelArt.cs`, `Sprites.cs`, `SpriteAnim.cs`, shaders in `Resources/Shaders` (banded fog) |
| Player | `Player/Player.cs`, `Player/WizardHands.cs` |
| Spells | `Spells/*`, `UI/SpellForgeUI.cs` |
| Enemies | `Enemies/EnemyType.cs` (imp, eye, boss), `Enemy.cs`, `EnemySpawner.cs` |
| Combat / FX | `Combat/Combat.cs` (explosions, bombs), `Projectile.cs`, `Fx.cs`, `LightningFx.cs` |
| HUD / audio | `UI/HUD.cs` (minimap, full map, slots, shop prices, boss bar), `Audio/Sfx.cs` |
