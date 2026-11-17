using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using C = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster.Grimoire
{
    public enum CardCategory { Neutral, Power, Shape, Tempo, Element, Curse }

    /// <summary>How a spell reaches its targets, as far as cards care.</summary>
    public enum SpellShape { Bolt, Projectile, Hitscan, Area, Self }

    /// <summary>One kind of card. The effect edits the cast about to happen.</summary>
    public class CardDef
    {
        public string Id, Name, Desc;
        /// <summary>Short text drawn big on the card (its icon).</summary>
        public string Symbol;
        public Color Color;
        public CardCategory Category;
        public Action<CardContext> Apply;
        public bool IsCurse => Category == CardCategory.Curse;
    }

    /// <summary>A card in the deck (several copies of a card are separate instances).</summary>
    public class Card
    {
        public readonly CardDef Def;
        public Card(CardDef def) { Def = def; }
        public override string ToString() => Def.Name;
    }

    /// <summary>What a card's effect receives: the cast request it may modify.</summary>
    public class CardContext
    {
        public CastRequest Request;
        public SpellDefinition Spell;
        public SpellShape Shape;
        public Player Player;

        /// <summary>The single copy a cast starts with (cards are applied one per cast).</summary>
        public CastMods Main => Request.Copies[0];
        public bool HasChain => Spell != null && Spell.Payload.Exists(e => e is ChainEffect);
        public bool IsHeal => Spell != null && Spell.Payload.Exists(e => e is HealEffect);
        public bool IsBlink => Spell != null && Spell.Payload.Exists(e => e is BlinkEffect);
        public float Power => Run.Instance ? Run.Instance.Stats.Power : 1f;

        public void ForEach(Action<CastMods> f) { foreach (var m in Request.Copies) f(m); }

        /// <summary>Adds work to do right after the spell went off.</summary>
        public void After(Action a) => Request.AfterLaunch += a;
    }

    /// <summary>
    /// THE card list. To add a card: add one entry below (id, name, description, symbol, colour,
    /// category) with an Apply function that edits the CardContext (see existing cards for each spell
    /// shape), then optionally put it in a starting deck in GrimoireConfig. Rewards pick from every
    /// non-curse card except Blank automatically.
    /// </summary>
    public static class Cards
    {
        static Color Col(float r, float g, float b) => new Color(r, g, b);

        public static readonly List<CardDef> All = new List<CardDef>
        {
            new CardDef { Id = "blank", Name = "Blank", Symbol = "-", Desc = "No effect", Color = Col(0.62f, 0.6f, 0.68f),
                Category = CardCategory.Neutral, Apply = c => { } },

            new CardDef { Id = "amplify", Name = "Amplify", Symbol = "+", Desc = "+50% damage (Heal: +50% healing)",
                Color = Col(1f, 0.55f, 0.25f), Category = CardCategory.Power,
                Apply = c => c.ForEach(m => { m.DamageMult *= C.AmplifyMult; m.RangeMult *= C.AmplifyMult; }) },

            new CardDef { Id = "overload", Name = "Overload", Symbol = "x2", Desc = "+100% damage, cooldown doubled",
                Color = Col(1f, 0.25f, 0.3f), Category = CardCategory.Power,
                Apply = c =>
                {
                    c.ForEach(m => { m.DamageMult *= C.OverloadMult; m.RangeMult *= C.OverloadMult; });
                    c.Request.CooldownMult *= C.OverloadCooldownMult;
                } },

            new CardDef { Id = "split", Name = "Split", Symbol = "x3", Desc = "3 shots in a fan / 3 smaller areas, 60% each",
                Color = Col(0.4f, 1f, 0.9f), Category = CardCategory.Shape, Apply = Split },

            new CardDef { Id = "pierce", Name = "Pierce", Symbol = ">>", Desc = "Shots pass through enemies; areas +1 chain",
                Color = Col(0.85f, 0.9f, 1f), Category = CardCategory.Shape, Apply = Pierce },

            new CardDef { Id = "wide", Name = "Wide", Symbol = "( )", Desc = "+50% area and projectile size",
                Color = Col(1f, 0.85f, 0.35f), Category = CardCategory.Shape, Apply = Wide },

            new CardDef { Id = "echo", Name = "Echo", Symbol = ")))", Desc = "Recast automatically 0.5s later",
                Color = Col(0.8f, 0.5f, 1f), Category = CardCategory.Tempo,
                Apply = c => c.Request.EchoDelay = C.EchoDelay },

            new CardDef { Id = "quicken", Name = "Quicken", Symbol = "0s", Desc = "No cooldown for this cast",
                Color = Col(0.45f, 1f, 0.5f), Category = CardCategory.Tempo,
                Apply = c => c.Request.ResetCooldown = true },

            new CardDef { Id = "burning", Name = "Burning", Symbol = "~", Desc = "Hits burn enemies over time",
                Color = Col(1f, 0.4f, 0.1f), Category = CardCategory.Element,
                Apply = c =>
                {
                    float dps = C.BurnDps * c.Power;
                    if (c.Shape == SpellShape.Self) c.After(() => SelfPulse(c, new Color(1f, 0.45f, 0.1f), 1f, e => e.Ignite(dps, C.BurnSeconds)));
                    else c.ForEach(m => m.OnHit += e => e.Ignite(dps * m.DamageMult, C.BurnSeconds));
                } },

            new CardDef { Id = "frozen", Name = "Frozen", Symbol = "*", Desc = "Hits slow enemies by 50% for 2s",
                Color = Col(0.55f, 0.85f, 1f), Category = CardCategory.Element,
                Apply = c =>
                {
                    if (c.Shape == SpellShape.Self) c.After(() => SelfPulse(c, new Color(0.6f, 0.85f, 1f), 1f, e => e.Slow(C.SlowFactor, C.SlowSeconds)));
                    else c.ForEach(m => m.OnHit += e => e.Slow(C.SlowFactor, C.SlowSeconds));
                } },

            new CardDef { Id = "fumble", Name = "Fumble", Symbol = "?", Desc = "Curse: the spell fizzles (no cooldown spent)",
                Color = Col(0.55f, 0.3f, 0.45f), Category = CardCategory.Curse,
                Apply = c => c.Request.Fizzle = true },
        };

        public static CardDef Get(string id) => All.FirstOrDefault(c => c.Id == id);

        /// <summary>Cards rooms can offer: every non-curse card except Blank.</summary>
        public static List<CardDef> RewardPool => All.Where(c => !c.IsCurse && c.Id != "blank").ToList();

        public static SpellShape ShapeOf(SpellDefinition s)
        {
            if (s == null) return SpellShape.Bolt;
            switch (s.Delivery)
            {
                case ProjectileDelivery _:
                case HomingDelivery _: return SpellShape.Projectile;
                case HitscanDelivery _: return SpellShape.Hitscan;
                case SelfDelivery _: return SpellShape.Self;
                default: return SpellShape.Area; // nova, meteor
            }
        }

        // ---------------- card effects that differ per spell shape ----------------

        static void Split(CardContext c)
        {
            var main = c.Main;
            var copies = c.Request.Copies;
            copies.Clear();
            for (int i = 0; i < 3; i++)
            {
                var m = main.Clone();
                m.DamageMult *= C.SplitPower;
                if (i > 0) m.NoLifesteal = true; // only one copy can lifesteal (balance unchanged)
                switch (c.Shape)
                {
                    case SpellShape.Bolt:
                    case SpellShape.Projectile:
                    case SpellShape.Hitscan:
                        m.AimYaw += (i - 1) * C.SplitFanDegrees;
                        break;
                    case SpellShape.Area:
                        // Three smaller areas in a triangle: around you for novas, around the target for meteors.
                        m.AreaMult *= C.SplitAreaScale;
                        var fwd = c.Player ? c.Player.Cam.transform.forward : Vector3.forward;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                        m.Offset += Quaternion.AngleAxis(i * 120f, Vector3.up) * fwd.normalized * C.SplitAreaSpacing;
                        break;
                    case SpellShape.Self:
                        m.RangeMult *= C.SplitPower;
                        if (i == 2) continue; // self spells go off twice
                        break;
                }
                copies.Add(m);
            }
        }

        static void Pierce(CardContext c)
        {
            switch (c.Shape)
            {
                case SpellShape.Bolt:
                case SpellShape.Projectile:
                    c.ForEach(m => m.Pierce = true);
                    break;
                case SpellShape.Hitscan:
                    c.ForEach(m => { m.Pierce = true; if (c.HasChain) m.ExtraChains += 1; });
                    break;
                case SpellShape.Area:
                    c.ForEach(m => m.ExtraChains += 1);
                    break;
                case SpellShape.Self:
                    if (!c.IsBlink || !c.Player) break; // Heal: no target to pierce
                    Vector3 from = c.Player.transform.position;
                    c.After(() => BlinkPierce(c, from));
                    break;
            }
        }

        static void Wide(CardContext c)
        {
            if (c.Shape == SpellShape.Self)
            {
                c.ForEach(m => m.RangeMult *= C.WideMult); // Blink goes further
                if (c.IsHeal) c.After(() => SelfPulse(c, new Color(0.7f, 0.75f, 1f), C.WideMult, e => e.Slow(C.SlowFactor, C.SlowSeconds)));
                return;
            }
            c.ForEach(m => { m.AreaMult *= C.WideMult; m.SizeMult *= C.WideMult; });
            // Single-target ray without an area or chain (Life Drain): reach one more enemy nearby instead.
            if (c.Shape == SpellShape.Hitscan && !c.HasChain && c.Spell.AreaRadius <= 0f) c.ForEach(m => m.ExtraChains += 1);
        }

        // ---------------- fallbacks for self spells ----------------

        static void SelfPulse(CardContext c, Color color, float radiusMult, Action<Enemy> onEnemy)
        {
            if (!c.Player) return;
            Vector3 p = c.Player.transform.position;
            float r = C.SelfPulseRadius * radiusMult;
            Fx.Ring(p + Vector3.up * 0.1f, color, r, 0.45f);
            Fx.AreaMarker(p, color, r, 0.6f);
            DevMode.Ring(p + Vector3.up * 0.1f, r, DevMode.SpellArea);
            foreach (var e in Enemy.All.ToArray())
            {
                Vector3 d = e.transform.position - p;
                d.y = 0f;
                if (e.IsAlive && d.sqrMagnitude <= r * r) onEnemy(e);
            }
        }

        static void BlinkPierce(CardContext c, Vector3 from)
        {
            if (!c.Player) return;
            Vector3 to = c.Player.transform.position;
            float dmg = C.BlinkPierceDamage * c.Power * c.Main.DamageMult;
            DevMode.Line(from + Vector3.up, to + Vector3.up, DevMode.SpellRay);
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.IsAlive) continue;
                Vector3 p = e.transform.position;
                Vector3 seg = to - from;
                float t = seg.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - from, seg) / seg.sqrMagnitude) : 0f;
                Vector3 d = p - (from + seg * t);
                d.y = 0f;
                if (d.sqrMagnitude > 1.6f * 1.6f) continue;
                e.TakeDamage(dmg, Element.Arcane);
                Fx.Flash(e.Center, c.Spell.Color, 1.4f, 0.3f, 1f);
            }
        }
    }
}
