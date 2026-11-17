using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// Debug panel (F4 while dev mode is on; the game pauses). Keys:
    /// G on/off, 1-9 / 0 / - add that card as Next (Shift: into the discard pile), C clear the deck,
    /// X reshuffle, P next starting-deck preset, B bolt card rule, L on-screen log.
    /// </summary>
    public static class GrimoireDebug
    {
        static readonly KeyCode[] CardKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals,
        };

        public static string KeyName(int i) => i < 9 ? (i + 1).ToString() : i == 9 ? "0" : i == 10 ? "-" : "=";

        public static void HandleInput(GrimoireRun g)
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                Settings.SetGrimoireEnabled(!Settings.GrimoireEnabled);
                g.AddLog("Grimoire " + (Settings.GrimoireEnabled ? "ON" : "OFF"));
            }
            if (Input.GetKeyDown(KeyCode.L)) g.ShowLog = !g.ShowLog;
            if (Input.GetKeyDown(KeyCode.B))
            {
                C.BoltUse = (BoltCardUse)(((int)C.BoltUse + 1) % 3);
                g.AddLog("Bolt uses: " + C.BoltUse);
            }
            if (!g.Active || g.Deck == null) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            for (int i = 0; i < CardKeys.Length && i < Cards.All.Count; i++)
            {
                if (!Input.GetKeyDown(CardKeys[i])) continue;
                var def = Cards.All[i];
                if (shift) g.Deck.AddToDiscard(def); else g.Deck.AddToHand(def);
                g.AddLog($"Added {def.Name} to {(shift ? "discard" : "hand")}");
            }
            if (Input.GetKeyDown(KeyCode.C)) { g.Deck.Clear(); g.AddLog("Deck cleared"); }
            if (Input.GetKeyDown(KeyCode.X)) { g.Deck.ForceReshuffle(); g.AddLog("Reshuffled"); }
            if (Input.GetKeyDown(KeyCode.P)) g.NextPreset();
        }
    }
}
