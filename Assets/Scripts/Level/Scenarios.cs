using System.Collections.Generic;
using System.Linq;

namespace Spellcaster
{
    /// <summary>One scenario: a floor's look (palette, structures, hazards, decor), its own enemies and its boss.</summary>
    public class ScenarioDef
    {
        /// <summary>The theme id the builders use (FloorThemes / RoomLayouts / EnemyType.Roster).</summary>
        public int Theme;
        public string Name;
        public EnemyType Boss;
    }

    /// <summary>
    /// The dungeon is endless. Floors 1-3 are the classic order (Crypt, Frozen Caverns, Forge, each with
    /// its boss). From floor 4 on, each floor's scenario is picked from the run seed (never the same as
    /// the floor before), its boss from all bosses (never the one before), and its enemies mix the
    /// scenario's own roster with a couple of guests from other scenarios. The same seed always gives the
    /// same floors, so saves and co-op agree.
    ///
    /// To add a scenario: add an entry to <see cref="All"/> with a new theme id, then give that id a
    /// palette, structures, hazards, decor and ambient in FloorThemes and a roster in EnemyType.Roster.
    /// It joins the rotation by itself.
    /// </summary>
    public static class Scenarios
    {
        public static readonly List<ScenarioDef> All = new List<ScenarioDef>
        {
            new ScenarioDef { Theme = 1, Name = "The Crypt", Boss = EnemyType.Boss },
            new ScenarioDef { Theme = 2, Name = "Frozen Caverns", Boss = EnemyType.Burrower },
            new ScenarioDef { Theme = 3, Name = "The Forge", Boss = EnemyType.Behemoth },
        };

        /// <summary>Floors that always come in the classic order (the old "three floors and out").</summary>
        public const int ClassicFloors = 3;
        /// <summary>From this floor on, rosters get guests from other scenarios.</summary>
        public const int GuestsFrom = ClassicFloors + 1;
        /// <summary>Guest enemy types per floor, and how often they come compared to a scenario's own.</summary>
        public const int GuestTypes = 2;
        public const float GuestWeight = 0.3f;

        static System.Random Rng(string what, int floor) => new System.Random(RunRandom.Derive(RunRandom.RunSeed, what, floor));

        public static ScenarioDef ById(int theme) => All.FirstOrDefault(s => s.Theme == theme) ?? All[0];

        /// <summary>The scenario (theme id) of a floor.</summary>
        public static int ThemeFor(int floor)
        {
            if (floor <= ClassicFloors) return All[(System.Math.Max(floor, 1) - 1) % All.Count].Theme;
            int prev = ThemeFor(floor - 1);
            var options = All.Where(s => s.Theme != prev).ToList();
            if (options.Count == 0) return prev;
            return options[Rng("scenario", floor).Next(options.Count)].Theme;
        }

        public static string NameFor(int floor) => ById(ThemeFor(floor)).Name;

        /// <summary>The boss at the end of a floor: the classic one on floors 1-3, then any boss but the last.</summary>
        public static EnemyType BossFor(int floor)
        {
            if (floor <= ClassicFloors) return ById(ThemeFor(floor)).Boss;
            var prev = BossFor(floor - 1);
            var bosses = All.Select(s => s.Boss).Distinct().Where(b => b != prev).ToList();
            if (bosses.Count == 0) return prev;
            return bosses[Rng("boss", floor).Next(bosses.Count)];
        }

        /// <summary>A floor's enemy mix: its scenario's roster, plus a couple of guests from other scenarios after floor 3.</summary>
        public static (EnemyType type, float w)[] Roster(int floor, int depth)
        {
            int theme = ThemeFor(floor);
            var own = EnemyType.Roster(theme, depth);
            if (floor < GuestsFrom) return own;
            var ownTypes = new HashSet<EnemyType>(own.Select(o => o.type));
            var others = All.Where(s => s.Theme != theme)
                .SelectMany(s => EnemyType.Roster(s.Theme, depth))
                .Where(o => o.w > 0f && !ownTypes.Contains(o.type) && o.type.MaxPerRoom == 0)
                .Select(o => o.type).Distinct().OrderBy(t => t.Name).ToList();
            var rng = Rng("guests", floor);
            var list = own.ToList();
            for (int i = 0; i < GuestTypes && others.Count > 0; i++)
            {
                int k = rng.Next(others.Count);
                list.Add((others[k], GuestWeight));
                others.RemoveAt(k);
            }
            return list.ToArray();
        }
    }
}
