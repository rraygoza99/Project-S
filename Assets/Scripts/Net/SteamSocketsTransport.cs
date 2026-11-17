using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mirror;
using Steamworks;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Mirror transport over Steam Networking Sockets (peer to peer through Steam Datagram Relay, so no
    /// port forwarding and no IP addresses are shared). The client "address" is the host's SteamID64.
    /// </summary>
    public class SteamSocketsTransport : Transport
    {
        public const string Scheme = "steam";
        /// <summary>Steam's own limit for one reliable message, and a safe size for unreliable ones.</summary>
        const int MaxReliable = 512 * 1024, MaxUnreliable = 1200;

        HSteamListenSocket listen = HSteamListenSocket.Invalid;
        readonly Dictionary<int, HSteamNetConnection> conns = new Dictionary<int, HSteamNetConnection>();
        readonly Dictionary<HSteamNetConnection, int> ids = new Dictionary<HSteamNetConnection, int>();
        int nextId = 1;

        HSteamNetConnection client = HSteamNetConnection.Invalid;
        bool clientConnected;

        Callback<SteamNetConnectionStatusChangedCallback_t> statusChanged;
        readonly IntPtr[] inbox = new IntPtr[256];
        byte[] buffer = new byte[64 * 1024];

        public override bool Available() => SteamService.Ready;

        void EnsureCallback()
        {
            if (statusChanged == null) statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        }

        // ---------------- client ----------------

        public override bool ClientConnected() => clientConnected;

        public override void ClientConnect(string address)
        {
            if (!SteamService.Ready || !ulong.TryParse(address, out ulong hostId))
            {
                OnClientError?.Invoke(TransportError.DnsResolve, "Not a Steam host: " + address);
                OnClientDisconnected?.Invoke();
                return;
            }
            EnsureCallback();
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(hostId));
            client = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (clientConnected) Send(client, segment, channelId);
        }

        public override void ClientDisconnect()
        {
            if (client == HSteamNetConnection.Invalid) return;
            SteamNetworkingSockets.CloseConnection(client, 0, "leaving", false);
            client = HSteamNetConnection.Invalid;
            clientConnected = false;
            OnClientDisconnected?.Invoke();
        }

        public override void ClientEarlyUpdate()
        {
            if (client != HSteamNetConnection.Invalid && clientConnected)
                Receive(client, seg => OnClientDataReceived?.Invoke(seg, Channels.Reliable));
        }

        // ---------------- server ----------------

        public override Uri ServerUri() => new UriBuilder { Scheme = Scheme, Host = SteamService.MySteamId.ToString() }.Uri;
        public override bool ServerActive() => listen != HSteamListenSocket.Invalid;

        public override void ServerStart()
        {
            if (!SteamService.Ready || ServerActive()) return;
            EnsureCallback();
            listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (conns.TryGetValue(connectionId, out var h)) Send(h, segment, channelId);
        }

        public override void ServerDisconnect(int connectionId)
        {
            if (!conns.TryGetValue(connectionId, out var h)) return;
            SteamNetworkingSockets.CloseConnection(h, 0, "removed", true);
            Forget(h);
            OnServerDisconnected?.Invoke(connectionId);
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            if (!conns.TryGetValue(connectionId, out var h) || !SteamNetworkingSockets.GetConnectionInfo(h, out var info)) return "";
            return info.m_identityRemote.GetSteamID().m_SteamID.ToString();
        }

        public override void ServerStop()
        {
            foreach (var h in conns.Values) SteamNetworkingSockets.CloseConnection(h, 0, "host stopped", false);
            conns.Clear();
            ids.Clear();
            if (listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
            listen = HSteamListenSocket.Invalid;
        }

        public override void ServerEarlyUpdate()
        {
            if (!ServerActive()) return;
            foreach (var kv in new List<KeyValuePair<int, HSteamNetConnection>>(conns))
                Receive(kv.Value, seg => OnServerDataReceived?.Invoke(kv.Key, seg, Channels.Reliable));
        }

        public override int GetMaxPacketSize(int channelId = Channels.Reliable) =>
            channelId == Channels.Reliable ? MaxReliable : MaxUnreliable;

        public override void Shutdown()
        {
            ServerStop();
            ClientDisconnect();
        }

        // ---------------- connection events (from SteamAPI.RunCallbacks) ----------------

        void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t cb)
        {
            var h = cb.m_hConn;
            var state = cb.m_info.m_eState;
            bool closed = state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                       || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;

            if (h == client)
            {
                if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected && !clientConnected)
                {
                    clientConnected = true;
                    OnClientConnected?.Invoke();
                }
                else if (closed)
                {
                    SteamNetworkingSockets.CloseConnection(h, 0, "closed", false);
                    bool was = clientConnected;
                    client = HSteamNetConnection.Invalid;
                    clientConnected = false;
                    if (!was) OnClientError?.Invoke(TransportError.Timeout, "Couldn't reach the host: " + cb.m_info.m_szEndDebug);
                    OnClientDisconnected?.Invoke();
                }
                return;
            }

            if (cb.m_info.m_hListenSocket != listen || listen == HSteamListenSocket.Invalid) return;
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    SteamNetworkingSockets.AcceptConnection(h);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (ids.ContainsKey(h)) break;
                    int id = nextId++;
                    conns[id] = h;
                    ids[h] = id;
                    string addr = cb.m_info.m_identityRemote.GetSteamID().m_SteamID.ToString();
                    if (OnServerConnectedWithAddress != null) OnServerConnectedWithAddress(id, addr);
                    else OnServerConnected?.Invoke(id);
                    break;
                default:
                    if (!closed) break;
                    SteamNetworkingSockets.CloseConnection(h, 0, "closed", false);
                    if (ids.TryGetValue(h, out int gone))
                    {
                        Forget(h);
                        OnServerDisconnected?.Invoke(gone);
                    }
                    break;
            }
        }

        void Forget(HSteamNetConnection h)
        {
            if (!ids.TryGetValue(h, out int id)) return;
            ids.Remove(h);
            conns.Remove(id);
        }

        // ---------------- send / receive ----------------

        void Send(HSteamNetConnection h, ArraySegment<byte> segment, int channelId)
        {
            int flags = channelId == Channels.Reliable
                ? Constants.k_nSteamNetworkingSend_ReliableNoNagle
                : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
            var pin = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = pin.AddrOfPinnedObject() + segment.Offset;
                var r = SteamNetworkingSockets.SendMessageToConnection(h, ptr, (uint)segment.Count, flags, out long _);
                if (r != EResult.k_EResultOK && r != EResult.k_EResultIgnored)
                    Debug.LogWarning($"[Spellcaster] Steam send failed: {r}");
            }
            finally { pin.Free(); }
        }

        void Receive(HSteamNetConnection h, Action<ArraySegment<byte>> deliver)
        {
            int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(h, inbox, inbox.Length);
            for (int i = 0; i < n; i++)
            {
                var msg = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                int size = msg.m_cbSize;
                if (buffer.Length < size) buffer = new byte[Mathf.NextPowerOfTwo(size)];
                Marshal.Copy(msg.m_pData, buffer, 0, size);
                SteamNetworkingMessage_t.Release(inbox[i]);
                deliver(new ArraySegment<byte>(buffer, 0, size));
            }
        }
    }
}
