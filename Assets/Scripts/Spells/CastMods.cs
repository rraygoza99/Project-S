using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Per-cast modifiers a system outside the spell code can set (used by the experimental Grimoire).
    /// The defaults change nothing, so a cast without modifiers behaves exactly as before.
    /// </summary>
    public class CastMods
    {
        /// <summary>Multiplies the cast's power (damage, burn, healing).</summary>
        public float DamageMult = 1f;
        /// <summary>Multiplies explosion / nova / cloud radius and chain range.</summary>
        public float AreaMult = 1f;
        /// <summary>Multiplies projectile size.</summary>
        public float SizeMult = 1f;
        /// <summary>Multiplies distances of self spells (Blink).</summary>
        public float RangeMult = 1f;
        /// <summary>Projectiles pass through enemies; hitscan rays continue to the next enemy.</summary>
        public bool Pierce;
        /// <summary>Extra chain jumps (spells without a chain get a short one from the first enemy hit).</summary>
        public int ExtraChains;
        /// <summary>Horizontal aim offset in degrees (fanned shots).</summary>
        public float AimYaw;
        /// <summary>World offset of the area centre (novas around the caster, meteors around the target).</summary>
        public Vector3 Offset;
        /// <summary>This cast never lifesteals (extra copies keep lifesteal balance unchanged).</summary>
        public bool NoLifesteal;
        /// <summary>Called for every enemy this cast hits.</summary>
        public System.Action<Enemy> OnHit;

        public static readonly CastMods None = new CastMods();

        public CastMods Clone() => (CastMods)MemberwiseClone();
    }

    /// <summary>
    /// A spell or basic bolt about to be cast. Handlers of <see cref="SpellSystem.BeforeCast"/> may change it:
    /// how many copies go off (each with its own modifiers), the cooldown, a fizzle, or an echo.
    /// </summary>
    public class CastRequest
    {
        /// <summary>The spell, or null for the basic bolt.</summary>
        public SpellDefinition Spell;
        public bool IsBolt => Spell == null;
        /// <summary>One entry per copy that goes off. Starts with one unmodified copy.</summary>
        public readonly List<CastMods> Copies = new List<CastMods> { new CastMods() };
        public float CooldownMult = 1f;
        public bool ResetCooldown;
        /// <summary>The cast does nothing and spends no cooldown.</summary>
        public bool Fizzle;
        /// <summary>&gt; 0: the same copies go off again after this many seconds.</summary>
        public float EchoDelay;
        /// <summary>Called right after the copies were launched.</summary>
        public System.Action AfterLaunch;
        /// <summary>Text for logs ("Fireball + Split").</summary>
        public string Label;
    }
}
