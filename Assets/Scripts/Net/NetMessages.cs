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
    }

    /// <summary>Test only (-netreport): a client tells the host what it sees, twice a second.</summary>
    public struct DebugReportMsg : NetworkMessage
    {
        public Vector3 pos, otherPos;
        public float hp, lastError;
        public int players, corrections, states;
    }

    /// <summary>Host to everyone, 30 times a second (unreliable): every player's state.</summary>
    public struct PlayerStatesMsg : NetworkMessage
    {
        public PlayerStateEntry[] players;
    }

    /// <summary>Client to host: my run started (the host counts these).</summary>
    public struct RunStartedMsg : NetworkMessage
    {
        public int seed;
    }
}
