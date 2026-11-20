using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// One player's side of the run: stats, gold, owned items and relics, consumable slots, temporary
    /// buffs. Lives on the player (one per player in co-op); Run.Instance is the local player's.
    /// The run-wide setup (seed, spell book, achievements this run) is in GameState.
    /// </summary>
    public class Run : MonoBehaviour
    {
        /// <summary>The local player's run (HUD, input, saves).</summary>
        public static Run Instance { get; private set; }
        /// <summary>The player this run belongs to.</summary>
        public Player Owner { get; private set; }
        bool IsLocal => Owner && Owner.IsLocal;
        public const int Slots = 3;
        public static string SlotKeyName(int slot) => Bindings.Short(GameAction.Item1 + slot);
        public const float HasteDuration = 10f;

        public readonly PlayerStats Stats = new PlayerStats();
        public int Gold;
        public int GoldEarned;

        // ---- Arcane Essence (pays for the Spell Forge; values in EssenceConfig) ----
        public int Essence;
        /// <summary>Successful forges this run. Never goes down (forgetting doesn't make forging cheaper).</summary>
        public int ForgesDone;
        /// <summary>Kills in a row without an Essence drop (each one raises the next chance).</summary>
        public int EssenceBadLuck;
        /// <summary>Essence rolls made so far: with the run seed, it seeds the next roll (saves can't reroll).</summary>
        public int EssenceRolls;
        /// <summary>HUD pulse on pickup, 1 fading to 0.</summary>
        public float EssencePulse;
        /// <summary>What the next forge costs (free in the Practice tool).</summary>
        public int ForgeCost => Spellcaster.Essence.Free ? 0 : EssenceConfig.ForgeCost(ForgesDone);
        public int NextForgeCost => Spellcaster.Essence.Free ? 0 : EssenceConfig.ForgeCost(ForgesDone + 1);
        public bool CanAffordForge => Essence >= ForgeCost;

        /// <summary>Pays for a forge and records what this spell cost. False if there isn't enough Essence.</summary>
        public bool PayForge(SpellDefinition made)
        {
            int cost = ForgeCost;
            if (Essence < cost) return false;
            Essence -= cost;
            if (made != null) { made.EssencePaid = cost; made.EssenceRefunded = 0; }
            if (!Spellcaster.Essence.Free) ForgesDone++;
            return true;
        }

        /// <summary>Forgetting a spell: gives back part of what was paid for it. The rune remembers the refund.</summary>
        public int RefundForget(SpellDefinition s)
        {
            if (s == null) return 0;
            int refund = EssenceConfig.Refund(s.EssencePaid);
            Essence += refund;
            s.EssenceRefunded = refund;
            return refund;
        }

        public void AddEssence(int amount)
        {
            Essence = Mathf.Max(0, Essence + amount);
            if (amount > 0) EssencePulse = 1f;
        }
        /// <summary>Passive items and relics, in pickup order.</summary>
        public readonly List<ItemDef> Items = new List<ItemDef>();
        public readonly ItemDef[] Consumables = new ItemDef[Slots];
        public float HasteTimer;
        /// <summary>Time Scroll: enemies at half speed while above zero.</summary>
        public float SlowTimer;
        float frozenHeartCooldown;

        /// <summary>Counts toasts (multiplayer: a client shows the ones the host made for it).</summary>
        public int ToastId { get; private set; }
        public string ToastTitle { get; private set; }
        public string ToastText { get; private set; }
        public Color ToastColor { get; private set; }
        public float ToastTimer { get; private set; }

        public float MoveSpeed => Stats.MoveSpeed * SpeedBuff;

        /// <summary>Haste potion and Quickening: they multiply, or only the bigger counts (SelfSpellConfig.StacksWithHaste).</summary>
        float SpeedBuff
        {
            get
            {
                float haste = HasteTimer > 0f ? 1.35f : 1f, quick = Quickened ? 1f + QuickenSpeed : 1f;
                return SelfSpellConfig.StacksWithHaste ? haste * quick : Mathf.Max(haste, quick);
            }
        }

        // ---------------- Quickening (a timed buff; SelfSpells.cs) ----------------

        /// <summary>Seconds of Quickening left, its speed bonus (0.3 = +30%) and HP per second.</summary>
        public float QuickenTimer, QuickenSpeed, QuickenRegen;
        /// <summary>The longest it lasted this time (HUD timer ring), and the card extras.</summary>
        public float QuickenLength;
        public bool QuickenBurn, QuickenFrost;
        public bool Quickened => QuickenTimer > 0f;
        float quickenBurnTick, quickenFrostTick;

        /// <summary>Starts Quickening, or refreshes it: the new cast replaces the old one, it never stacks.</summary>
        public void Quicken(QuickenBuff b)
        {
            QuickenTimer = QuickenLength = Mathf.Max(0.01f, b.seconds);
            QuickenSpeed = b.speed;
            QuickenRegen = b.regen;
            QuickenBurn = b.burn;
            QuickenFrost = b.frost;
        }

        void TickQuicken(float dt)
        {
            if (QuickenTimer <= 0f) return;
            float step = Mathf.Min(dt, QuickenTimer);
            QuickenTimer -= dt;
            if (Net.NetSession.IsClientOnly || !Owner || Owner.IsDead) return; // the host heals and burns
            // Regen counts as healing (the "heal" achievement); no item reacts to healing, so it can't loop.
            if (QuickenRegen > 0f) Owner.Heal(QuickenRegen * step, quiet: true);
            Vector3 at = Owner.transform.position;
            if (QuickenBurn && (quickenBurnTick -= dt) <= 0f)
            {
                quickenBurnTick = 0.5f;
                float dps = Grimoire.GrimoireConfig.BurnDps * Stats.Power;
                foreach (var e in Enemy.All.ToArray())
                    if (e.IsAlive && Flat(e.transform.position - at) <= SelfSpellConfig.QuickenBurnRadius + e.Type.Radius * 0.5f)
                        e.Ignite(dps, Grimoire.GrimoireConfig.BurnSeconds);
            }
            if (QuickenFrost && (quickenFrostTick -= dt) <= 0f)
            {
                quickenFrostTick = 1f;
                foreach (var e in Enemy.All.ToArray())
                    if (e.IsAlive && Flat(e.transform.position - at) <= SelfSpellConfig.QuickenFrostRadius)
                        e.Slow(Grimoire.GrimoireConfig.SlowFactor, Grimoire.GrimoireConfig.SlowSeconds);
            }
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        float quickenPredictedAt = -10f;
        /// <summary>Co-op client: the buff starts at once here (the host applies the real one and sends it back).</summary>
        public void PredictQuicken(QuickenBuff b)
        {
            Quicken(b);
            quickenPredictedAt = Time.time;
        }
        public float CooldownMult => Stats.CooldownMult * (HasteTimer > 0f ? 0.5f : 1f);

        void Awake() => Enemy.Killed += OnKilled;

        /// <summary>Called by Player.Create. The local player's run is Run.Instance and is saved.</summary>
        public void Bind(Player owner)
        {
            Owner = owner;
            if (!owner.IsLocal) return;
            Instance = this;
            SaveSystem.Register(new SaveSection<RunSave>("run", 10, SaveState, LoadState));
        }

        void OnDestroy()
        {
            Enemy.Killed -= OnKilled;
            if (Instance == this) Instance = null;
        }

        public RunSave SaveState()
        {
            var gs = GameState.Instance;
            var s = new RunSave
            {
                gold = Gold, goldEarned = GoldEarned, stats = Stats,
                essence = Essence, forgesDone = ForgesDone, essenceBadLuck = EssenceBadLuck, essenceRolls = EssenceRolls,
                kills = gs ? gs.Kills : 0, runTime = gs ? gs.RunTime : 0f,
            };
            foreach (var i in Items) s.items.Add(i.Id);
            foreach (var c in Consumables) s.consumables.Add(c != null ? c.Id : "");
            foreach (var a in Progress.NewThisRun) s.newAchievements.Add(a.Reward);
            return s;
        }

        /// <summary>Items come back as owned (their pickup effects are already in the saved stats).</summary>
        public void LoadState(RunSave s)
        {
            Gold = s.gold;
            GoldEarned = s.goldEarned;
            Essence = s.essence;
            ForgesDone = s.forgesDone;
            EssenceBadLuck = s.essenceBadLuck;
            EssenceRolls = s.essenceRolls;
            Items.Clear();
            foreach (var id in s.items) { var it = Spellcaster.Items.Resolve(id); if (it != null) Items.Add(it); }
            for (int i = 0; i < Slots; i++) Consumables[i] = i < s.consumables.Count ? Spellcaster.Items.Resolve(s.consumables[i]) : null;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(s.stats), Stats);
            HasteTimer = SlowTimer = 0f;
            QuickenTimer = 0f; // buffs are not saved
            if (GameState.Instance) GameState.Instance.RestoreTotals(s.kills, s.runTime);
            Progress.NewThisRun.Clear();
            foreach (var r in s.newAchievements) { var a = Progress.All.FirstOrDefault(x => x.Reward == r); if (a != null) Progress.NewThisRun.Add(a); }
        }

        void Update()
        {
            HasteTimer = Mathf.Max(0f, HasteTimer - Time.deltaTime);
            TickQuicken(Time.deltaTime);
            SlowTimer = Mathf.Max(0f, SlowTimer - Time.deltaTime);
            frozenHeartCooldown = Mathf.Max(0f, frozenHeartCooldown - Time.deltaTime);
            ToastTimer = Mathf.Max(0f, ToastTimer - Time.deltaTime);
            EssencePulse = Mathf.Max(0f, EssencePulse - Time.deltaTime * 2.5f);

            var player = Owner;
            if (AutoTest.Active || !IsLocal || !player || !player.InputEnabled || !GameState.Instance.AcceptsGameplayInput) return;
            for (int i = 0; i < Slots; i++)
                if (Bindings.Pressed(GameAction.Item1 + i)) UseConsumable(i);
            if (Bindings.Pressed(GameAction.Interact))
            {
                var p = Pickup.NearestInteractable(player.transform.position);
                if (p) p.TryCollect(this);
            }
        }

        public bool Has(string id) => Items.Exists(i => i.Id == id);

        public int FreeSlot() => System.Array.IndexOf(Consumables, null);

        /// <summary>Checks whether an item can be taken right now; reason is shown to the player.</summary>
        public bool CanAcquire(ItemDef item, out string reason)
        {
            reason = null;
            switch (item.Kind)
            {
                case ItemKind.Consumable:
                    if (FreeSlot() < 0) { reason = "Item slots are full"; return false; }
                    break;
                case ItemKind.Heart:
                    if (Owner && Owner.HP >= Owner.MaxHP) { reason = "Already at full health"; return false; }
                    break;
                case ItemKind.Rune:
                    // A forgotten spell's rune: taking it back returns the Essence its forgetting refunded.
                    if (item.Spell != null && item.Spell.EssenceRefunded > Essence) { reason = $"Need {item.Spell.EssenceRefunded} Essence to take it back"; return false; }
                    bool remote = !IsLocal && Net.NetPlayers.Running; // another player's book (on the host)
                    if (remote ? Net.NetCombat.Knows(Owner, item.SpellId) : SpellBook.Knows(item.SpellId)) { reason = "You already know that spell"; return false; }
                    if ((remote ? Net.NetCombat.BookCount(Owner) : SpellBook.Known.Count) >= SpellBook.MaxSpells) { reason = "Spell book is full"; return false; }
                    break;
            }
            return true;
        }

        public void Acquire(ItemDef item)
        {
            switch (item.Kind)
            {
                case ItemKind.Coin:
                    AddGold(item.Value + Stats.CoinBonus);
                    Sfx.Play("coin", 0.5f, Random.Range(0.95f, 1.1f));
                    return;
                case ItemKind.Essence:
                    AddEssence(item.Value);
                    if (IsLocal) Sfx.Play("essence", 0.55f, Random.Range(0.97f, 1.06f));
                    return;
                case ItemKind.Heart:
                    if (Owner) Owner.Heal(item.Value);
                    Sfx.Play("heal", 0.5f, 1.2f);
                    return;
                case ItemKind.Consumable:
                    int slot = FreeSlot();
                    if (slot >= 0) Consumables[slot] = item;
                    Sfx.Play("pickup", 0.6f);
                    Toast(item.Name, $"{item.Desc}  (key {SlotKeyName(Mathf.Max(0, slot))})", item.Color);
                    return;
                case ItemKind.Rune:
                    if (item.Spell != null && item.Spell.EssenceRefunded > 0)
                    {
                        Essence = Mathf.Max(0, Essence - item.Spell.EssenceRefunded); // the refund goes back
                        item.Spell.EssenceRefunded = 0;
                    }
                    if (item.Spell != null) RelearnSpell(item.Spell);
                    else GrantSpell(item.SpellId);
                    return;
                default:
                    Items.Add(item);
                    item.OnAcquire?.Invoke(this);
                    Sfx.Play("item", 0.7f);
                    Toast(item.Name, item.Desc, item.Color);
                    return;
            }
        }

        static void FrostSpread(Enemy e)
        {
            Fx.Ring(e.transform.position + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), 4.5f, 0.4f);
            DevMode.Ring(e.transform.position + Vector3.up * 0.1f, 4.5f, DevMode.SpellArea);
            foreach (var o in Enemy.All)
                if (o.IsAlive && (o.transform.position - e.transform.position).sqrMagnitude < 4.5f * 4.5f) o.Freeze(2f);
        }

        public void AddGold(int amount)
        {
            Gold += amount;
            if (amount > 0) { GoldEarned += amount; if (IsLocal) Progress.Add("gold", amount); }
        }

        /// <summary>Multiplayer client: your run as the host keeps it (gold, items, consumables, stats).</summary>
        public void ApplyNet(Net.RunNet n)
        {
            Gold = n.gold;
            GoldEarned = n.goldEarned;
            if (n.essence > Essence) EssencePulse = 1f;
            Essence = n.essence;
            ForgesDone = n.forgesDone;
            EssenceBadLuck = n.essenceBadLuck;
            EssenceRolls = n.essenceRolls;
            HasteTimer = n.haste;
            // A Quickening this client just predicted isn't in the host's state yet: keep it for a moment.
            if (n.quicken > 0f || Time.time - quickenPredictedAt > 0.5f) QuickenTimer = n.quicken;
            if (n.quicken > 0f) { QuickenSpeed = n.quickenSpeed; QuickenRegen = n.quickenRegen; QuickenLength = Mathf.Max(n.quickenLength, 0.01f); }
            Items.Clear();
            foreach (var id in n.items) { var it = Spellcaster.Items.Resolve(id); if (it != null) Items.Add(it); }
            for (int i = 0; i < Slots; i++) Consumables[i] = i < n.consumables.Count ? Spellcaster.Items.Resolve(n.consumables[i]) : null;
            if (n.stats != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(n.stats), Stats);
            if (Owner) Owner.ClampHP();
            if (n.toastId != Net.NetLoot.LastRunToast && !string.IsNullOrEmpty(n.toastTitle))
            {
                Net.NetLoot.LastRunToast = n.toastId;
                Toast(n.toastTitle, n.toastText, n.toastColor);
            }
        }

        /// <summary>Puts a forgotten spell (its rune was picked back up) back in the book.</summary>
        public void RelearnSpell(SpellDefinition s)
        {
            if (!IsLocal && Net.NetPlayers.Running) { if (Net.NetCombat.Learn(Owner, s)) Toast($"Relearned {s.Name}", $"Rune {Arrows.Glyphs(s.Sequence)}   {s.Describe()}", s.Color); return; }
            if (SpellBook.Knows(s.Id)) return;
            SpellBook.Known.Add(s);
            Sfx.Play("forge", 0.6f, 1.3f);
            Toast($"Relearned {s.Name}", $"Rune {Arrows.Glyphs(s.Sequence)}   {s.Describe()}", s.Color);
        }

        /// <summary>Adds a base spell to the spell book (from a rune). Returns false if already known.</summary>
        public bool GrantSpell(string spellId)
        {
            if (!IsLocal && Net.NetPlayers.Running)
            {
                var b = SpellBook.CreateBase(spellId);
                if (b == null || !Net.NetCombat.Learn(Owner, b)) return false;
                Toast($"Learned {b.Name}", $"Rune {Arrows.Glyphs(b.Sequence)}   {b.Describe()}", b.Color);
                return true;
            }
            if (SpellBook.Knows(spellId)) return false;
            var s = SpellBook.CreateBase(spellId);
            if (s == null) return false;
            SpellBook.Known.Add(s);
            Progress.Add("runes_learned");
            Sfx.Play("forge", 0.6f, 1.3f);
            Toast($"Learned {s.Name}", $"Rune {Arrows.Glyphs(s.Sequence)}   {s.Describe()}", s.Color);
            return true;
        }

        public bool UseConsumable(int slot)
        {
            var item = slot >= 0 && slot < Slots ? Consumables[slot] : null;
            if (item == null || Owner == null || Owner.IsDead) return false;
            if (IsLocal && Net.NetSession.IsClientOnly && Net.NetPlayers.Running) { Net.NetLoot.SendUse(slot); return true; } // the host uses it
            if (!item.Use(this))
            {
                Sfx.Play("fizzle", 0.4f, 1.3f);
                return false;
            }
            Consumables[slot] = null;
            if (IsLocal) Progress.Add("items_used");
            return true;
        }

        public void Toast(string title, string text, Color color, float seconds = 3f)
        {
            ToastId++;
            ToastTitle = title;
            ToastText = text;
            ToastColor = color;
            ToastTimer = seconds;
        }

        /// <summary>Called when the player takes damage (Frozen Heart).</summary>
        public void OnPlayerHurt()
        {
            if (!Stats.FrostOnHurt || frozenHeartCooldown > 0f || !Owner) return;
            frozenHeartCooldown = 8f;
            var p = Owner.transform.position;
            Fx.Ring(p + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), 5f, 0.4f);
            DevMode.Ring(p + Vector3.up * 0.1f, 5f, DevMode.SpellArea);
            Sfx.Play("freeze", 0.6f);
            foreach (var e in Enemy.All.ToArray())
                if (e.IsAlive && (e.transform.position - p).sqrMagnitude < 25f) e.Freeze(2f);
        }

        /// <summary>Phoenix Feather: uses up one revive. Returns true if the hit should not kill.</summary>
        public bool TryRevive()
        {
            if (Stats.Revives <= 0) return false;
            Stats.Revives--;
            int i = Items.FindIndex(it => it.Id == "phoenix");
            if (i >= 0) Items.RemoveAt(i);
            Toast("Phoenix Feather", "burned away to save you", new Color(1f, 0.55f, 0.2f));
            Sfx.Play("portal", 0.8f, 1.3f);
            return true;
        }

        void OnKilled(Enemy e)
        {
            if (e.SelfDestructed) return; // a flame skull that blew itself up wasn't your kill
            if (Stats.HealOnKill > 0f && Owner) Owner.Heal(Stats.HealOnKill);
            if (!IsLocal)
            {
                // Another player's Permafrost (multiplayer host); achievements and loot run once, on this PC's run.
                if (Stats.FrostSpread && e.DiedFrozen && !Net.NetSession.IsClientOnly) FrostSpread(e);
                return;
            }
            Progress.Add("kills");
            if (e.LastHitByBolt) Progress.Add("bolt_kills");
            if (e.LastElement == Element.Poison) Progress.Add("poison_kills");
            if (e.Type.IsBoss) Progress.Add("bosses");
            if (Stats.FrostSpread && e.DiedFrozen) FrostSpread(e);
            Spellcaster.Essence.OnEnemyKilled(e); // every player rolls (bosses included)
            Loot.OnEnemyKilled(e, this);
        }
    }
}
