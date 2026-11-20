using System.Linq;
using UnityEngine;
using G = Spellcaster.Grimoire.GrimoireConfig;

namespace Spellcaster
{
    /// <summary>How a spell goes off once its rune is drawn.</summary>
    public enum CastMode
    {
        /// <summary>The rune readies the spell; Left Click fires it (every spell before 0.19).</summary>
        Readied,
        /// <summary>It goes off the moment the rune is complete; a spell already readied stays readied.</summary>
        Instant,
    }

    /// <summary>
    /// Every tunable of the two self-buff spells, Blink Dash and Quickening (static fields, not constants,
    /// so tests and the debug tools can change them at runtime).
    /// </summary>
    public static class SelfSpellConfig
    {
        // ---- cast mode (switch to Readied to test both ways) ----
        public static CastMode BlinkDashMode = CastMode.Instant;
        public static CastMode QuickeningMode = CastMode.Instant;

        /// <summary>A base spell's cast mode. Forged spells are always Readied.</summary>
        public static CastMode ModeOf(string id) =>
            id == BlinkDashId ? BlinkDashMode : id == QuickeningId ? QuickeningMode : CastMode.Readied;

        public const string BlinkDashId = "blinkdash", QuickeningId = "quickening";

        // ---- Blink Dash ----
        public const string BlinkDashRune = "←→";
        /// <summary>Metres travelled, over DashDuration seconds. Spell power doesn't change it (cards do).</summary>
        public static float DashDistance = 6f;
        public static float DashDuration = 0.15f;
        public static float DashCooldown = 4f;
        /// <summary>No damage for the dash plus this long.</summary>
        public static float DashExtraIFrames = 0.1f;
        /// <summary>Degrees the view widens for a moment (subtle).</summary>
        public static float DashFovKick = 7f;
        /// <summary>How long the afterimage other players see lasts.</summary>
        public static float AfterimageSeconds = 0.3f;

        // ---- Quickening ----
        public const string QuickeningRune = "↓↑↓";
        public static float QuickenDuration = 6f;
        /// <summary>+30% movement speed (Momentum's running too; never the dash distance).</summary>
        public static float QuickenSpeedBonus = 0.3f;
        /// <summary>HP healed per second while it lasts.</summary>
        public static float QuickenRegen = 3f;
        public static float QuickenCooldown = 20f;
        /// <summary>True: Quickening and the Haste potion multiply. False: only the bigger one counts.</summary>
        public static bool StacksWithHaste = true;

        // ---- Grimoire cards on Blink Dash ----
        /// <summary>Split: three chained dashes, each this share of the distance.</summary>
        public static float DashSplitScale = 0.6f;
        /// <summary>Pierce: damage to each enemy you dash through (times spell power).</summary>
        public static float DashPierceDamage = 15f;
        /// <summary>Wide: knockback burst where the dash starts.</summary>
        public static float DashWideRadius = 4f, DashWideForce = 10f;
        /// <summary>Frozen: enemies near where the dash starts are frozen.</summary>
        public static float DashFreezeRadius = 4f, DashFreezeSeconds = 2f;
        /// <summary>Burning: a trail of fire patches (they never hurt players).</summary>
        public static float DashTrailSpacing = 1.5f, DashTrailRadius = 1.1f, DashTrailSeconds = 2.5f;
        /// <summary>Echo: a second dash this long after the cast.</summary>
        public static float DashEchoDelay = 0.5f;

        // ---- Grimoire cards on Quickening ----
        /// <summary>Split: allies this close get the buff too (co-op); solo, +1 regen per second.</summary>
        public static float QuickenSplitAllyRadius = 5f, QuickenSplitSoloRegen = 1f;
        /// <summary>Wide: allies this close get the buff too (co-op); solo, a slowing pulse around you.</summary>
        public static float QuickenWideAllyRadius = 10f;
        /// <summary>Echo: duration +50%.</summary>
        public static float QuickenEchoDuration = 1.5f;
        /// <summary>Burning: enemies this close to you catch fire (checked twice a second).</summary>
        public static float QuickenBurnRadius = 1.8f;
        /// <summary>Frozen: enemies this close to you are slowed (checked every second).</summary>
        public static float QuickenFrostRadius = 4f;
    }

