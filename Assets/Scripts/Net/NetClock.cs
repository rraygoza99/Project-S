using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Timed hazards (fire jets, spike traps) on a shared clock in multiplayer, so the flames and spikes a
    /// client sees go up exactly when the host's do. Offline they keep their own timers as before.
    /// </summary>
    public static class NetClock
    {
        public static bool Shared => NetPlayers.Running && (NetworkServer.active || NetworkClient.isConnected);

        /// <summary>The hazard's time within its cycle: the shared clock plus an offset from its position.</summary>
        public static float Cycle(Vector3 position, float cycle, float t, float dt)
        {
            if (!Shared) return (t + dt) % cycle;
            float offset = Mathf.Abs(position.x * 0.731f + position.z * 0.377f) % cycle;
            return Mathf.Repeat((float)(NetworkTime.time % 100000.0) + offset, cycle);
        }
    }
}
