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
        public static readonly KeyCode[] SlotKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };
        public static string SlotKeyName(int slot) => (slot + 1).ToString();
        public const float HasteDuration = 10f;

        public readonly PlayerStats Stats = new PlayerStats();
        public int Gold;
        public int GoldEarned;
        /// <summary>Passive items and relics, in pickup order.</summary>
        public readonly List<ItemDef> Items = new List<ItemDef>();
        public readonly ItemDef[] Consumables = new ItemDef[Slots];
        public float HasteTimer;

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
            Enemy.Killed += OnKilled;
        }

        void OnDestroy() => Enemy.Killed -= OnKilled;

        void Update()
        {
            HasteTimer = Mathf.Max(0f, HasteTimer - Time.deltaTime);
            ToastTimer = Mathf.Max(0f, ToastTimer - Time.deltaTime);

            var player = Player.Instance;
            if (AutoTest.Active || !player || !player.InputEnabled || !GameState.Instance.AcceptsGameplayInput) return;
            for (int i = 0; i < Slots; i++)
                if (Input.GetKeyDown(SlotKeys[i]) || Input.GetKeyDown(KeyCode.Keypad1 + i)) UseConsumable(i);
            if (Input.GetKeyDown(KeyCode.E))
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
                    AddGold(item.Value);
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
            if (amount > 0) GoldEarned += amount;
        }

        /// <summary>Adds a base spell to the spell book (from a rune). Returns false if already known.</summary>
        public bool GrantSpell(string spellId)
        {
            if (SpellBook.Knows(spellId)) return false;
            var s = SpellBook.CreateBase(spellId);
            if (s == null) return false;
            SpellBook.Known.Add(s);
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
            return true;
        }

        public void Toast(string title, string text, Color color, float seconds = 3f)
        {
            ToastTitle = title;
            ToastText = text;
            ToastColor = color;
            ToastTimer = seconds;
        }

        void OnKilled(Enemy e)
        {
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
