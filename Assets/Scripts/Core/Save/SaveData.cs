using System;
using System.Collections.Generic;

namespace Spellcaster
{
    // Plain data for each system's save section (no engine objects). Ids are stable strings:
    // rooms "r_x_y", items ItemDef.Id, spells SpellData, cards CardDef.Id.

    /// <summary>Dungeon.SaveState: the floor, every room's state, what lies on the floor and what was broken.</summary>
    [Serializable]
    public class DungeonSave
    {
        public int floor, floorSeed, roomsCleared;
        /// <summary>Last cleared room: Continue puts you here.</summary>
        public string checkpoint;
        public bool portalOpen;
        public List<RoomSave> rooms = new List<RoomSave>();
        public List<PickupSave> pickups = new List<PickupSave>();
        public List<PropSave> destroyed = new List<PropSave>();
        public List<string> floorStock = new List<string>();
    }

    [Serializable]
    public class RoomSave
    {
        public string id;
        /// <summary>Visited = shown on the map.</summary>
        public bool visited, cleared;
    }

    [Serializable]
    public class PickupSave
    {
        public string item;
        public int price;
        public float x, y, z;
        /// <summary>For a dropped (forgotten) spell rune: the spell itself.</summary>
        public SpellData spell;
    }

    [Serializable]
    public class PropSave
    {
        public string kind;
        public float x, z;
    }

    /// <summary>Run.SaveState: gold, items, item slots, stats and run totals.</summary>
    [Serializable]
    public class RunSave
    {
        public int gold, goldEarned, kills;
        /// <summary>Arcane Essence (save version 2; older saves load these as 0).</summary>
        public int essence, forgesDone, essenceBadLuck, essenceRolls;
        public float runTime;
        public List<string> items = new List<string>();
        /// <summary>One entry per slot, "" when empty.</summary>
        public List<string> consumables = new List<string>();
        public PlayerStats stats = new PlayerStats();
        /// <summary>Achievements completed this run (reward ids), for the end screen.</summary>
        public List<string> newAchievements = new List<string>();
    }

    /// <summary>Player.SaveState.</summary>
    [Serializable]
    public class PlayerSave
    {
        public float hp;
    }

    /// <summary>SpellSystem.SaveState: the spell book in order, and the cast counter (Storm Crown).</summary>
    [Serializable]
    public class SpellsSave
    {
        public List<SpellData> known = new List<SpellData>();
        public int castCount;
    }
}
