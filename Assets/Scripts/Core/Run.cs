using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// State of one roguelike run: stats, gold, owned items and relics, consumable slots, temporary
    /// buffs. Lives on the Game object, so a restart (scene reload) starts a fresh run.
    /// </summary>
    public class Run : MonoBehaviour
    {
        public static Run Instance { get; private set; }
        public const int Slots = 3;
        public static string SlotKeyName(int slot) => Bindings.Short(GameAction.Item1 + slot);
        public const float HasteDuration = 10f;

        public readonly PlayerStats Stats = new PlayerStats();
        public int Gold;
        public int GoldEarned;
        /// <summary>Passive items and relics, in pickup order.</summary>
        public readonly List<ItemDef> Items = new List<ItemDef>();
        public readonly ItemDef[] Consumables = new ItemDef[Slots];
        public float HasteTimer;
        /// <summary>Time Scroll: enemies at half speed while above zero.</summary>
        public float SlowTimer;
        float frozenHeartCooldown;

        public string ToastTitle { get; private set; }
        public string ToastText { get; private set; }
        public Color ToastColor { get; private set; }
        public float ToastTimer { get; private set; }

        public float MoveSpeed => Stats.MoveSpeed * (HasteTimer > 0f ? 1.35f : 1f);
        public float CooldownMult => Stats.CooldownMult * (HasteTimer > 0f ? 0.5f : 1f);

        void Awake()
        {
            Instance = this;
            SpellBook.StartRun();
            Progress.NewThisRun.Clear();
            Enemy.Killed += OnKilled;
        }

        void OnDestroy() => Enemy.Killed -= OnKilled;

        void Update()
        {
            HasteTimer = Mathf.Max(0f, HasteTimer - Time.deltaTime);
            SlowTimer = Mathf.Max(0f, SlowTimer - Time.deltaTime);
            frozenHeartCooldown = Mathf.Max(0f, frozenHeartCooldown - Time.deltaTime);
            ToastTimer = Mathf.Max(0f, ToastTimer - Time.deltaTime);

            var player = Player.Instance;
            if (AutoTest.Active || !player || !player.InputEnabled || !GameState.Instance.AcceptsGameplayInput) return;
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
                    if (Player.Instance && Player.Instance.HP >= Player.Instance.MaxHP) { reason = "Already at full health"; return false; }
                    break;
                case ItemKind.Rune:
                    if (SpellBook.Knows(item.SpellId)) { reason = "You already know that spell"; return false; }
                    if (SpellBook.Known.Count >= SpellBook.MaxSpells) { reason = "Spell book is full"; return false; }
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
                case ItemKind.Heart:
                    if (Player.Instance) Player.Instance.Heal(item.Value);
                    Sfx.Play("heal", 0.5f, 1.2f);
                    return;
                case ItemKind.Consumable:
                    int slot = FreeSlot();
                    if (slot >= 0) Consumables[slot] = item;
                    Sfx.Play("pickup", 0.6f);
                    Toast(item.Name, $"{item.Desc}  (key {SlotKeyName(Mathf.Max(0, slot))})", item.Color);
                    return;
                case ItemKind.Rune:
                    GrantSpell(item.SpellId);
                    return;
                default:
                    Items.Add(item);
                    item.OnAcquire?.Invoke(this);
                    Sfx.Play("item", 0.7f);
                    Toast(item.Name, item.Desc, item.Color);
                    return;
            }
        }

        public void AddGold(int amount)
        {
            Gold += amount;
            if (amount > 0) { GoldEarned += amount; Progress.Add("gold", amount); }
        }

        /// <summary>Adds a base spell to the spell book (from a rune). Returns false if already known.</summary>
        public bool GrantSpell(string spellId)
        {
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
            if (item == null || Player.Instance == null || Player.Instance.IsDead) return false;
            if (!item.Use(this))
            {
                Sfx.Play("fizzle", 0.4f, 1.3f);
                return false;
            }
            Consumables[slot] = null;
            Progress.Add("items_used");
            return true;
        }

        public void Toast(string title, string text, Color color, float seconds = 3f)
        {
            ToastTitle = title;
            ToastText = text;
            ToastColor = color;
            ToastTimer = seconds;
        }

        /// <summary>Called when the player takes damage (Frozen Heart).</summary>
        public void OnPlayerHurt()
        {
            if (!Stats.FrostOnHurt || frozenHeartCooldown > 0f || !Player.Instance) return;
            frozenHeartCooldown = 8f;
            var p = Player.Instance.transform.position;
            Fx.Ring(p + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), 5f, 0.4f);
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
            Progress.Add("kills");
            if (e.LastHitByBolt) Progress.Add("bolt_kills");
            if (e.LastElement == Element.Poison) Progress.Add("poison_kills");
            if (e.Type.IsBoss) Progress.Add("bosses");
            if (Stats.HealOnKill > 0f && Player.Instance) Player.Instance.Heal(Stats.HealOnKill);
            if (Stats.FrostSpread && e.DiedFrozen)
            {
                Fx.Ring(e.transform.position + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), 4.5f, 0.4f);
                foreach (var o in Enemy.All)
                    if (o.IsAlive && (o.transform.position - e.transform.position).sqrMagnitude < 4.5f * 4.5f) o.Freeze(2f);
            }
            Loot.OnEnemyKilled(e, this);
        }
    }
}
