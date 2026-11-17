using Mirror;

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

    /// <summary>Host to everyone: the run starts with this seed.</summary>
    public struct StartRunMsg : NetworkMessage
    {
        public int seed;
    }

    /// <summary>Client to host: my run started (the host counts these).</summary>
    public struct RunStartedMsg : NetworkMessage
    {
        public int seed;
    }
}
