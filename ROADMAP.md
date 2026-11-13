# Spellcaster roadmap

Next steps after v0.6. Tick items off as they're done.

## Check v0.6
- [ ] Play a full run by hand. The self-test covers each enemy alone, not mixed fights in big rooms.
- [ ] Collect playtester feedback on Flame Skull damage (18) and how often beetles show up.
- [ ] Try a build without the Asset Store pack. The recoloured fallback art for the new enemies has never been tested.
- [ ] Watch 2×2 arenas on floor 3. With up to 22 enemies, skull chain explosions and beetle charges may stack badly.

## Balance (once feedback is in)
- [ ] Tune Flame Skull damage, speed and windup, and how often they appear.
- [ ] Scale the beetle cap to room size: 1 in small rooms, 2 in big ones.
- [ ] Check whether Hornet bursts plus cultist flanking make floor 1 too hard.

## Polish
- [ ] Give the new enemies their own sounds. They currently reuse generic ones like "boss" and "claw".
- [ ] Add a bestiary screen in the menu that shows enemies you've met and how they behave.
- [ ] Add a `.gitattributes` file to stop the line-ending (CRLF) warnings on every commit.

## Content
- [ ] Add more bosses, so each floor's boss is different.
- [ ] Add elite variants of regular enemies (more health, a special trait) on deeper floors.
- [ ] Give each floor its own look and its own enemy mix.
- [ ] Add more spells and items to unlock, so meta-progression keeps up with the harder enemies.

## Release process
- [ ] Make one release script that bumps the version, builds, runs the self-test, packages and runs `gh release create`.
- [ ] Have the game write a log or crash file that playtesters can send along with bug reports.
