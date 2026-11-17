# Spellcaster content list (v0.13)

Everything currently in the game, read from the code. **Locked** means it only appears in runs after
you earn the achievement shown (see `Assets/Scripts/Core/Progress.cs`).

Totals: 12 spells (6 locked at first), 12 runes, 29 items (13 locked at first, 1 Grimoire only),
3 pickups, 11 Grimoire cards, 3 scenarios rotating in an endless dungeon, 13 enemies and 3 bosses, 2 casting styles,
online co-op for 2-6 players.

## Spells and their runes

Each spell has one rune: a sequence of arrows you draw with the mouse (Classic) or type with W A S D (Momentum, see [Casting styles](#casting-styles)). A rune pickup costs 25 gold in shops.
Every run starts knowing only Fireball. Defined in `Assets/Scripts/Spells/SpellBook.cs`.

| Spell | Rune | Cooldown | What it does | Unlock |
|---|---|---|---|---|
| Fireball | ↓ → ↑ | 3 s | Projectile, 40 fire damage, 3.5 m explosion | Known from the start |
| Lightning | → ↓ → | 6 s | Instant hit (45 m range), 45 storm damage, chains to 2 more enemies | Always available |
| Frost Nova | ↓ ← ↑ → | 10 s | 6.5 m burst around you, 8 damage, freezes for 3 s | Always available |
| Heal | ↑ ↓ ↑ ↓ | 15 s | Heals you 25 HP | Always available |
| Frost Shard | ← ↑ → | 2.5 s | Fast projectile, 22 frost damage, freezes for 1.2 s | Always available |
| Thunder Ring | ↑ → ↓ | 8 s | 7 m burst around you, 25 storm damage, knockback | Always available |
| Poison Cloud | ← ↓ → | 7 s | Projectile, 10 damage plus poison, leaves a 4 m cloud for 5 s | Locked: clear 10 rooms |
| Arcane Missiles | → ↑ → | 4 s | 5 homing missiles, 14 arcane damage each | Locked: defeat 100 enemies |
| Blink | ↑ ← ↑ | 5 s | Teleports you 8 m | Locked: clear a room without getting hurt |
| Quake | ↓ ← ↓ | 9 s | 8 m burst around you, 20 earth damage, stuns for 2.5 s | Locked: defeat a boss |
| Life Drain | ← ↑ ← | 5 s | Instant hit (35 m range), 30 damage, heals you 35% of it | Locked: heal 300 HP in total |
| Meteor | → ↓ ← | 14 s | Falls on a target up to 40 m away, 60 fire damage, 6 m explosion | Locked: reach floor 3 |

### Other ways to get spells

- **Forged spells:** fusing two spells in the Forge makes a new one. Some element pairs add a bonus
  effect (`Assets/Scripts/Spells/SpellSynergies.cs`):

  | Elements fused | Bonus |
  |---|---|
  | Fire + Frost | Shatter |
  | Fire + Storm | Ignite |
  | Frost + Storm | Thunderclap |
  | Life + any damage element | Lifesteal |
  | Fire + Poison | Combustion |
  | Earth + Storm | Tremor |

- **Forgotten runes:** forgetting a spell in the Forge drops a rune that teaches back that exact
  spell, including forged ones.

## Passive items (stat boosts)

Defined in `Assets/Scripts/Loot/Items.cs`.

| Item | Effect | Price | Unlock |
|---|---|---|---|
| Ruby Heart | +25 max HP | 25 | Always available |
| Swift Boots | +15% move speed | 20 | Always available |
| Arcane Focus | +20% spell power | 30 | Always available |
| Hourglass | Spell cooldowns 15% shorter | 30 | Always available |
| Sharpened Wand | Magic bolt +5 damage | 20 | Always available |
| Quickcast Ring | Magic bolt fires 30% faster | 20 | Always available |
| Iron Robe | Take 15% less damage | 25 | Always available |
| Lucky Clover | Enemies and crates drop more loot | 15 | Always available |
| Vampire Fang | Heal 2 HP per kill | 25 | Always available |
| Glass Cannon | +40% spell power, −25 max HP | 20 | Always available |
| Thorned Robe | Enemies that hit you up close take 15 damage | 20 | Locked: die once |
| Phoenix Feather | Survive one lethal hit with half your HP | 40 | Locked: die 5 times |
| Gold Tooth | Every coin is worth 1 more gold | 15 | Locked: collect 300 gold in total |

## Relics (change how spells behave)

| Relic | Effect | Price | Unlock |
|---|---|---|---|
| Splitting Prism | Projectile spells fire 2 extra projectiles | 35 | Always available |
| Conductive Coil | Chains jump to 2 more enemies | 30 | Always available |
| Volatile Soul | Enemies killed by spells explode | 35 | Always available |
| Permafrost Shard | Frozen enemies that die freeze their neighbours | 30 | Always available |
| Ember Brand | Every spell hit sets enemies burning | 35 | Always available |
| Echo Stone | 25% chance a spell casts twice | 40 | Always available |
| Leech Sigil | Spells heal you 8% of their damage | 30 | Always available |
| Blast Core | Explosions and novas are 40% bigger | 30 | Always available |
| Frostbite Wand | Magic bolts have a 20% chance to freeze | 25 | Always available |
| Bolt Splitter | The magic bolt fires 3 bolts | 30 | Locked: defeat 50 enemies with the magic bolt |
| Toxic Vial | Every spell hit poisons enemies | 30 | Locked: defeat 30 enemies with poison |
| Storm Crown | Every 5th spell has no cooldown | 35 | Locked: cast 150 spells |
| Overcharge | A spell kept ready for 1.5 s hits 50% harder | 30 | Locked: defeat 3 enemies with one spell |
| Frozen Heart | Getting hurt freezes enemies around you | 30 | Locked: freeze 100 enemies |
| Hex Mark | Spells deal 30% more to burning or poisoned enemies | 30 | Locked: forge a spell |

## Consumables (used from slots 1–3)

| Item | Effect | Price | Unlock |
|---|---|---|---|
| Health Potion | Restores 35 HP | 8 | Always available |
| Fire Bomb | Thrown; explodes for 60 damage | 10 | Always available |
| Frost Scroll | Freezes every enemy within 16 m for 4 s | 12 | Always available |
| Haste Elixir | For 10 s: faster movement, half cooldowns | 12 | Always available |
| Scroll of Renewal | Resets all spell cooldowns | 10 | Always available |
| Smoke Bomb | Stuns every enemy within 14 m for 3 s | 10 | Locked: use 10 items |
| Time Scroll | Enemies move and attack at half speed for 6 s | 12 | Locked: buy 5 items in shops |
| Rune Stone | Teaches a random spell you don't know | 20 | Locked: learn 8 spells from runes |
| Scroll of Erasure | Removes a card of your choice from your Grimoire | 35 (+15% per floor) | Only in shops while the Grimoire is on (one per shop) |

## Pickups

| Pickup | Effect | Price |
|---|---|---|
| Gold | Worth 1 gold | – |
| Gold pile | Worth 5 gold | – |
| Heart | Restores 20 HP | 6 |

## Floors (endless)

Three scenarios, each with its own look, room layouts, hazards, enemies and boss. Defined in
`Assets/Scripts/Level/Scenarios.cs`, `Assets/Scripts/Level/FloorThemes.cs`, `Assets/Scripts/Level/Hazards.cs`
and `Assets/Scripts/Enemies/EnemyTypesFloors.cs`.

**The dungeon is endless.** Floors 1-3 always come in the order below; the floor 3 portal counts as a win
and leads deeper. From floor 4 on:

| Rotates | How |
|---|---|
| Scenario | Picked from the run's seed each floor, never the same as the floor before |
| Boss | Any of the three bosses, never the same as the floor before |
| Enemies | The scenario's own roster plus 2 guest types from other scenarios (rarer, 0.3 weight) |
| Difficulty | Floors 2-3: enemy HP +40% and damage +25% per floor. From floor 4: HP +25% and damage +12% per floor (damage capped at x3); waves grow until 22 enemies |
| Rewards | From floor 4: coin drops +15% per deep floor, 2 boss reward items, and a 25% heal on arrival |
| Record | The death screen and the main menu show your deepest floor |

The same seed always gives the same floors (saves and co-op agree).

| Floor | Name | Enemies | Hazards and props | Boss |
|---|---|---|---|---|
| 1 | The Crypt | Imp, Cultist, Hornet, Shade (deeper rooms only) | Spike traps, barrels, crates | The Watcher |
| 2 | Frozen Caverns | Frost Slug, Gloom Moth, Cave Frog, Shade, Cultist, Imp | Ice patches (you slide), ceiling pods (a tentacle drops on you), frost crystals (burst when broken: freeze enemies, chill you) | The Burrower |
| 3 | The Forge | Cinder Crawler, Forge Sentry, Flame Skull, Horned Beetle (max 2 per room), Imp, Mimic (max 1 per room) | Lava pools, fire jets (a vent glows, then shoots flame) | The Behemoth |

Hazards stop once a room's fight is over. Flying enemies ignore spike traps.

## Enemies

Defined in `Assets/Scripts/Enemies/EnemyType.cs` and `Assets/Scripts/Enemies/EnemyTypesFloors.cs`.

| Enemy | Floor | HP | What it does |
|---|---|---|---|
| Imp | 1, 2, 3 | 30 | Runs at you and claws (8 damage) |
| Cultist | 1, 2 | 60 | Flying eye; keeps its distance and shoots bolts (10) |
| Hornet | 1 | 34 | Flying; fires bursts of 3 stingers (5 each) |
| Shade | 1, 2 | 55 | Splits into 2 Shadelings (16 HP) when it dies |
| Frost Slug | 2 | 85 | Slow and tough; heavy bite (14) |
| Gloom Moth | 2 | 34 | Flying; spits 2 frost shots (7 each) |
| Cave Frog | 2 | 40 | Crouches, then leaps at you (12); bites up close |
| Cinder Crawler | 3 | 50 | Throws 3 burning spikes in a fan (8 each) |
| Forge Sentry | 3 | 45 | Small robot; fires bursts of 3 shots (6 each) |
| Flame Skull | 3 | 22 | Flies at you and explodes (18 in 3.2 m), also when killed |
| Horned Beetle | 3 | 140 | Braces and charges across the room (20); ramming a wall dazes it |
| Mimic | 3 | 90 | Sits still as a treasure chest until you get close or hit it, then bites (16) |

### Bosses

| Boss | Floor | HP | What it does |
|---|---|---|---|
| The Watcher | 1 | 650 | Giant flying eye; fires fans of 5 bolts and summons imps |
| The Burrower | 2 | 600 | Dives underground, erupts under you, spits frost and calls slugs and moths |
| The Behemoth | 3 | 700 | Rolls across the arena leaving burning ground, fires spikes, leaps and slams down; tires after a crash |

## Grimoire cards (experimental, off by default)

Turn it on in **Options > Grimoire cards**. You hold a hand of 3 cards; each rune spell you cast uses
the **Next** card, which changes that cast. The magic bolt only uses a Blank card. **Q** throws the
Next card away unused (1 s before you can cast again), but Blank and curse cards can't be thrown away.
Cleared rooms (and bosses) offer a choice of 3 new cards; for 50 gold you can remove one card instead
(the deck never goes below 4). Defined in `Assets/Scripts/Grimoire/Cards.cs` and
`Assets/Scripts/Grimoire/GrimoireConfig.cs`.

Starting deck: 5 Blank, Amplify, Split, Echo.

| Card | Type | Effect |
|---|---|---|
| Blank | Neutral | No effect |
| Amplify | Power | +50% damage (Heal: +50% healing) |
| Overload | Power | +100% damage, cooldown doubled |
| Split | Shape | 3 shots in a fan, or 3 smaller areas, at 60% each |
| Pierce | Shape | Shots pass through enemies; areas chain to 1 more enemy |
| Wide | Shape | +50% area and projectile size |
| Echo | Tempo | Casts the spell again 0.5 s later |
| Quicken | Tempo | No cooldown for this cast |
| Burning | Element | Hits burn enemies (6 damage per second for 3 s) |
| Frozen | Element | Hits slow enemies by 50% for 2 s |
| Fumble | Curse | The spell fizzles (no cooldown spent) |

- **Elites:** while the Grimoire is on, a fight room has a 15% chance of one elite enemy (double HP,
  marked ELITE). Killing it adds a Fumble curse to your discard pile.
- **Scroll of Erasure:** a shop item that removes a card for free (see Consumables).

## Casting styles

Pick one in **Options > Casting style**. Defined in `Assets/Scripts/Spells/SpellSystem.cs`, tunables in
`Assets/Scripts/Player/MomentumConfig.cs`.

| Style | Hold the cast key (Right Mouse or Ctrl) and... |
|---|---|
| Classic (default) | The view stops, the mouse draws the rune, W A S D walk at 30% speed |
| Momentum | You keep running the way you were moving (the mouse steers) and W A S D or the arrow keys type the rune. Standing still when you press the key keeps you in place. Optional field-of-view and head-bob cue |

## Run features

- **Saving:** the run saves every time you clear a fight room. **Continue** on the main menu resumes
  it. Dying or winning deletes the save.
- **Pause menu (Esc):** Resume, Options and Save & quit. Save & quit saves where you are outside a
  fight; in a fight it restarts from the last cleared room.
- **Seeds:** **Seeded run** on the main menu takes a number or any word. The same seed builds the same
  dungeon, shops, enemy waves and rewards. The pause and end screens show the seed (**C** copies it).

## Online co-op (experimental)

2 to 6 players through Steam (App ID 3965800). The host's PC runs the game; the others connect to it.
Code in `Assets/Scripts/Net/`; every tunable is in `NetConfig.cs` and `NetDowned.cs`.

### Lobby

| Setting / action | What it does |
|---|---|
| Host a game | Max players 2-6; who can join: friends only, public, or invite only |
| Join a game | Friends' games and public Spellcaster lobbies; Steam invites and "Join game" in the friends list (also while the game is closed) |
| Lobby screen | Steam names and avatars, Ready / Not ready, the host can remove a player, **Start run** once everyone is ready |
| Run (host) | **New run** or **Continue saved** (when a save exists) |
| Refused joins | Another game version, a full lobby, a run already in progress (only players coming back get in) |
| Direct IP (debug) | With Dev mode on: connect copies of the game by address, no Steam needed |

### How a co-op run plays

| Topic | Rule |
|---|---|
| Players | A wizard in their own colour (6 colours by lobby slot), with name tag and health bar; your spell glow uses your colour |
| Movement | Yours is instant (the host checks it and corrects it if needed); the others move smoothly |
| Casting | Classic and Momentum both work; the rune is drawn on your own PC, the host checks the spell is known and off cooldown, then casts it as you. Friendly fire is off |
| Grimoire | The host's Options setting decides for everyone; each player has their own deck, cycles and picks their own rewards. An elite's curse falls on the whole party |
| Loot | Instanced: every coin, heart, item, rune and shop item has one copy per player |
| Your run | Gold, items, consumables, stats, spell book and Forge are your own (held by the host) |
| Fight rooms | The first player in starts a 3 s "Fight! Gather up" countdown; then everyone outside is pulled in and the doors close |
| Portal | Opens once every living player stands on it; the whole party moves to the next floor |
| Pause | The pause menu doesn't stop the game in co-op |

### Difficulty by player count

Enemy HP and wave size (bosses: HP only), from `NetConfig.Scaling`:

| Players | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| Enemy HP | x1 | x1.4 | x1.75 | x2.1 | x2.4 | x2.7 |
| Enemies per wave | x1 | x1.35 | x1.7 | x2.0 | x2.3 | x2.6 |

### Downed and revive

| Rule | Value |
|---|---|
| At 0 HP (after a Phoenix Feather) | Downed: can't act, enemies ignore you |
| Revive | A teammate within 2 m for 3 s; you get up with 35% HP |
| Bleed out | After 30 s: you spectate a teammate (click to switch) |
| Back in | When the room is cleared: next to a teammate with 35% HP |
| Run over | Only when every player is down at the same time |

### Leaving, rejoining and saving

- A player who leaves mid-run is removed; the host keeps their character by SteamID (HP, gold, items,
  consumables, spells, deck, loot taken). Rejoining the same run gives it back.
- If the host leaves, the session ends for everyone.
- Only the host saves; the save keeps every player's character by SteamID. On **Continue saved**,
  returning players get theirs back and new players start fresh.

### Debug tools (dev mode)

| Key / option | What it does |
|---|---|
| F6 | Network overlay: role, tick rate (30 Hz), traffic, ping and packet loss per player, prediction corrections |
| Ctrl+F6 | Simulated lag and loss: off / 150 ms 2% / 300 ms 5% |
| `-netlag 150 -netloss 2` | The same at launch |
| `-netkey NAME` | Give a copy its own identity (several copies on one Steam account) |