    /// <summary>Blink Dash: a very fast horizontal dash along the view, through enemies and their shots, never through walls.</summary>
    public class DashEffect : SpellEffect
    {
        public float Distance;
        public DashEffect(float distance) { Distance = distance; }
        public override string Name => "Dash";
        public override Element Element => Element.Arcane;
        public override EffectRole Role => EffectRole.Caster;
        public override SpellEffect Scaled(float p) => new DashEffect(Distance);
        public override string Describe() => $"dash {Distance:0}m";

        public override void ApplyToCaster(SpellCast cast)
        {
            var p = cast.Caster;
            if (!p) return;
            float d = Distance * cast.Mods.RangeMult;
            p.QueueDash(d, 0f, cast.Mods.DashPierce, cast.Mods.DashTrail);
            if (cast.Mods.DashEcho > 0f) p.QueueDash(d, cast.Mods.DashEcho, cast.Mods.DashPierce, cast.Mods.DashTrail);
            Sfx.Play("whoosh", 0.7f, Random.Range(0.95f, 1.08f));
        }
    }

    /// <summary>Quickening: a timed buff, faster movement and health regen. Recasting refreshes it, it never stacks.</summary>
    public class QuickenEffect : SpellEffect
    {
        public float Duration, SpeedBonus, Regen;
        public QuickenEffect(float duration, float speedBonus, float regen) { Duration = duration; SpeedBonus = speedBonus; Regen = regen; }
        public override string Name => "Quicken";
        public override Element Element => Element.Life;
        public override EffectRole Role => EffectRole.Caster;
        public override SpellEffect Scaled(float p) => new QuickenEffect(Duration, SpeedBonus, Regen * p);
        public override string Describe() => $"+{SpeedBonus * 100f:0}% speed, {Regen:0.#} HP/s for {Duration:0}s";

