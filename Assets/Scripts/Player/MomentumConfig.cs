namespace Spellcaster
{
    /// <summary>How runes are entered (Options > Casting style). Saved with the settings.</summary>
    public enum CastingStyle
    {
        /// <summary>Hold the cast key: the view stops and the mouse draws the rune; W A S D walk slowly.</summary>
        Classic,
        /// <summary>Hold the cast key: you keep running the way you were moving, the mouse steers, W A S D type the rune.</summary>
        Momentum,
    }

    /// <summary>
    /// Tunables for the Momentum casting style. Change the numbers here (they are read every frame).
    /// </summary>
    public static class MomentumConfig
    {
        /// <summary>Forward speed while casting = walk speed (with items and haste) times this.</summary>
        public static float SpeedMultiplier = 1.0f;
        /// <summary>Seconds to reach full speed after pressing the cast key.</summary>
        public static float RampIn = 0.15f;
        /// <summary>Seconds to come to a stop (or back to walking) after letting go.</summary>
        public static float RampOut = 0.2f;
        /// <summary>
        /// Below this speed (m/s), with no movement key held, you count as standing still when you press the
        /// cast key: that cast doesn't run, you stay where you are and W A S D only type the rune.
        /// </summary>
        public static float StillSpeed = 0.5f;

        // ---- movement cue (Options > Momentum cue) ----
        /// <summary>Extra field of view, in degrees, at full casting speed.</summary>
        public static float CueFov = 4f;
        /// <summary>Head bob height in metres and steps per second at full casting speed.</summary>
        public static float CueBobHeight = 0.025f, CueBobRate = 9f;
    }
}
