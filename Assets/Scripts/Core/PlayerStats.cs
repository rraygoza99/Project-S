namespace Spellcaster
{
    /// <summary>
    /// Everything items can change about the wizard. Passive items edit the base stats; relics flip on
    /// the spell modifiers at the bottom, which the spell code reads when a spell resolves.
    /// </summary>
    public class PlayerStats
    {
        public float MaxHP = 100f;
        public float MoveSpeed = 6.5f;
        /// <summary>Multiplier on all spell damage, burn, shatter and healing.</summary>
        public float Power = 1f;
        /// <summary>Multiplier on spell cooldowns (lower is better).</summary>
        public float CooldownMult = 1f;
        public float BoltDamage = 10f;
        public float BoltInterval = 0.45f;
        /// <summary>Multiplier on damage the player takes (lower is better).</summary>
        public float DamageTaken = 1f;
        /// <summary>Raises drop chances: 0.5 = +50%.</summary>
        public float Luck;
        public float HealOnKill;

        // ---- relic modifiers ----
        public int ExtraChains;
        public int ProjectileSplit;
        /// <summary>Explosion and nova radius multiplier.</summary>
        public float AreaMult = 1f;
        public float EchoChance;
        public float SpellLifesteal;
        public float IgniteOnHit;
        public float BoltFreezeChance;
        public bool VolatileKills;
        public bool FrostSpread;
    }
}
