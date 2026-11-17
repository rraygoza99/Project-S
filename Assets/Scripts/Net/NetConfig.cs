using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>Who can join a hosted Steam lobby.</summary>
    public enum LobbyPrivacy { FriendsOnly, Public, InviteOnly }

    /// <summary>
    /// Every online setting in one place. To ship on Steam, change <see cref="SteamAppId"/> to the game's
    /// own App ID (and the build writes it into steam_appid.txt next to the exe).
    /// </summary>
    public static class NetConfig
    {
        /// <summary>480 = Spacewar, Valve's shared test app, for development.</summary>
        public const uint SteamAppId = 480;

        public const int MinPlayers = 2, MaxPlayersLimit = 6, DefaultMaxPlayers = 4;
        /// <summary>Network updates per second (Mirror send rate).</summary>
        public const int TickRate = 30;

        /// <summary>Debug-only direct connection (several copies of the game on one PC, or on a LAN).</summary>
        public const ushort DebugPort = 7777;
        public const string DebugAddress = "127.0.0.1";

        /// <summary>Steam lobby data key that marks our lobbies (App 480 is shared with other games).</summary>
        public const string LobbyGameKey = "spellcaster_game";
        public const string LobbyGameValue = "spellcaster";

        /// <summary>Seconds between the first player stepping into a fight room and the doors closing (the others are pulled in).</summary>
        public const float GatherSeconds = 3f;

        /// <summary>
        /// Difficulty by player count (index = players): enemy HP multiplier and how many enemies per wave
        /// (x the single-player wave). Bosses only get the HP. Tune here.
        /// </summary>
        public static readonly (float hp, float count)[] Scaling =
        {
            (1f, 1f),      // (unused)
            (1f, 1f),      // 1 player
            (1.4f, 1.35f), // 2
            (1.75f, 1.7f), // 3
            (2.1f, 2.0f),  // 4
            (2.4f, 2.3f),  // 5
            (2.7f, 2.6f),  // 6
        };
        public static (float hp, float count) ScaleFor(int players) => Scaling[Mathf.Clamp(players, 1, Scaling.Length - 1)];

        /// <summary>Players must run the same version to play together.</summary>
        public static string Version => Application.version;

        /// <summary>The direct-IP mode is a debug tool: shown with Options > Dev mode on (or in debug builds).</summary>
        public static bool DebugTransportAllowed => Settings.DevMode || Debug.isDebugBuild || AutoTest.Active;
    }
}
