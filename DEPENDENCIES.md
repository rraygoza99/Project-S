# Dependencies

What Spellcaster is built with and the third-party content it uses. Most of the game (sprites,
textures, levels, sounds, UI) is generated from code, so the list is short.

## Engine

| Dependency | Version | Notes |
|---|---|---|
| Unity | 6000.3.5f1 (Unity 6) | Built-in render pipeline. The UI is IMGUI (`OnGUI`). |

## Third-party assets (Unity Asset Store)

These packs are **optional**. Without them, the game uses its own procedural pixel art, synth
sounds and drawn lightning. `tools/import-assets.ps1` copies only the needed files into
`Assets/ThirdParty/`.

That folder is **gitignored** because Asset Store content may not be redistributed as source. It is
included in the built game (release zip), which the Asset Store license allows.

| Pack | Publisher | Used for | Files in `Assets/ThirdParty/` | Code that loads it |
|---|---|---|---|---|
| Pixel Monsters Vol.2 | Ismartal | Enemy sprite sheets: Imp, Watcher (ranged enemy and boss), Flame Skull, Horned Beetle, Hornet, Shade; floor 2 slug, moth, frog, ceiling pod and The Burrower; floor 3 crawler, sentry, mimic and The Behemoth | `Resources/Monsters/*.png` | `Rendering/Sprites.cs`, `Rendering/SpritesFloors.cs` |
| SpellBook Megapack | Rexard | Spell icons: fireball, lightning, frost, heal, burn, shatter. The other spells use procedural rune icons. | `Resources/SpellIcons/*.png` | `Spells/SpellDefinition.cs` |
| Monster Sounds Pack | Cafofo | Enemy hit, death and attack sounds (imp and watcher sets) | `Resources/MonsterSounds/*.wav` | `Audio/Sfx.cs`, `Enemies/EnemyType.cs` |
| Lightning Bolt Effect | Digital Ruby | Lightning bolt visuals | `LightningBolt/LightningBoltScript.cs`, `Resources/Lightning/bolt.png` | `Combat/LightningFx.cs`, through reflection so the game still compiles without it |

## Open-source libraries (in the repo)

| Library | Version | License | Where | Used for |
|---|---|---|---|---|
| Mirror | 96.11.3 | MIT | `Assets/Mirror/` (examples removed) | Networking for online co-op: the listen server, messages, the KCP transport used by the debug direct-IP mode |
| Steamworks.NET | 2025.165.0 | MIT (includes Valve's redistributable `steam_api64.dll`) | `Assets/com.rlabrecque.steamworks.net/` | Steam: sign-in, lobbies, invites, avatars, Steam Networking Sockets |

Mirror's import added `com.unity.nuget.newtonsoft-json` (3.2.2) to the manifest and the `MIRROR...` scripting
defines. The Steam transport is the game's own (`Net/SteamSocketsTransport.cs`). The build writes
`steam_appid.txt` (App ID 3965800, from `Net/NetConfig.cs`) next to the exe.

## Unity packages (`Packages/manifest.json`)

### Used

| Package | Version | Why |
|---|---|---|
| Built-in modules (`com.unity.modules.*`) | 1.0.0 | Physics, audio, IMGUI, image conversion, screen capture and the rest of the core engine |
| `com.unity.ide.visualstudio` | 2.0.26 | Editor integration only (not in builds) |
| `com.unity.ide.rider` | 3.0.38 | Editor integration only (not in builds) |

### Installed but not used by the game's code

These came with the project template. No script in `Assets/Scripts` or `Assets/Editor` references
them. They could be removed in a cleanup, after a build and self-test to confirm nothing breaks.

| Package | Version |
|---|---|
| `com.unity.ads` | 4.16.4 |
| `com.unity.purchasing` | 4.14.2 |
| `com.unity.analytics` | 3.8.2 |
| `com.unity.ai.navigation` | 2.0.9 |
| `com.unity.collab-proxy` | 2.11.2 |
| `com.unity.multiplayer.center` | 1.0.1 |
| `com.unity.test-framework` | 1.6.0 |
| `com.unity.timeline` | 1.8.10 |
| `com.unity.ugui` | 2.0.0 |
| `com.unity.2d.sprite` | 1.0.0 |
| `com.unity.2d.tilemap` | 1.0.0 |
| `com.unity.xr.legacyinputhelpers` | 2.1.13 |

`Assets/MobileDependencyResolver/` was added by the Ads package and is not used either.

## Build and release tools (developers only)

| Tool | Used by |
|---|---|
| Windows PowerShell 5.1 | `play.ps1`, `Release/package.ps1`, `tools/import-assets.ps1` |
| GitHub CLI (`gh`) | Publishing playtest releases |
