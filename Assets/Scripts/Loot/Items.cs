using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    public enum ItemKind { Passive, Relic, Consumable, Rune, Coin, Heart }

    /// <summary>One kind of loot. Passives change stats, relics change how spells behave, consumables sit in a slot.</summary>
    public class ItemDef
    {
        public string Id, Name, Desc;
        public ItemKind Kind;
        public int Price;
        public Color Color = Color.white;
        /// <summary>Icon shape from ItemIcons (potion, bomb, gem, ring...).</summary>
        public string Shape;
        public int Value = 1;
        public string SpellId;
        public Action<Run> OnAcquire;
        /// <summary>Consumables: returns false if it can't be used right now (the item is kept).</summary>
        public Func<Run, bool> Use;
        public Func<Texture2D> IconFactory;

        Texture2D icon;
        public Texture2D Icon => icon ? icon : (icon = IconFactory != null ? IconFactory() : ItemIcons.Get(Shape, Color));
    }

    /// <summary>
    /// THE loot table. Add a passive or relic by adding an entry; relic effects read the PlayerStats
    /// modifiers in the spell code (SpellCast, SpellEffects, SpellDeliveries).
    /// </summary>
    public static class Items
    {
        static Color C(float r, float g, float b) => new Color(r, g, b);

        static Items()
        {
            foreach (var r in Relics) r.Kind = ItemKind.Relic;
        }

        public static readonly ItemDef Coin = new ItemDef
            { Id = "coin", Name = "Gold", Kind = ItemKind.Coin, Shape = "coin", Color = C(1f, 0.85f, 0.25f) };
        public static readonly ItemDef BigCoin = new ItemDef
            { Id = "coin5", Name = "Gold pile", Kind = ItemKind.Coin, Shape = "coin", Value = 5, Color = C(1f, 0.85f, 0.25f) };
        public static readonly ItemDef Heart = new ItemDef
            { Id = "heart", Name = "Heart", Desc = "Restore 20 HP", Kind = ItemKind.Heart, Shape = "heart", Value = 20, Price = 6, Color = C(0.95f, 0.15f, 0.2f) };

        public static readonly List<ItemDef> Passives = new List<ItemDef>
        {
            new ItemDef { Id = "ruby_heart", Name = "Ruby Heart", Desc = "+25 max HP", Shape = "heart", Color = C(0.9f, 0.1f, 0.35f), Price = 25,
                OnAcquire = r => { r.Stats.MaxHP += 25f; Player.Instance?.Heal(25f); } },
            new ItemDef { Id = "swift_boots", Name = "Swift Boots", Desc = "+15% move speed", Shape = "boots", Color = C(0.35f, 0.75f, 0.4f), Price = 20,
                OnAcquire = r => r.Stats.MoveSpeed *= 1.15f },
            new ItemDef { Id = "arcane_focus", Name = "Arcane Focus", Desc = "+20% spell power", Shape = "gem", Color = C(0.7f, 0.4f, 1f), Price = 30,
                OnAcquire = r => r.Stats.Power += 0.2f },
            new ItemDef { Id = "hourglass", Name = "Hourglass", Desc = "Spell cooldowns -15%", Shape = "hourglass", Color = C(0.9f, 0.8f, 0.4f), Price = 30,
                OnAcquire = r => r.Stats.CooldownMult *= 0.85f },
            new ItemDef { Id = "sharp_wand", Name = "Sharpened Wand", Desc = "Magic bolt +5 damage", Shape = "wand", Color = C(0.75f, 0.55f, 1f), Price = 20,
                OnAcquire = r => r.Stats.BoltDamage += 5f },
            new ItemDef { Id = "quick_ring", Name = "Quickcast Ring", Desc = "Magic bolt fires 30% faster", Shape = "ring", Color = C(1f, 0.85f, 0.3f), Price = 20,
                OnAcquire = r => r.Stats.BoltInterval /= 1.3f },
            new ItemDef { Id = "iron_robe", Name = "Iron Robe", Desc = "Take 15% less damage", Shape = "shield", Color = C(0.6f, 0.65f, 0.75f), Price = 25,
                OnAcquire = r => r.Stats.DamageTaken *= 0.85f },
            new ItemDef { Id = "clover", Name = "Lucky Clover", Desc = "Enemies and crates drop more loot", Shape = "clover", Color = C(0.3f, 0.85f, 0.35f), Price = 15,
                OnAcquire = r => r.Stats.Luck += 0.5f },
            new ItemDef { Id = "fang", Name = "Vampire Fang", Desc = "Heal 2 HP per kill", Shape = "fang", Color = C(0.85f, 0.1f, 0.15f), Price = 25,
                OnAcquire = r => r.Stats.HealOnKill += 2f },
            new ItemDef { Id = "glass_cannon", Name = "Glass Cannon", Desc = "+40% spell power, -25 max HP", Shape = "potion", Color = C(0.6f, 0.9f, 1f), Price = 20,
                OnAcquire = r => { r.Stats.Power += 0.4f; r.Stats.MaxHP = Mathf.Max(25f, r.Stats.MaxHP - 25f); Player.Instance?.ClampHP(); } },
        };

        public static readonly List<ItemDef> Relics = new List<ItemDef>
        {
            new ItemDef { Id = "prism", Name = "Splitting Prism", Desc = "Projectile spells fire 2 extra projectiles", Shape = "gem", Color = C(0.4f, 1f, 0.95f), Price = 35,
                OnAcquire = r => r.Stats.ProjectileSplit += 2 },
            new ItemDef { Id = "coil", Name = "Conductive Coil", Desc = "Chains jump to 2 more enemies", Shape = "ring", Color = C(0.5f, 0.75f, 1f), Price = 30,
                OnAcquire = r => r.Stats.ExtraChains += 2 },
            new ItemDef { Id = "volatile", Name = "Volatile Soul", Desc = "Enemies killed by spells explode", Shape = "orb", Color = C(1f, 0.5f, 0.15f), Price = 35,
                OnAcquire = r => r.Stats.VolatileKills = true },
            new ItemDef { Id = "permafrost", Name = "Permafrost Shard", Desc = "Frozen enemies that die freeze their neighbours", Shape = "gem", Color = C(0.65f, 0.9f, 1f), Price = 30,
                OnAcquire = r => r.Stats.FrostSpread = true },
            new ItemDef { Id = "ember", Name = "Ember Brand", Desc = "Every spell hit sets enemies burning", Shape = "tome", Color = C(1f, 0.35f, 0.1f), Price = 35,
                OnAcquire = r => r.Stats.IgniteOnHit += 8f },
            new ItemDef { Id = "echo", Name = "Echo Stone", Desc = "25% chance a spell casts twice", Shape = "orb", Color = C(0.8f, 0.5f, 1f), Price = 40,
                OnAcquire = r => r.Stats.EchoChance += 0.25f },
            new ItemDef { Id = "leech", Name = "Leech Sigil", Desc = "Spells heal you for 8% of their damage", Shape = "fang", Color = C(0.55f, 1f, 0.45f), Price = 30,
                OnAcquire = r => r.Stats.SpellLifesteal += 0.08f },
            new ItemDef { Id = "blast_core", Name = "Blast Core", Desc = "Explosions and novas are 40% bigger", Shape = "orb", Color = C(1f, 0.8f, 0.3f), Price = 30,
                OnAcquire = r => r.Stats.AreaMult *= 1.4f },
            new ItemDef { Id = "frost_wand", Name = "Frostbite Wand", Desc = "Magic bolts may freeze enemies", Shape = "wand", Color = C(0.55f, 0.9f, 1f), Price = 25,
                OnAcquire = r => r.Stats.BoltFreezeChance += 0.2f },
        };

        public static readonly List<ItemDef> Consumables = new List<ItemDef>
        {
            new ItemDef { Id = "potion", Name = "Health Potion", Desc = "Restore 35 HP", Kind = ItemKind.Consumable, Shape = "potion", Color = C(0.95f, 0.2f, 0.25f), Price = 8,
                Use = r =>
                {
                    var p = Player.Instance;
                    if (p.HP >= p.MaxHP) return false;
                    p.Heal(35f);
                    Sfx.Play("potion", 0.7f);
                    return true;
                } },
            new ItemDef { Id = "bomb", Name = "Fire Bomb", Desc = "Throw: explodes for 60 damage", Kind = ItemKind.Consumable, Shape = "bomb", Color = C(1f, 0.5f, 0.15f), Price = 10,
                Use = r => { Combat.ThrowBomb(Player.Instance); return true; } },
            new ItemDef { Id = "frost_scroll", Name = "Frost Scroll", Desc = "Freeze every nearby enemy for 4s", Kind = ItemKind.Consumable, Shape = "scroll", Color = C(0.4f, 0.75f, 1f), Price = 12,
                Use = r =>
                {
                    var p = Player.Instance.transform.position;
                    Fx.Ring(p + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), 16f, 0.6f);
                    Sfx.Play("freeze", 0.8f);
                    foreach (var e in Enemy.All.ToArray())
                        if (e.IsAlive && (e.transform.position - p).sqrMagnitude < 16f * 16f) e.Freeze(4f);
                    return true;
                } },
            new ItemDef { Id = "haste", Name = "Haste Elixir", Desc = "10s: faster movement, half cooldowns", Kind = ItemKind.Consumable, Shape = "potion", Color = C(1f, 0.85f, 0.2f), Price = 12,
                Use = r => { r.HasteTimer = Run.HasteDuration; Sfx.Play("potion", 0.7f, 1.3f); return true; } },
            new ItemDef { Id = "renewal", Name = "Scroll of Renewal", Desc = "Reset all spell cooldowns", Kind = ItemKind.Consumable, Shape = "scroll", Color = C(0.5f, 1f, 0.6f), Price = 10,
                Use = r => { SpellSystem.Instance.ResetCooldowns(); Sfx.Play("ready", 0.7f); return true; } },
        };

        static readonly Dictionary<string, ItemDef> runes = new Dictionary<string, ItemDef>();

        /// <summary>A pickup that teaches a base spell.</summary>
        public static ItemDef Rune(string spellId)
        {
            if (runes.TryGetValue(spellId, out var r)) return r;
            var s = SpellBook.CreateBase(spellId);
            r = new ItemDef
            {
                Id = "rune_" + spellId, Name = s.Name + " Rune", Kind = ItemKind.Rune, SpellId = spellId, Price = 25,
                Desc = $"Learn {s.Name} (keys {Arrows.KeysText(s.Sequence)})", Color = s.Color, IconFactory = () => s.Icon,
            };
            runes[spellId] = r;
            return r;
        }

        public static ItemDef Get(string id) =>
            Passives.Concat(Relics).Concat(Consumables).FirstOrDefault(i => i.Id == id)
            ?? (id.StartsWith("rune_") ? Rune(id.Substring(5)) : null);

        static ItemDef Pick(List<ItemDef> pool) => pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)];

        /// <summary>A passive or relic the player doesn't own yet (relicChance 0..1), or a heart if none are left.</summary>
        public static ItemDef RandomTreasure(Run run, float relicChance, ICollection<ItemDef> exclude = null)
        {
            bool Free(ItemDef i) => !run.Has(i.Id) && (exclude == null || !exclude.Contains(i));
            var relics = Relics.Where(Free).ToList();
            var passives = Passives.Where(Free).ToList();
            var pool = (UnityEngine.Random.value < relicChance && relics.Count > 0) || passives.Count == 0 ? relics : passives;
            return Pick(pool) ?? Heart;
        }

        public static ItemDef RandomConsumable() => Pick(Consumables);

        /// <summary>A rune for a base spell the player doesn't know yet, or null.</summary>
        public static ItemDef RandomRune(ICollection<ItemDef> exclude = null)
        {
            var ids = SpellBook.BaseIds.Where(id => !SpellBook.Knows(id) && (exclude == null || !exclude.Contains(Rune(id)))).ToList();
            return ids.Count == 0 ? null : Rune(ids[UnityEngine.Random.Range(0, ids.Count)]);
        }
    }
}
