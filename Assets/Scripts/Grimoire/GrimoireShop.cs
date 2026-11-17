using System.Collections.Generic;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// The Scroll of Erasure: a consumable sold only in shops, only while the Grimoire is on. Using it
    /// opens the removal list (no gold cost); backing out gives the scroll back. It is not in the normal
    /// loot tables, so drops and shop stock are unchanged when the Grimoire is off.
    /// </summary>
    public static class GrimoireShop
    {
        public static readonly ItemDef Erasure = new ItemDef
        {
            Id = "grimoire_erasure", Name = "Scroll of Erasure", Desc = "Remove a card from your Grimoire",
            Kind = ItemKind.Consumable, Shape = "scroll", Color = new Color(0.75f, 0.55f, 1f), Price = C.ErasurePrice,
            Use = UseErasure,
        };

        static readonly HashSet<Room> stocked = new HashSet<Room>();
        static Dungeon stockedFor;

        /// <summary>Puts one Scroll of Erasure in every shop of the floor (called while the Grimoire is active).</summary>
        public static void StockShops()
        {
            var d = Dungeon.Instance;
            if (!d || !C.ErasureInShops) return;
            if (stockedFor != d) { stocked.Clear(); stockedFor = d; }
            foreach (var room in d.AllRooms)
            {
                if (room.Type != RoomType.Shop || stocked.Contains(room) || room.Root == null) continue;
                stocked.Add(room);
                // In front of the shop's row of pedestals.
                Vector3 spot = d.FindFreeSpot(room, room.Center + new Vector3(0f, 0f, 0.5f), 1.3f);
                int price = Mathf.RoundToInt(C.ErasurePrice * (1f + 0.15f * (d.Floor - 1)));
                d.Pedestal(room, spot - room.Center, Erasure, price, Erasure.Color);
            }
            stocked.RemoveWhere(r => r.Root == null); // rooms of a finished floor
        }

        public static List<string> StockedRoomIds()
        {
            var ids = new List<string>();
            foreach (var r in stocked) if (r != null) ids.Add(r.Id);
            ids.Sort();
            return ids;
        }

        /// <summary>Loading a save: these shops already got their scroll (it is a saved pickup, or was bought).</summary>
        public static void MarkStocked(IEnumerable<string> roomIds)
        {
            var d = Dungeon.Instance;
            stocked.Clear();
            stockedFor = d;
            if (!d) return;
            var set = new HashSet<string>(roomIds);
            foreach (var r in d.AllRooms) if (set.Contains(r.Id)) stocked.Add(r);
        }

        static bool UseErasure(Run run)
        {
            var g = GrimoireRun.Instance;
            if (g == null || !g.Active || g.Deck == null)
            {
                if (SpellSystem.Instance) SpellSystem.Instance.Flash("The Grimoire is off");
                return false;
            }
            if (g.Rewards.IsOpen) return false;
            bool opened = g.Rewards.OpenFreeRemoval(g, null, () =>
            {
                // Backed out without removing: the scroll goes back into a free slot.
                int slot = run.FreeSlot();
                if (slot >= 0) run.Consumables[slot] = Erasure;
            });
            if (!opened && SpellSystem.Instance) SpellSystem.Instance.Flash($"The deck can't go below {C.MinDeckSize} cards");
            return opened;
        }
    }
}
