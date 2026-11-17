# Spellcaster content list (v0.7)

Everything currently in the game, read from the code. **Locked** means it only appears in runs after
you earn the achievement shown (see `Assets/Scripts/Core/Progress.cs`).

Totals: 12 spells (6 locked at first), 12 runes, 28 items (13 locked at first), 3 pickups.

## Spells and their runes

Each spell has one rune: a sequence you draw with the mouse. A rune pickup costs 25 gold in shops.
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

## Pickups

| Pickup | Effect | Price |
|---|---|---|
| Gold | Worth 1 gold | – |
| Gold pile | Worth 5 gold | – |
| Heart | Restores 20 HP | 6 |