        public override void ApplyToCaster(SpellCast cast)
        {
            var p = cast.Caster;
            if (!p || !p.Run) return;
            var m = cast.Mods;
            var buff = new QuickenBuff
            {
                seconds = Duration * m.DurationMult,
                speed = SpeedBonus * m.DamageMult,
                regen = Regen * cast.Power + m.ExtraRegen,
                burn = m.BuffBurn, frost = m.BuffFrost,
            };
            p.Run.Quicken(buff);
            if (m.Cleanse) p.Cleanse();
            if (m.AllyRadius > 0f)
                foreach (var q in Players.All.ToArray())
                {
                    if (!q || q == p || q.IsDead || !q.Run) continue;
                    Vector3 d = q.transform.position - p.transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > m.AllyRadius * m.AllyRadius) continue;
                    q.Run.Quicken(buff);
                    Fx.Flash(q.ChestPosition, cast.Spell.Color, 1.6f, 0.35f, 1.2f);
                }
            Sfx.Play("quicken", 0.7f);
        }
    }

    /// <summary>One Quickening buff: how long, how much faster, how much regen, and the card extras.</summary>
    public struct QuickenBuff
    {
        public float seconds, speed, regen;
        public bool burn, frost;
    }

    /// <summary>
    /// How the Grimoire cards change Blink Dash and Quickening (every card does something to them).
    /// Other self spells (Heal, Blink, forged spells) keep the general card rules in <see cref="Grimoire.Cards"/>.
    /// </summary>
    public static class SelfSpellCards
    {
        public static bool IsDash(SpellDefinition s) => s != null && s.Id == SelfSpellConfig.BlinkDashId;
        public static bool IsQuickening(SpellDefinition s) => s != null && s.Id == SelfSpellConfig.QuickeningId;

        /// <summary>Applies the card when the spell is Blink Dash or Quickening; false for any other spell.</summary>
        public static bool TryApply(Grimoire.CardDef card, Grimoire.CardContext c)
        {
            if (IsDash(c.Spell)) { Dash(card.Id, c); return true; }
            if (IsQuickening(c.Spell)) { Quicken(card.Id, c); return true; }
            return false;
        }

        static void Dash(string id, Grimoire.CardContext c)
        {
            Vector3 start = c.Player ? c.Player.transform.position : Vector3.zero;
            switch (id)
            {
                case "amplify": c.ForEach(m => m.RangeMult *= G.AmplifyMult); break;
                case "overload":
                    c.ForEach(m => m.RangeMult *= G.OverloadMult);
                    c.Request.CooldownMult *= G.OverloadCooldownMult;
                    break;
                case "split":
                {
                    var main = c.Main;
                    c.Request.Copies.Clear();
                    for (int i = 0; i < 3; i++) { var m = main.Clone(); m.RangeMult *= SelfSpellConfig.DashSplitScale; c.Request.Copies.Add(m); }
                    break;
                }
                case "pierce": { float dmg = SelfSpellConfig.DashPierceDamage * c.Power; c.ForEach(m => m.DashPierce = dmg); break; }
                case "wide":
                    c.After(() => Pulse(c, start, new Color(1f, 0.85f, 0.35f), SelfSpellConfig.DashWideRadius, e =>
                    {
                        Vector3 d = e.transform.position - start;
                        d.y = 0f;
                        e.Knockback((d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward) * SelfSpellConfig.DashWideForce);
                    }));
                    break;
                case "echo": c.ForEach(m => m.DashEcho = SelfSpellConfig.DashEchoDelay); break;
                case "quicken": c.Request.ResetCooldown = true; break;
                case "burning": c.ForEach(m => m.DashTrail = true); break;
                case "frozen":
                    c.After(() => Pulse(c, start, new Color(0.6f, 0.85f, 1f), SelfSpellConfig.DashFreezeRadius, e => e.Freeze(SelfSpellConfig.DashFreezeSeconds)));
                    break;
                case "fumble": c.Request.Fizzle = true; break;
                // blank: no effect
            }
        }

        static void Quicken(string id, Grimoire.CardContext c)
        {
            bool coop = GameState.Multiplayer;
            switch (id)
            {
                case "amplify": c.ForEach(m => m.DamageMult *= G.AmplifyMult); break; // speed and regen +50%
                case "overload":
                    c.ForEach(m => m.DurationMult *= 2f);
                    c.Request.CooldownMult *= G.OverloadCooldownMult;
                    break;
                case "split":
                    if (coop) c.ForEach(m => m.AllyRadius = SelfSpellConfig.QuickenSplitAllyRadius);
                    else c.ForEach(m => m.ExtraRegen += SelfSpellConfig.QuickenSplitSoloRegen);
                    break;
                case "pierce": c.ForEach(m => m.Cleanse = true); break;
                case "wide":
                    if (coop) c.ForEach(m => m.AllyRadius = SelfSpellConfig.QuickenWideAllyRadius);
                    else
                    {
                        Vector3 at = c.Player ? c.Player.transform.position : Vector3.zero;
                        c.After(() => Pulse(c, at, new Color(0.7f, 0.75f, 1f), G.SelfPulseRadius * G.WideMult, e => e.Slow(G.SlowFactor, G.SlowSeconds)));
                    }
                    break;
                case "echo": c.ForEach(m => m.DurationMult *= SelfSpellConfig.QuickenEchoDuration); break;
                case "quicken": c.Request.ResetCooldown = true; break;
                case "burning": c.ForEach(m => m.BuffBurn = true); break;
                case "frozen": c.ForEach(m => m.BuffFrost = true); break;
                case "fumble": c.Request.Fizzle = true; break;
                // blank: no effect
            }
        }

        /// <summary>A ring at a point that does something to every live enemy inside it.</summary>
        static void Pulse(Grimoire.CardContext c, Vector3 p, Color color, float r, System.Action<Enemy> onEnemy)
        {
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

        /// <summary>
        /// Co-op client: what the dash will do with the Next card (the host plays it and has the last word):
        /// the distance of each dash, how many chain, and the echo delay. Fumble: no dash at all.
        /// </summary>
        public static void Predict(string cardId, out float distance, out int count, out float echo)
        {
            distance = SelfSpellConfig.DashDistance;
            count = 1;
            echo = 0f;
            switch (cardId)
            {
                case "amplify": distance *= G.AmplifyMult; break;
                case "overload": distance *= G.OverloadMult; break;
                case "split": distance *= SelfSpellConfig.DashSplitScale; count = 3; break;
                case "echo": echo = SelfSpellConfig.DashEchoDelay; break;
                case "fumble": distance = 0f; count = 0; break;
            }
        }
    }
}
