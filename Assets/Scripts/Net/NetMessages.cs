using Mirror;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>Client to host, right after connecting: who I am and which version I run.</summary>
    public struct HelloMsg : NetworkMessage
    {
        public string version;
        public ulong steamId;
        public string name;
        /// <summary>Who this player is across sessions (SteamID; a name in the debug direct mode).</summary>
        public string key;
    }

    /// <summary>Host to client: you can't join (or you were removed). The connection closes right after.</summary>
    public struct RejectMsg : NetworkMessage
    {
        public string reason;
    }

    /// <summary>One player in the lobby.</summary>
    public struct RosterEntry
    {
        public int id;          // the host's connection id for this player
        public ulong steamId;   // 0 in the debug direct-IP mode
        public string name;
        public string key;      // SteamID (debug: a name) - characters are kept by this
        public bool ready;
        public bool host;
    }

    /// <summary>Host to everyone: the lobby as it is now.</summary>
    public struct RosterMsg : NetworkMessage
    {
        public RosterEntry[] players;
        public int maxPlayers;
        public bool inRun;
        /// <summary>The receiving client's own entry id.</summary>
        public int yourId;
    }

    /// <summary>Client to host: ready or not.</summary>
    public struct ReadyMsg : NetworkMessage
    {
        public bool ready;
    }

    /// <summary>Host to everyone: the run starts with this seed and these players (slot order = colour).</summary>
    public struct StartRunMsg : NetworkMessage
    {
        public int seed;
        public RosterEntry[] players;
        /// <summary>The receiving client's own entry id.</summary>
        public int yourId;
        /// <summary>The spells every player starts with (ids); the host keeps each player's own copy.</summary>
        public string[] spells;
        /// <summary>The Grimoire is on for this run (the host's setting).</summary>
        public bool grimoire;
        /// <summary>The floor to start on (a resumed save, or joining a run in progress).</summary>
        public int floor;
        /// <summary>Joining a run in progress / the host is resuming its save.</summary>
        public bool join, resume;
    }

    // ---------------- combat (Phase 3) ----------------

    /// <summary>A visual or sound the host made, replayed on clients (no gameplay).</summary>
    public struct FxMsg : NetworkMessage
    {
        public byte kind;
        public int id, target;
        public Vector3 a, b;
        public Color color;
        public float f0, f1, f2;
        public string s;
    }

    /// <summary>Host to clients: an enemy appeared.</summary>
    public struct EnemySpawnMsg : NetworkMessage
    {
        public int id;
        public string type;
        public Vector3 pos;
        public float hp, maxHp;
        public bool boss;
    }

    /// <summary>One enemy as the host sees and draws it.</summary>
    public struct EnemyStateEntry
    {
        public int id;
        public Vector3 pos;
        public float hp, alpha, squash, lift;
        public byte anim, frame;
        public Color32 tint;
        public bool flash, dead, elite;
    }

    /// <summary>Host to clients, 30 times a second (unreliable): every enemy.</summary>
    public struct EnemyStatesMsg : NetworkMessage
    {
        public EnemyStateEntry[] enemies;
    }

    /// <summary>Host to clients: an enemy is gone (after its death animation).</summary>
    public struct EnemyGoneMsg : NetworkMessage
    {
        public int id;
    }

    /// <summary>Client to host: cast this spell (or the magic bolt) looking this way. The host checks it.</summary>
    public struct CastIntentMsg : NetworkMessage
    {
        public string spellId;   // null or empty = the magic bolt
        public float yaw, pitch;
        /// <summary>How long the spell was kept ready (Overcharge relic).</summary>
        public float heldFor;
    }

    /// <summary>Host to clients: a room's doors locked or opened, and whether it is cleared.</summary>
    public struct RoomGatesMsg : NetworkMessage
    {
        public string roomId;
        public bool locked, cleared;
    }

    /// <summary>Host to clients: a barrel, crate or crystal broke here.</summary>
    public struct PropBrokenMsg : NetworkMessage
    {
        public Vector3 pos;
        public byte kind;
    }

    /// <summary>One frame of a client's movement input (see Player.MoveInput).</summary>
    public struct InputFrame
    {
        public uint seq;
        public float dt, yaw, pitch;
        public Vector2 wish, autoDir;
        public byte flags;
    }

    /// <summary>Client to host, 30 times a second (unreliable): the inputs the host hasn't confirmed yet.</summary>
    public struct InputBatchMsg : NetworkMessage
    {
        public uint batch;          // counts batches (the host measures packet loss from gaps)
        public InputFrame[] frames;
    }

    /// <summary>One player's authoritative state from the host.</summary>
    public struct PlayerStateEntry
    {
        public int id;
        public uint lastSeq;          // the owner's last input the host applied
        public Vector3 pos, planar;
        public float vertical, decel, chill;
        public bool wasAuto, wasStill;
        public float yaw, pitch, hp;
        public bool casting;
        public bool downed, bledOut;
        public float bleed, revive;
    }

    /// <summary>Test only (-netreport): a client tells the host what it sees, twice a second.</summary>
    public struct DebugReportMsg : NetworkMessage
    {
        public Vector3 pos, otherPos, enemyPos;
        public float hp, lastError, enemyHp;
        public int players, corrections, states, enemies, fx, projectiles, gatesLocked, deck, decks, gold, items, consumables, spells, floor, pickups;
        public float maxHp;
        public string hand;
        public bool downed, bledOut, spectating;
        public int phase;
        public float rtt, lossIn;
    }

    /// <summary>Host to everyone, 30 times a second (unreliable): every player's state.</summary>
    public struct PlayerStatesMsg : NetworkMessage
    {
        public uint seq;            // counts state messages (clients measure packet loss from gaps)
        public PlayerStateEntry[] players;
    }

    /// <summary>Client to host: my run started (the host counts these).</summary>
    public struct RunStartedMsg : NetworkMessage
    {
        public int seed;
    }
}
