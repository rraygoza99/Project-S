using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    /// <summary>
    /// The experimental Grimoire for one run (created with the rest of the game on every scene load, so
    /// it resets each run). Connects to the game only through hooks:
    /// SpellSystem.BeforeCast (play the Next card on a cast), Dungeon.RoomCleared (rewards),
    /// Enemy.Spawned / Killed (elites and their curse), GameState.ExtraPanelOpen (pause while a panel
    /// is open) and the HUD draw hooks. While Settings.GrimoireEnabled is off it hooks nothing that
    /// changes play, so the game behaves exactly as without it.
    /// </summary>
    public class GrimoireRun : MonoBehaviour
    {
        public static GrimoireRun Instance { get; private set; }

        public Deck Deck { get; private set; }
        public readonly GrimoireRewards Rewards = new GrimoireRewards();
        /// <summary>Debug panel (F4 in dev mode).</summary>
        public bool DebugOpen { get; set; }
        public bool ShowLog = true;
        public int PresetIndex { get; private set; }

        /// <summary>Recent events for the on-screen log ("Cast Fireball + Split").</summary>
        public readonly List<string> Log = new List<string>();
        public float LogTime { get; private set; } = -10f;
        public string LastCast { get; private set; }
        public CardDef LastCard { get; private set; }
        public int CardsPlayed { get; private set; }
        public int ElitesMade { get; private set; }

        /// <summary>Deck changes, for the HUD's animations.</summary>
        public event Action<Deck.Change, Card> DeckChanged;

        bool hooked;
        Room eliteRolledFor;

        /// <summary>On, in a real run (not the tutorial or the practice tool).</summary>
        public bool Active
        {
            get
            {
                var gs = GameState.Instance;
                // Multiplayer: the host's setting decides for everyone (each player has their own deck, held by the host).
                bool on = Net.NetPlayers.Running ? Net.NetGrimoire.Enabled : Settings.GrimoireEnabled && !GameState.Multiplayer;
                return on && gs && gs.InRun && !GameState.PracticeMode && !GameState.TutorialMode;
            }
        }

        void Awake()
        {
            Instance = this;
            Items.ExtraResolve = id => id == GrimoireShop.Erasure.Id ? GrimoireShop.Erasure : null;
            SaveSystem.Register(new SaveSection<GrimoireSave>("grimoire", 40, SaveState, LoadState));
        }

        /// <summary>The deck (pile by pile, in order) and which shops already stock a Scroll of Erasure. Null if there is no deck.</summary>
        public GrimoireSave SaveState()
        {
            if (Deck == null) return null;
            var s = new GrimoireSave { preset = PresetIndex, shops = GrimoireShop.StockedRoomIds() };
            foreach (var c in Deck.DrawPile) s.draw.Add(c.Def.Id);
            foreach (var c in Deck.Hand) s.hand.Add(c.Def.Id);
            foreach (var c in Deck.DiscardPile) s.discard.Add(c.Def.Id);
            return s;
        }

        public void LoadState(GrimoireSave s)
        {
            if (Deck != null) Deck.Changed -= OnDeckChanged;
            Deck = new Deck();
            Deck.Changed += OnDeckChanged;
            Deck.Restore(s.draw, s.hand, s.discard);
            PresetIndex = Mathf.Clamp(s.preset, 0, C.Presets.Count - 1);
            GrimoireShop.MarkStocked(s.shops);
        }

        void OnEnable()
        {
            Dungeon.RoomCleared += OnRoomCleared;
            Enemy.Spawned += OnSpawned;
            Enemy.Killed += OnKilled;
            GameState.ExtraPanelOpen = PanelOpen;
            HUD.DrawOverlay += GrimoireHUD.DrawOverlay;
            HUD.DrawPanels += GrimoireHUD.DrawPanels;
            HUD.DrawPauseExtra += GrimoireHUD.DrawPause;
        }

        void OnDisable()
        {
            Dungeon.RoomCleared -= OnRoomCleared;
            Enemy.Spawned -= OnSpawned;
            Enemy.Killed -= OnKilled;
            GameState.ExtraPanelOpen = null;
            HUD.DrawOverlay -= GrimoireHUD.DrawOverlay;
            HUD.DrawPanels -= GrimoireHUD.DrawPanels;
            HUD.DrawPauseExtra -= GrimoireHUD.DrawPause;
            Hook(false);
            if (Instance == this) Instance = null;
        }

        bool PanelOpen() => Rewards.IsOpen || DebugOpen;

        /// <summary>The cast hooks are only attached while the Grimoire is active.</summary>
        void Hook(bool on)
        {
            if (on == hooked) return;
            hooked = on;
            if (on)
            {
                SpellSystem.BeforeCast += OnBeforeCast;
                SpellSystem.GlowTint = Tint;
            }
            else
            {
                SpellSystem.BeforeCast -= OnBeforeCast;
                SpellSystem.GlowTint = null;
            }
        }

        void Update()
        {
            bool active = Active;
            Hook(active);
            if (active && Deck == null && !Net.NetGrimoire.ClientMirror) StartDeck(C.StartingDeck); // a client's deck comes from the host

            var gs = GameState.Instance;
            if (!Settings.DevMode || !gs || !gs.InRun) DebugOpen = false;
            else if (Input.GetKeyDown(KeyCode.F4) && !AutoTest.Active) DebugOpen = !DebugOpen;
            if (DebugOpen) GrimoireDebug.HandleInput(this);

            if (!active)
            {
                if (Rewards.IsOpen) Rewards.Close();
                return;
            }
            if (!Net.NetSession.IsClientOnly) GrimoireShop.StockShops(); // shops are the host's
            Rewards.Update(this);

            var player = Player.Instance;
            if (!AutoTest.Active && player && player.InputEnabled && gs.AcceptsGameplayInput && Input.GetKeyDown(C.CycleKey)) Cycle();
        }

        /// <summary>Multiplayer client: the host's copy of this player's deck.</summary>
        public void ApplyNetDeck(string[] draw, string[] hand, string[] discard)
        {
            if (Deck == null)
            {
                Deck = new Deck();
                Deck.Changed += OnDeckChanged;
            }
            Deck.Restore(draw, hand, discard);
        }

        /// <summary>Starts the deck over with these cards (run start, debug presets).</summary>
        public void StartDeck(IEnumerable<string> ids)
        {
            if (Deck != null) Deck.Changed -= OnDeckChanged;
            Deck = new Deck();
            Deck.Changed += OnDeckChanged;
            Deck.Build(ids);
            AddLog("New deck: " + Deck.Count + " cards");
        }

        public void NextPreset()
        {
            PresetIndex = (PresetIndex + 1) % C.Presets.Count;
            StartDeck(C.Presets[PresetIndex].cards);
            AddLog("Preset: " + C.Presets[PresetIndex].name);
        }

        void OnDeckChanged(Deck.Change change, Card card)
        {
            if (change == Deck.Change.Reshuffle && isActiveAndEnabled) StartCoroutine(ShuffleSound());
            DeckChanged?.Invoke(change, card);
        }

        IEnumerator ShuffleSound()
        {
            for (int i = 0; i < 5; i++)
            {
                Sfx.Play("tick", 0.45f, 1.5f + 0.12f * i);
                yield return new WaitForSecondsRealtime(0.05f);
            }
        }

        /// <summary>Why the Next card can't be cycled away, or null (Blank and curse cards must be played).</summary>
        public string CycleBlocked()
        {
            if (Deck == null || Deck.Next == null) return "No card";
            var d = Deck.Next.Def;
            if (d.IsCurse && !C.CycleCurses) return $"Can't discard a curse: {d.Name} must be played";
            if (d.Id == "blank" && !C.CycleBlank) return "Can't discard a Blank card";
            return null;
        }

        /// <summary>Discards the Next card unused and draws; casting is locked for a moment.</summary>
        public bool Cycle()
        {
            var why = CycleBlocked();
            if (why != null)
            {
                if (Deck != null && Deck.Next != null && SpellSystem.Instance) SpellSystem.Instance.Flash(why);
                Sfx.Play("fizzle", 0.4f, 1.3f);
                return false;
            }
            if (Net.NetGrimoire.ClientMirror)
            {
                // The host cycles this player's deck and sends it back.
                Net.NetGrimoire.SendCycle();
                if (SpellSystem.Instance) SpellSystem.Instance.CastLock = C.CycleLockout;
                Sfx.Play("tick", 0.6f, 0.75f);
                return true;
            }
            var c = Deck.CycleNext();
            if (SpellSystem.Instance) SpellSystem.Instance.CastLock = C.CycleLockout;
            Sfx.Play("tick", 0.6f, 0.75f);
            AddLog("Cycled " + c.Def.Name);
            return true;
        }

        // ---------------- hooks ----------------

        void OnBeforeCast(CastRequest req)
        {
            if (!Active || Deck == null) return;
            var next = Deck.Next;
            if (next == null) return;
            if (req.IsBolt)
            {
                if (C.BoltUse == BoltCardUse.None) return;
                if (C.BoltUse == BoltCardUse.BlankOnly && next.Def.Id != "blank") return;
            }
            Deck.PlayNext();
            CardsPlayed++;
            var ctx = new CardContext { Request = req, Spell = req.Spell, Shape = Cards.ShapeOf(req.Spell), Player = Player.Instance };
            next.Def.Apply?.Invoke(ctx);
            req.Label = (req.Spell != null ? req.Spell.Name : "Bolt") + " + " + next.Def.Name;
            LastCast = req.Label;
            LastCard = next.Def;
            if (!req.IsBolt || next.Def.Id != "blank") AddLog("Cast " + req.Label);
        }

        Color? Tint()
        {
            if (!Active || Deck == null || Deck.Next == null || Deck.Next.Def.Id == "blank") return null;
            return Deck.Next.Def.Color;
        }

        void OnRoomCleared(Room room)
        {
            if (!Active || Deck == null) return;
            if (room.Type == RoomType.Boss && !C.RewardOnBossRooms) return;
            Rewards.Open(this);
        }

        void OnSpawned(Enemy e)
        {
            if (!Active || !C.CursesEnabled || C.EliteChance <= 0f) return;
            var d = Dungeon.Instance;
            if (!d || !d.InCombat || d.Current == null || d.Current == eliteRolledFor) return;
            if (e.Type.IsBoss || !e.Type.DropsLoot) return;
            eliteRolledFor = d.Current; // one roll per fight room, on its first enemy (seeded per room)
            float roll;
            using (RunRandom.Use(RunRandom.RoomSeed(d.FloorSeed, d.Current, "elite"))) roll = UnityEngine.Random.value;
            if (roll >= C.EliteChance) return;
            MakeElite(e);
        }

        /// <summary>Turns an enemy into an elite (also a test hook).</summary>
        public void MakeElite(Enemy e)
        {
            e.MakeElite(C.EliteHpMult);
            ElitesMade++;
            AddLog("Elite " + e.Type.Name + " appeared");
        }

        void OnKilled(Enemy e)
        {
            if (!e.IsElite || !Active || !C.CursesEnabled || Deck == null) return;
            var def = Cards.Get(C.EliteCurse);
            if (def == null) return;
            Deck.AddToDiscard(def);
            AddLog("Curse added: " + def.Name);
            if (Run.Instance) Run.Instance.Toast("Cursed!", $"The elite's death put {def.Name} in your Grimoire", def.Color);
        }

        public void AddLog(string s)
        {
            Log.Add(s);
            if (Log.Count > 6) Log.RemoveAt(0);
            LogTime = Time.unscaledTime;
            Debug.Log("[Grimoire] " + s);
        }
    }
}
