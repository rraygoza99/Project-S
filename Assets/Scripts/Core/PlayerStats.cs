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

        // ---- unlockable items (meta progression) ----
        /// <summary>Damage dealt back to enemies that hit you in melee.</summary>
        public float Thorns;
        /// <summary>Lethal hits survived (Phoenix Feather), used up one at a time.</summary>
        public int Revives;
        public int BoltSplit;
        /// <summary>Extra gold per coin picked up.</summary>
        public int CoinBonus;
        public float PoisonOnHit;
        /// <summary>Every Nth spell cast has no cooldown (0 = off).</summary>
        public int FreeCastEvery;
        /// <summary>A spell kept ready for 1.5 s hits 50% harder.</summary>
        public bool Overcharge;
        /// <summary>Getting hurt freezes enemies around you (8 s cooldown).</summary>
        public bool FrostOnHurt;
        /// <summary>Extra spell damage against burning or poisoned enemies.</summary>
        public float HexBonus;
    }
}
