# Spellcaster

A retro, DOOM-style first-person roguelike where you are a wizard. Spells are cast Helldivers-2
"stratagem" style: hold a key, type an arrow sequence, click to unleash. Fight through three dungeon
floors room by room, collect spell runes, relics and items, and fuse your spells into new ones.
Built in Unity 6 (6000.3, Built-in render pipeline). The whole game is built from code at runtime:
no hand-made scenes or prefabs.

## Play it

**[Download the game from the Releases page](https://github.com/rraygoza99/Project-S/releases)** (Windows 64-bit):

1. On the newest release, under **Assets**, download **Spellcaster-Windows.zip**.
   Don't download "Source code": that's for building the game, and it needs Unity.
2. Right-click the zip → **Extract All**, then open the extracted `Spellcaster` folder.
   (Running it from inside the zip without extracting won't work.)
3. Double-click **Spellcaster.exe**. If Windows says it protected your PC, click
   **More info → Run anyway**.

## Build from source (developers)

Needs Unity 6000.3.5f1.

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
| Pixel Monsters Vol.2 (Ismartal) | Imp = "monster 8", ranged enemy and boss = "monster 16" (floating eye), Flame Skull = 18, Horned Beetle = 4, Hornet = 11, Shade = 13; floor 2: Frost Slug = 1, Gloom Moth = 2, Cave Frog = 12, ceiling pod = 6, The Burrower = 20; floor 3: Cinder Crawler = 14, Forge Sentry = 17, Mimic = 19, The Behemoth = 7 |
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

### Dev mode

Press **F3** in game (or Options > Dev mode) to draw hitboxes over the view, through walls:
enemy hitboxes (green capsules around the sprite, with name and HP; flying enemies also show a faint
ring where they touch the floor), melee reach and beetle ram contact (red rings),
your projectiles (yellow) and enemy projectiles (magenta) at their real collision radius, and for
a moment after each hit: spell blast areas, nova and cloud rings (orange), instant rays and chain
jumps (blue), and enemy blasts and swings (bright red). A legend sits on the left.

## Multiplayer (in progress)

Online co-op for 2-6 players through Steam is being built in phases. What works now:

- **Main menu > Multiplayer.** Needs Steam running and signed in; otherwise the page says why and
  single-player is unaffected.
- **Host a game:** pick max players (2-6) and who can join (friends only, public, invite only). The lobby
  lists players with their Steam names and avatars; **Invite friends** opens the Steam overlay; the host
  can click a player to remove them and starts the run when everyone is ready.
- **Join a game:** friends' games and public Spellcaster lobbies, or accept a Steam invite / "Join game"
  in the friends list (also while the game is closed).
- **Different versions can't play together**; full lobbies and runs in progress refuse joins.
- **Direct IP (debug):** with Options > Dev mode on, copies of the game connect by address
  (127.0.0.1:7777 on one PC) without Steam.
- Starting the run puts everyone on the same seeded floor. Seeing each other in the world comes next.
- In a multiplayer session the pause menu doesn't stop the game, and only the host saves.

## Controls

Defaults (rebind them in Options > Controls). These are for the **Classic** casting style; see
**Casting styles** below for **Momentum**.

| Input | Action |
|---|---|
| Mouse | Look (draws runes while channeling) |
| W A S D | Move (also while channeling, at 30% speed) |
| Hold **Right Mouse** (or **Left Ctrl**) | Channel: the view stops turning and the mouse draws a rune in front of you |
| **Left Click** | Cast the readied spell, or fire the magic bolt (hold for auto-fire) |
| **1 2 3** | Use the item in that slot |
| **E** | Take / buy the item in front of you |
| Hold **M** | Full map, stats and items (Options > Map key: Toggle to press once instead) |
| **Tab** | Spell Forge (outside of fights; the game pauses) |
| Esc | Pause menu: **Resume**, **Options** (all the main menu settings) and **Save & quit**; your items and seed are listed beside it. Esc again resumes, Q saves and quits. Alt-tabbing out also pauses. |
| R / Esc | New run / main menu after death or victory |
| K | Debug: spawn 10 enemies in this room |

### Casting styles (Options > Casting style)

- **Classic** (default): hold Right Mouse or Left Ctrl; the view stops, the mouse draws the rune, and
  W A S D still walk at 30% speed.
- **Momentum**: hold Right Mouse or Left Ctrl; you **keep running the way you were already moving** (forward, sideways or back, relative to the camera)
  and **steer with the mouse**, while **W A S D type the rune** (W up, A left, S down, D right; the
  arrow keys too, since they are the second movement keys). Left Click fires while you run. Let go
  and you slow down and walk normally again. A key you were already holding when you started
  doesn't count until you press it again, and keys still held when you let go don't move you until
  you press them again. Opening the map, a panel, pausing or alt-tabbing stops you on the spot;
  press the cast key again to carry on. **Options > Momentum cue** adds a small field-of-view push
  and head bob while you run (on by default). If you are **standing still** when you press the cast
  key (slower than 0.5 m/s, no movement key held), that cast doesn't run: you stay where you are and
  W A S D only type the rune.
- Tuning: `Player/MomentumConfig.cs` (`SpeedMultiplier` 1.0, `RampIn` 0.15 s, `RampOut` 0.2 s, `StillSpeed` 0.5 m/s,
  `CueFov` 4 degrees, `CueBobHeight` 0.025 m, `CueBobRate` 9).

Every spell is a **rune**: a few straight up / down / left / right strokes drawn with the mouse
(Fireball is a U: down, right, up). Draw in one go; a stroke counts once you move far enough in a new
direction, carrying on in the same direction doesn't add strokes, and diagonal wobble is ignored. The
rune glows in front of you while you draw, and the spell panel shows each spell's shape.

Casting rules: a full sequence **readies** the spell (hands glow in its colour); releasing the button keeps
it readied; only one spell can be readied. Input that matches no available spell **fizzles** (0.3 s
lockout). Spells on cooldown can't be readied, and cooldowns only count down during a fight (they pause in
cleared rooms, so you can't wait out Heal between fights).

## The run

- **Floors.** Three floors of 10, 12 and 14 rooms on a grid (Isaac style). Every floor has a
  **treasure room** (free relic or item), a **shop** (spend gold), a **rune shrine** (a new spell)
  and a **boss room** at the far end. Each floor is its own place:

  | Floor | Look | Hazards | Enemies | Boss |
  |---|---|---|---|---|
  | 1. The Crypt | Brick halls, pillars and cover walls, torches, drifting dust | Spike traps, explosive barrels, crates | Imps, cultists, hornets, shades | The Watcher |
  | 2. Frozen Caverns | Rock and ice: boulders, crystal groves, ice walls, stalagmites, stalactites, falling snow, thick blue fog | Ice patches (you slide), frost crystals, ceiling pods | Frost slugs, gloom moths, cave frogs, shades | The Burrower |
  | 3. The Forge | Iron plates and basalt: anvils, grates, a furnace, lava pits, braziers, rising embers | Lava pools, fire jets, explosive barrels | Cinder crawlers, forge sentries, flame skulls, horned beetles, imps, mimics | The Behemoth |
- **Rooms.** Walking into an uncleared room drops iron gates over its doors and summons the fight.
  Clear it to open the doors; cleared rooms sometimes leave a reward.
- **Room sizes.** Most rooms are one grid cell (26 x 26 m). Some combat rooms are big: long halls
  (2x1 or 1x2) and 2x2 arenas, with a cover layout in each cell and more enemies (1.6x / 2.1x).
  The start room, its neighbours and the special rooms are always one cell.
- **Bosses.** Killing a floor's boss opens a portal to the next floor (and drops an item and a
  heart). The portal on floor 3 wins the run.
  - **The Watcher** (floor 1) fires fans of bolts and summons imps.
  - **The Burrower** (floor 2) spits fans of frost, then dives underground (it can't be hurt there)
    and bursts out under you: get off the purple warning ring. It calls slugs and moths, and below
    half HP it moves and spits faster.
  - **The Behemoth** (floor 3) walks at you, then either curls up and **rolls** across the arena
    (leaving burning ground; if it crashes into a wall it is tired for a few seconds: punish it),
    fires a **ring of spikes**, or **leaps** (can't be hurt in the air) and slams down on the orange
    warning ring. At half HP it roars and gets faster, and its slams call flame skulls.
- **Map.** The minimap shows visited rooms and their neighbours; T, $, R and B mark the special rooms.
- **Saving.** The run saves itself every time you clear a fight room (a small "Saving..." shows in
  the corner). **Continue** on the main menu puts you back in the last room you cleared, with the same
  HP, gold, items, spells, map and Grimoire deck. A room you were in the middle of starts over.
  **Save & quit** on the pause menu (or Esc, then Q) saves right where you are when no fight is on; in
  the middle of a fight you keep everything you have and the fight starts over from the last cleared
  room. The rest of the run plays out the same as if you had
  never quit: same rooms, same shops, same enemy waves, same rewards.
- **Seeds.** Every run has a seed, shown on the pause screen and the end screen (**C** copies it).
  **Seeded run** on the main menu starts a run from a seed you type: a number, or any word or
  phrase (case doesn't matter). The same seed always builds the same floors, props, shops, enemy
  waves and rewards, so you can share a run with friends. What enemies and crates drop when they
  die still depends on how each fight goes.
- **Death** (or winning) ends the run and deletes the save. Nothing carries over between runs.
  Starting a new run while a save exists asks first.

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
| Heal 300 HP in total | Life Drain (← ↑ ←): beam that heals you for 35% of its damage |
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
| Explosive barrel | Breaks in one bolt; blasts enemies, you and other barrels (4.2 m; aim at one to see the ring) |
| Crate | Breakable; may drop gold, a heart or an item |
| Spike trap (floor 1) | Rises every few seconds after a short warning; hurts you and walking (not flying) enemies; stops once the room is cleared |
| Ice patch (floor 2) | You slide: you keep your speed and turn slowly. Enemies don't slip |
| Frost crystal (floor 2) | Breakable; shatters into a freezing burst (3.6 m) that freezes enemies and slows you if you're close |
| Ceiling pod (floor 2) | Hangs from the ceiling; shakes, then drops its tentacle on whoever stands under it (12 damage) |
| Lava pool (floor 3) | Burns you (16 HP/s) and walking enemies; flyers are safe |
| Fire jet (floor 3) | A vent glows as a warning, then shoots a column of fire (14 damage); stops once the room is cleared |

### Enemies

| Enemy | From | Behaviour |
|---|---|---|
| Imp | floor 1 | Runs at you and claws |
| Cultist | floor 1 | Keeps its distance and shoots; walks around cover to find you |
| Hornet | floor 1 | Darting flyer that fires bursts of 3 stingers |
| Shade | floor 1 (deeper rooms) | Splits into 2 fast shadelings when it dies (they drop nothing) |
| Frost Slug | floor 2 | Slow and tough; hits hard up close |
| Gloom Moth | floor 2 | Flying; spits pairs of frost bolts from a distance |
| Cave Frog | floor 2 | Crouches, then leaps at you; bites up close |
| Flame Skull | floor 3 | Rushes you and explodes (18 damage, 3 m); also explodes when killed, hurting nearby enemies, so kill it at range |
| Horned Beetle | floor 3 | Tough; braces, then charges in a straight line (20 damage). Dodge it into a wall and it's dazed |
| Cinder Crawler | floor 3 | Throws fans of burning spikes |
| Forge Sentry | floor 3 | Little robot that fires bursts of 3 shots |
| Mimic | floor 3 (at most one per room) | Looks like a treasure chest until you get close or hit it; then it chases and bites hard |

Each floor has its own mix (`Enemies/EnemyTypesFloors.cs`); ranged enemies get more common further
from the start.

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

Press **Tab** when no fight is on and click two spells (or press their numbers). The top row shows
**A + B = result** and says what's still missing; click **Forge** (or press **Enter**). The new spell:

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
| Life + any damage | Lifesteal | Heal 15% of the damage dealt |

Lifesteal never stacks inside one spell (only its largest lifesteal counts, plus the Leech Sigil), and
one cast heals at most 12 HP.

**Forget a spell** to make room (the book holds 9): pick one spell in the Forge and click **Forget**
(or press Delete) twice. Its rune drops at your feet, so you can pick it back up if you change your
mind. You always keep at least 2 spells.

Forged spells can be forged again (tier 3, 4...), up to 12 effects and 9 known spells. Relics apply
to forged spells too, so a Splitting Prism on a "Frozen Fireball" fires three freezing fireballs.

### Grimoire (experimental, off by default)

A deck of enhancement cards that ride on your spells. Turn it on in **Options > Grimoire cards**. With
it off, the game plays exactly as without it.

- **Deck:** a draw pile, a hand of 3 (bottom right, next to your hands) and a discard pile. Every run
  starts with 5 Blank, 1 Amplify, 1 Split and 1 Echo.
- **Casting** a spell plays the leftmost card (**NEXT**): its effect applies to that cast, it goes to
  the discard pile and a new card is drawn. The basic bolt only uses up **Blank** cards, so special
  cards wait for a rune spell. While a spell is readied, your hands glow in the Next card's colour.
- An empty draw pile is refilled by **reshuffling** the discard pile.
- **Q** discards the Next card unused (you can't cast for 1 s). Blank cards and curses can't be
  discarded this way: you have to play them.
- **Clearing a fight room** offers 3 cards (click or press 1-3), **Remove a card** for 50 gold (4), or
  **Skip** (5). The game is paused while the panel is open.
- **Shops** also sell a **Scroll of Erasure** (35 gold): use it like any item to remove one card from
  your deck for free (backing out gives the scroll back).
- **Elites** (one in about 15% of fight rooms, "ELITE" over their head, double HP) put a **Fumble**
  curse in your deck when they die.
- The pause screen lists your whole deck.

| Card | Effect |
|---|---|
| Blank | Nothing |
| Amplify | +50% damage (Heal: +50% healing; Blink: +50% distance) |
| Overload | +100% damage, cooldown doubled |
| Split | Projectiles and rays: 3 in a fan at 60% each. Novas: 3 smaller novas around you. Meteor: 3 smaller meteors around the target. Heal / Blink: twice at 60% |
| Pierce | Projectiles pass through enemies; rays hit the next enemy too (Lightning also +1 chain); novas and meteors chain to 1 more enemy; Blink hurts enemies along its path |
| Wide | +50% area and projectile size (Blink: +50% distance; Heal: also slows enemies around you) |
| Echo | The spell goes off again 0.5 s later |
| Quicken | This cast has no cooldown |
| Burning | Hits burn enemies (self spells: burn enemies around you) |
| Frozen | Hits slow enemies by 50% for 2 s (self spells: around you) |
| Fumble (curse) | The spell fizzles; no cooldown spent |

Extra copies from Split, Pierce and Echo never lifesteal, so lifesteal balance is unchanged.

**Debug (dev mode on, F4; the game pauses):** G turns the Grimoire on or off, 1-9 / 0 / - add a card
as Next (Shift: into the discard pile), C clears the deck, X reshuffles, P switches between starting
decks (presets in `GrimoireConfig.cs`), B changes which cards the bolt uses, L shows the cast log
("Cast Fireball + Split").

**Add a card:** add one entry to `Cards.All` in `Grimoire/Cards.cs` (id, name, description, symbol,
colour, category) with an `Apply` function that edits the cast (`CardContext`: change the copies'
`CastMods`, the cooldown, add an echo or a fizzle). It then appears in rewards automatically; add it
to `GrimoireConfig.StartingDeck` or a preset to start with it. All numbers are in
`Grimoire/GrimoireConfig.cs`.

## How it's built (and how to add things)

**Saves.** `Core/Save/`: one slot, `save.json` in `%USERPROFILE%\AppData\LocalLow\SpellcasterMVP\Spellcaster`
(the self-test uses an `autotest` folder next to it). The file is a header (`saveVersion`, game
version, time, run seed) plus one JSON block per system. Writes go to a temp file that then replaces
the save; the previous save is kept as `save.json.bak`. A damaged file falls back to the backup; if
both are damaged, Continue is hidden and the menu says why. Settings and unlocks are not in it (they
stay in PlayerPrefs). Everything the map decides is rolled from the run seed (`RunRandom`), so it
needs no other random state. Dev mode keys: **F5** save now, **F9** load the save, **F8** delete it,
**F7** print it to the log and open its folder.

**Add a system to the save:** write a plain `[Serializable]` data class (lists and numbers, stable
string ids, no engine objects), give the system a `SaveState()` that fills it and a
`LoadState(data)` that puts it back, and register it once when the system is created:

```csharp
SaveSystem.Register(new SaveSection<MyData>("mysystem", 50, SaveState, LoadState));
```

The key must never change; the number is the load order (the dungeon loads at 0, the run at 10,
spells 20, the player 30, the Grimoire 40). Return null from `SaveState()` to leave the block out. If
you change a saved format, bump `SaveSystem.Version` and add a migration from the old version in
`SaveSystem.Migrations`.

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
