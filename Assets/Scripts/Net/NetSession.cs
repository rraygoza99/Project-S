using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using kcp2k;
using Mirror;
using Steamworks;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// The online session: hosting, joining, the lobby roster (ready, kick), starting the run, Steam
    /// lobbies and invites. The lobby itself runs on Mirror messages, so it works the same over Steam
    /// and over the debug direct-IP transport. The host is a listen server (a player who also serves).
    /// Built once at launch and kept across scene reloads.
    /// </summary>
    public class NetSession : NetworkManager
    {
        public static NetSession Instance { get; private set; }

        public enum Kind { Steam, Direct }

        /// <summary>A Steam lobby found by the browser.</summary>
        public struct LobbyInfo
        {
            public CSteamID id;
            public string hostName, version;
            public int members, max;
            public bool friend;
            public bool VersionOk => version == NetConfig.Version;
        }

        // ---------------- state everyone sees ----------------

        public readonly List<RosterEntry> Roster = new List<RosterEntry>();
        public int MaxPlayers { get; private set; } = NetConfig.DefaultMaxPlayers;
        public bool InRun { get; private set; }
        public int MyId { get; private set; } = -1;
        public Kind Using { get; private set; }
        /// <summary>The last thing that went wrong (shown by the menu), or null.</summary>
        public string LastError { get; set; }
        /// <summary>What the session is doing right now ("Creating lobby...", "Connecting...").</summary>
        public string Status { get; private set; }

        public bool InSession => NetworkServer.active || NetworkClient.active;
        public bool IsHost => NetworkServer.active;
        /// <summary>Connected to someone else's game (never writes the run save).</summary>
        public static bool IsClientOnly => Instance && NetworkClient.active && !NetworkServer.active;
        public bool MyReady => Roster.Any(r => r.id == MyId && r.ready);
        public bool AllReady => Roster.Count >= 1 && Roster.All(r => r.ready || r.host);
        public CSteamID SteamLobby { get; private set; } = CSteamID.Nil;

        /// <summary>What starting the run does on each player (tests replace it).</summary>
        public static Action<StartRunMsg> StartRunHandler;
        /// <summary>How many players (host included) reported their run started (host only).</summary>
        public int StartAcks { get; private set; }

        KcpTransport kcp;
        SteamSocketsTransport steam;
        LatencySimulation lagSim;
        LobbyPrivacy privacy;
        bool leaving, everConnected;
        string rejectReason;

        // ---------------- creation ----------------

        /// <summary>Called by Bootstrap on every scene load; builds the session object only once.</summary>
        public static void Ensure()
        {
            if (Instance) return;
            SteamService.Init();
            NetSave.Register();
            var go = new GameObject("Network");
            go.SetActive(false); // configure before NetworkManager.Awake runs
            var k = go.AddComponent<KcpTransport>();
            k.Port = NetConfig.DebugPort;
            var s = go.AddComponent<SteamSocketsTransport>();
            var lat = go.AddComponent<LatencySimulation>();
            lat.wrap = k;
            lat.latency = 0f; lat.jitter = 0f; lat.unreliableLoss = 0f; lat.unreliableScramble = 0f;
            var ns = go.AddComponent<NetSession>();
            ns.kcp = k;
            ns.steam = s;
            ns.lagSim = lat;
            NetStats.Hook();
            ns.transport = k;
            ns.autoCreatePlayer = false;
            ns.playerPrefab = null;
            ns.dontDestroyOnLoad = true;
            ns.runInBackground = true;
            ns.sendRate = NetConfig.TickRate;
            ns.headlessStartMode = HeadlessStartOptions.DoNothing;
            ns.maxConnections = NetConfig.MaxPlayersLimit;
            Instance = ns;
            go.SetActive(true);
        }

        public override void Awake()
        {
            base.Awake();
            Instance = this;
            if (SteamService.Ready) HookSteam();
        }

        public override void Start()
        {
            base.Start();
            ReadLaunchArguments();
        }

        bool sessionWas;

        void Update()
        {
            // The pause switch follows the session (written only on change, so tests can set it too).
            bool on = InSession;
            if (on != sessionWas)
            {
                GameState.Multiplayer = on;
                // Players pass through enemies online: bodies a client sees slightly in the past mustn't block it.
                Physics.IgnoreLayerCollision(Layers.Player, Layers.EnemyBody, on);
            }
            sessionWas = on;
            NetPlayers.Tick();
            NetStats.Tick();
            NetOverlay.Update();
            NetEnemies.Tick();
            NetLoot.Tick();
            NetDowned.Tick();
            if (reportMode && NetPlayers.Running && NetworkClient.isConnected && (reportTimer -= Time.unscaledDeltaTime) <= 0f)
            {
                reportTimer = 0.5f;
                var me = Player.Instance;
                Player other = null;
                foreach (var p in Players.All) if (p && !p.IsLocal) { other = p; break; }
                if (me) NetworkClient.Send(new DebugReportMsg
                {
                    pos = me.transform.position, hp = me.HP, players = Players.All.Count,
                    corrections = NetPlayers.Corrections, lastError = NetPlayers.LastError, states = NetPlayers.StatesReceived,
                    otherPos = other ? other.transform.position : Vector3.zero,
                    enemies = NetEnemies.ProxyCount, fx = NetCombat.FxReceived, projectiles = NetCombat.ProjectilesSeen,
                    enemyPos = Enemy.All.Count > 0 ? Enemy.All[0].transform.position : Vector3.zero,
                    enemyHp = Enemy.All.Count > 0 ? Enemy.All[0].HP : 0f,
                    gatesLocked = Dungeon.Instance && Dungeon.Instance.Current != null ? Dungeon.Instance.Current.Doors.Count(d => d.Locked) : 0,
                    deck = Grimoire.GrimoireRun.Instance && Grimoire.GrimoireRun.Instance.Deck != null ? Grimoire.GrimoireRun.Instance.Deck.Count : 0,
                    hand = Grimoire.GrimoireRun.Instance && Grimoire.GrimoireRun.Instance.Deck != null
                        ? string.Join(",", Grimoire.GrimoireRun.Instance.Deck.Hand.Select(c => c.Def.Id)) : "",
                    decks = NetGrimoire.DecksReceived,
                    gold = Run.Instance ? Run.Instance.Gold : 0, items = Run.Instance ? Run.Instance.Items.Count : 0,
                    essence = Run.Instance ? Run.Instance.Essence : 0, forgesDone = Run.Instance ? Run.Instance.ForgesDone : 0,
                    consumables = Run.Instance ? Run.Instance.Consumables.Count(c => c != null) : 0,
                    spells = SpellBook.Known.Count, floor = Dungeon.Instance ? Dungeon.Instance.Floor : 0,
                    maxHp = me.MaxHP, pickups = NetLoot.ProxyCount,
                    downed = me.Downed, bledOut = me.BledOut, spectating = me.SpectateTarget != null,
                    phase = GameState.Instance ? (int)GameState.Instance.Phase : -1,
                    rtt = (float)(NetworkTime.rtt * 1000.0), lossIn = NetStats.Loss(-1).Percent, enemiesFresh = NetEnemies.FreshCount,
                    pedestals = Dungeon.Instance ? Dungeon.Instance.PedestalCount : 0, portalOpen = Dungeon.Instance && Dungeon.Instance.PortalOpen,
                });
            }
        }

        void OnGUI() => NetOverlay.Draw();

        /// <summary>Test only: the latest report from each client (host).</summary>
        public readonly Dictionary<int, DebugReportMsg> Reports = new Dictionary<int, DebugReportMsg>();
        bool reportMode, walkTest, combatTest, lootTest;
        /// <summary>Debug / tests (-netkey NAME): who this copy is, instead of its SteamID (several copies on one account).</summary>
        string debugKey;

        /// <summary>
        /// Test only (-nettestloot): take any item in reach, use a consumable once one arrives, and fuse the
        /// first two spells once a second one is learned.
        /// </summary>
        IEnumerator LootTest()
        {
            bool used = false, fused = false;
            var tried = new HashSet<int>();
            while (NetPlayers.Running)
            {
                var me = Player.Instance;
                var run = Run.Instance;
                if (me && run)
                {
                    var p = Pickup.NearestInteractable(me.transform.position);
                    if (p && p.NetId != 0 && tried.Add(p.NetId)) NetLoot.RequestTake(p, force: true);
                    if (!used && run.Consumables[0] != null) { used = true; run.UseConsumable(0); }
                    if (!fused && SpellBook.Known.Count == 2 && !SpellBook.Known.Any(SelfSpellCards.IsDash)) { fused = true; NetLoot.SendFuse(0, 1); } // not the dash the combat test was taught
                }
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        /// <summary>
        /// Test only (-nettestcombat): once the host's enemy shows up here, look at it and cast Fireball,
        /// then try to cast it again at once (the host must refuse), then fire the magic bolt.
        /// </summary>
        IEnumerator CombatTest()
        {
            while (NetEnemies.ProxyCount == 0) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            var me = Player.Instance;
            var target = Enemy.All.Count > 0 ? Enemy.All[0] : null;
            var ss = SpellSystem.Instance;
            var fireball = SpellBook.Known.FirstOrDefault(s => s.Id == "fireball");
            if (!me || !target || !ss || fireball == null) yield break;
            me.LookAt(target.Center);
            yield return null;
            ss.ReadyForTest(fireball);
            ss.CastReadied();
            NetCombat.SendCast(fireball); // a second cast straight away, ignoring the cooldown
            // Then a few magic bolts (one Fireball can miss a moving enemy at high ping).
            for (int i = 0; i < 4 && target && target.IsAlive; i++)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                if (target) { me.LookAt(target.Center); ss.FireBasic(); }
            }
            // Then Blink Dash, once the host teaches it: predicted here, played by the host.
            float dashWait = 0f;
            SpellDefinition dash = null;
            while (dashWait < 40f && (dash = SpellBook.Known.FirstOrDefault(s => s.Id == SelfSpellConfig.BlinkDashId)) == null)
            {
                dashWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (dash == null || !SpellSystem.Instance) yield break;
            yield return new WaitForSecondsRealtime(0.3f);
            SpellSystem.Instance.CastForTest(dash);
        }
        float reportTimer;

        /// <summary>Test only (-netwalk): a moment after the run starts, walk forward for two seconds.</summary>
        IEnumerator WalkTest()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (Player.Instance) Player.Instance.ForcedWish = Vector2.up;
            yield return new WaitForSecondsRealtime(2f);
            if (Player.Instance) Player.Instance.ForcedWish = null;
        }

        // ---------------- host ----------------

        /// <summary>Hosts over Steam: a listen server plus a Steam lobby friends can join or be invited to.</summary>
        public bool HostSteam(int maxPlayers, LobbyPrivacy who)
        {
            if (!SteamService.Ready) { LastError = SteamService.Error ?? "Steam isn't available."; return false; }
            if (InSession) return false;
            Prepare(Kind.Steam, maxPlayers);
            privacy = who;
            StartHost();
            Status = "Creating Steam lobby...";
            var type = who == LobbyPrivacy.Public ? ELobbyType.k_ELobbyTypePublic
                     : who == LobbyPrivacy.FriendsOnly ? ELobbyType.k_ELobbyTypeFriendsOnly
                     : ELobbyType.k_ELobbyTypePrivate;
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(type, MaxPlayers));
            return true;
        }

        /// <summary>Debug: hosts on a UDP port (other copies of the game join by IP).</summary>
        public bool HostDirect(ushort port = NetConfig.DebugPort, int maxPlayers = NetConfig.MaxPlayersLimit)
        {
            if (InSession) return false;
            Prepare(Kind.Direct, maxPlayers);
            kcp.Port = port;
            try { StartHost(); }
            catch (Exception e)
            {
                Leave();
                LastError = $"Couldn't host on port {port} (is another copy already hosting?) {e.Message}";
                return false;
            }
            Status = $"Hosting on port {port}";
            return true;
        }

        // ---------------- join ----------------

        /// <summary>Debug: joins a direct-IP host ("127.0.0.1" or "192.168.1.20:7777").</summary>
        public bool JoinDirect(string address)
        {
            if (InSession || string.IsNullOrWhiteSpace(address)) return false;
            Prepare(Kind.Direct, NetConfig.MaxPlayersLimit);
            address = address.Trim();
            int colon = address.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(address.Substring(colon + 1), out ushort port))
            {
                kcp.Port = port;
                address = address.Substring(0, colon);
            }
            else kcp.Port = NetConfig.DebugPort;
            networkAddress = address;
            Status = "Connecting to " + address + "...";
            StartClient();
            return true;
        }

        /// <summary>Joins a Steam lobby (browser, invite, or "Join game" in the friends list).</summary>
        public void JoinSteamLobby(CSteamID lobby)
        {
            if (!SteamService.Ready) { LastError = SteamService.Error; return; }
            if (InSession) Leave();
            LastError = null;
            Status = "Joining lobby...";
            SteamMatchmaking.JoinLobby(lobby);
        }

        void Prepare(Kind kind, int maxPlayers)
        {
            Using = kind;
            NetStats.Hook();
            transport = kind == Kind.Steam ? (Transport)steam : kcp;
            if (NetSim.Active)
            {
                // Debug: extra latency and loss on this copy's sends.
                lagSim.wrap = transport;
                lagSim.latency = NetSim.LatencyMs;
                lagSim.unreliableLoss = NetSim.LossPercent;
                lagSim.unreliableScramble = NetSim.LossPercent;
                lagSim.jitter = NetSim.LatencyMs > 0f ? 0.02f : 0f;
                transport = lagSim;
            }
            Transport.active = transport;
            MaxPlayers = Mathf.Clamp(maxPlayers, NetConfig.MinPlayers, NetConfig.MaxPlayersLimit);
            maxConnections = MaxPlayers;
            LastError = null;
            rejectReason = null;
            leaving = false;
            everConnected = false;
            InRun = false;
            StartAcks = 0;
            Roster.Clear();
            MyId = -1;
        }

        // ---------------- leave ----------------

        /// <summary>Leaves the session (the host ends it for everyone).</summary>
        public void Leave()
        {
            leaving = true;
            if (NetworkServer.active && NetworkClient.active) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
            if (SteamService.Ready && SteamLobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(SteamLobby);
            SteamLobby = CSteamID.Nil;
            Roster.Clear();
            MyId = -1;
            InRun = false;
            Status = null;
            NetPlayers.EndRun();
        }

        // ---------------- lobby actions ----------------

        public void SetReady(bool ready)
        {
            if (NetworkClient.isConnected) NetworkClient.Send(new ReadyMsg { ready = ready });
        }

        /// <summary>Host: removes a player from the lobby.</summary>
        public void Kick(int id)
        {
            if (!IsHost || id == MyId) return;
            if (NetworkServer.connections.TryGetValue(id, out var conn)) Reject(conn, "The host removed you from the lobby.");
        }

        /// <summary>Host: starts the run for everyone once all players are ready.</summary>
        public bool StartRun()
        {
            if (!IsHost || InRun || !AllReady) return false;
            InRun = true;
            StartAcks = 0;
            if (SteamService.Ready && SteamLobby != CSteamID.Nil) SteamMatchmaking.SetLobbyJoinable(SteamLobby, false);
            BroadcastRoster();
            int seed = RunRandom.NewSeed(), floor = 1;
            bool resume = ContinueSaved && SaveSystem.HasValidSave && ReadSave(out seed, out floor);
            if (!resume) { seed = RunRandom.NewSeed(); floor = 1; }
            var players = Roster.ToArray();
            var spells = SpellBook.StartingIds.Where(id => SpellBook.CreateBase(id) != null).ToArray(); // everyone starts like a new run
            foreach (var conn in NetworkServer.connections.Values)
                conn.Send(new StartRunMsg { seed = seed, players = players, yourId = conn.connectionId, spells = spells, grimoire = Settings.GrimoireEnabled, floor = floor, resume = resume });
            return true;
        }

        /// <summary>The saved run's seed and floor (the host resumes it).</summary>
        static bool ReadSave(out int seed, out int floor)
        {
            seed = 0; floor = 1;
            var f = SaveSystem.Read(SaveSystem.FilePath);
            if (f == null) return false;
            seed = f.runSeed;
            var dj = f.Get("dungeon");
            if (!string.IsNullOrEmpty(dj)) floor = Mathf.Max(1, JsonUtility.FromJson<DungeonSave>(dj).floor);
            return seed != 0;
        }

        /// <summary>
        /// Host, after a game over: everyone goes back to the lobby together (same group, ready up again,
        /// start a new run). Clients wait for the host on the death screen.
        /// </summary>
        public void BackToLobby()
        {
            if (!IsHost) return;
            InRun = false;
            for (int i = 0; i < Roster.Count; i++) { var r = Roster[i]; r.ready = r.host; Roster[i] = r; }
            if (SteamService.Ready && SteamLobby != CSteamID.Nil) SteamMatchmaking.SetLobbyJoinable(SteamLobby, true);
            NetworkServer.SendToAll(new BackToLobbyMsg());
            BroadcastRoster();
        }

        void OnBackToLobby(BackToLobbyMsg m)
        {
            InRun = false;
            NetPlayers.EndRun();
            if (GameState.Instance) GameState.Instance.ToLobby();
        }

        /// <summary>Host: opens the Steam overlay to invite friends to the lobby.</summary>
        public void OpenInviteOverlay()
        {
            if (SteamService.Ready && SteamLobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby);
        }

        // ---------------- server side ----------------

        public override void OnStartServer()
        {
            NetworkServer.ReplaceHandler<HelloMsg>(OnHello, false);
            NetworkServer.ReplaceHandler<ReadyMsg>(OnReady);
            NetworkServer.ReplaceHandler<RunStartedMsg>(OnRunStarted);
            NetworkServer.ReplaceHandler<InputBatchMsg>(NetPlayers.OnInputs);
            NetworkServer.ReplaceHandler<CastIntentMsg>(NetCombat.OnCastIntent);
            NetworkServer.ReplaceHandler<CycleMsg>(NetGrimoire.OnCycle);
            NetworkServer.ReplaceHandler<RewardPickMsg>(NetGrimoire.OnPick);
            NetworkServer.ReplaceHandler<PickupTakeMsg>(NetLoot.OnTake);
            NetworkServer.ReplaceHandler<UseItemMsg>(NetLoot.OnUseItem);
            NetworkServer.ReplaceHandler<ForgeMsg>(NetLoot.OnForge);
            NetworkServer.ReplaceHandler<DebugReportMsg>((c, m) => { if (AutoTest.Active) Reports[c.connectionId] = m; });
        }

        /// <summary>Why a player can't join, or null. A run in progress only takes players coming back. (Also the test hook.)</summary>
        public static string Validate(HelloMsg hello, int playersNow, int maxPlayers, bool inRun, bool comingBack = false)
        {
            if (hello.version != NetConfig.Version)
                return $"Version mismatch: the host runs {NetConfig.Version}, you run {hello.version}. Update the game to join.";
            if (inRun && !comingBack) return "That game's run is already in progress.";
            if (playersNow >= maxPlayers) return "That lobby is full.";
            return null;
        }

        void OnHello(NetworkConnectionToClient conn, HelloMsg hello)
        {
            if (Roster.Any(r => r.id == conn.connectionId)) return;
            bool comingBack = InRun && NetSave.HasCharacter(hello.key) && !Players.All.Any(p => p && p.NetKey == hello.key);
            string reason = Validate(hello, Roster.Count, MaxPlayers, InRun, comingBack);
            if (reason != null) { Reject(conn, reason); return; }
            bool isHost = conn is LocalConnectionToClient;
            var entry = new RosterEntry
            {
                id = conn.connectionId, steamId = hello.steamId, ready = isHost || InRun, host = isHost, key = hello.key,
                name = string.IsNullOrWhiteSpace(hello.name) ? "Player " + conn.connectionId : hello.name.Trim(),
            };
            Roster.Add(entry);
            if (InRun && NetPlayers.Running) NetSave.JoinInProgress(conn, entry, FreeSlot());
            BroadcastRoster();
            Debug.Log($"[Spellcaster] {hello.name} joined the lobby ({Roster.Count}/{MaxPlayers})");
        }

        void OnReady(NetworkConnectionToClient conn, ReadyMsg msg)
        {
            int i = Roster.FindIndex(r => r.id == conn.connectionId);
            if (i < 0 || Roster[i].host) return;
            var e = Roster[i];
            e.ready = msg.ready;
            Roster[i] = e;
            BroadcastRoster();
        }

        void OnRunStarted(NetworkConnectionToClient conn, RunStartedMsg msg) => StartAcks++;

        /// <summary>The lowest colour slot nobody uses.</summary>
        int FreeSlot()
        {
            for (int i = 0; i < PlayerColors.Count; i++)
                if (!Players.All.Any(p => p && p.NetId >= 0 && p.ColorIndex == i)) return i;
            return Roster.Count;
        }

        /// <summary>Host (lobby): start by resuming the saved run instead of a new one.</summary>
        public bool ContinueSaved;

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            int removed = Roster.RemoveAll(r => r.id == conn.connectionId);
            if (NetPlayers.Running)
            {
                NetSave.Departed(Players.ByNetId(conn.connectionId)); // kept in case they come back
                NetPlayers.RemovePlayer(conn.connectionId);
            }
            base.OnServerDisconnect(conn);
            if (removed > 0) BroadcastRoster();
        }

        void Reject(NetworkConnectionToClient conn, string reason)
        {
            conn.Send(new RejectMsg { reason = reason });
            StartCoroutine(DisconnectSoon(conn));
        }

        IEnumerator DisconnectSoon(NetworkConnectionToClient conn)
        {
            yield return new WaitForSecondsRealtime(0.3f); // let the reason arrive first
            conn.Disconnect();
        }

        void BroadcastRoster()
        {
            var players = Roster.ToArray();
            foreach (var conn in NetworkServer.connections.Values)
                conn.Send(new RosterMsg { players = players, maxPlayers = MaxPlayers, inRun = InRun, yourId = conn.connectionId });
            if (SteamService.Ready && SteamLobby != CSteamID.Nil)
                SteamMatchmaking.SetLobbyData(SteamLobby, "players", Roster.Count.ToString());
        }

        // ---------------- client side ----------------

        public override void OnStartClient()
        {
            NetworkClient.ReplaceHandler<RosterMsg>(OnRoster, false);
            NetworkClient.ReplaceHandler<RejectMsg>(OnReject, false);
            NetworkClient.ReplaceHandler<StartRunMsg>(OnStartRunMsg, false);
            NetworkClient.ReplaceHandler<BackToLobbyMsg>(OnBackToLobby, false);
            NetworkClient.ReplaceHandler<PlayerStatesMsg>(NetPlayers.OnStates, false);
            NetworkClient.ReplaceHandler<FxMsg>(NetCombat.OnFx, false);
            NetworkClient.ReplaceHandler<EnemySpawnMsg>(NetEnemies.OnSpawn, false);
            NetworkClient.ReplaceHandler<EnemyStatesMsg>(NetEnemies.OnStates, false);
            NetworkClient.ReplaceHandler<EnemyGoneMsg>(NetEnemies.OnGone, false);
            NetworkClient.ReplaceHandler<RoomGatesMsg>(NetCombat.OnGates, false);
            NetworkClient.ReplaceHandler<PropBrokenMsg>(NetCombat.OnPropBroken, false);
            NetworkClient.ReplaceHandler<DeckStateMsg>(NetGrimoire.OnDeckState, false);
            NetworkClient.ReplaceHandler<RewardOfferMsg>(NetGrimoire.OnOffer, false);
            NetworkClient.ReplaceHandler<PickupSpawnMsg>(NetLoot.OnSpawn, false);
            NetworkClient.ReplaceHandler<PickupGoneMsg>(NetLoot.OnGone, false);
            NetworkClient.ReplaceHandler<DenyMsg>(NetLoot.OnDeny, false);
            NetworkClient.ReplaceHandler<RunStateMsg>(NetLoot.OnRunState, false);
            NetworkClient.ReplaceHandler<SpellBookMsg>(NetLoot.OnBook, false);
            NetworkClient.ReplaceHandler<FloorMsg>(NetLoot.OnFloor, false);
            NetworkClient.ReplaceHandler<CooldownResetMsg>(NetLoot.OnCooldownReset, false);
            NetworkClient.ReplaceHandler<RunOverMsg>(NetDowned.OnRunOver, false);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            everConnected = true;
            Status = null;
            string name = SteamService.MyName;
            if (string.IsNullOrEmpty(name)) name = (NetworkServer.active ? "Host " : "Player ") + UnityEngine.Random.Range(100, 999);
            string key = debugKey ?? (SteamService.MySteamId != 0 ? SteamService.MySteamId.ToString() : name);
            NetworkClient.Send(new HelloMsg { version = NetConfig.Version, steamId = SteamService.MySteamId, name = name, key = key });
        }

        void OnRoster(RosterMsg msg)
        {
            Roster.Clear();
            if (msg.players != null) Roster.AddRange(msg.players);
            MaxPlayers = msg.maxPlayers;
            InRun = msg.inRun;
            MyId = msg.yourId;
            if (autoReady && !MyReady) SetReady(true);
            // Someone left during the run: their character goes; someone (re)joined: theirs appears.
            if (NetPlayers.Running && !NetworkServer.active)
            {
                foreach (var p in Players.All.ToArray())
                    if (p && !p.IsLocal && p.NetId >= 0 && !Roster.Any(r => r.id == p.NetId)) NetPlayers.RemovePlayer(p.NetId);
                for (int i = 0; i < Roster.Count; i++)
                    if (Roster[i].id != MyId && !Players.ByNetId(Roster[i].id))
                        NetPlayers.AddRemote(Roster[i], i, Player.Instance ? Player.Instance.transform.position : Vector3.zero);
            }
        }

        void OnReject(RejectMsg msg)
        {
            rejectReason = msg.reason;
            LastError = msg.reason;
        }

        void OnStartRunMsg(StartRunMsg msg)
        {
            InRun = true;
            (StartRunHandler ?? DefaultStartRun)(msg);
            NetworkClient.Send(new RunStartedMsg { seed = msg.seed });
            if (quitAfterStart) StartCoroutine(QuitSoon());
            if (walkTest) StartCoroutine(WalkTest());
            if (combatTest) StartCoroutine(CombatTest());
            if (lootTest) StartCoroutine(LootTest());
        }

        static void DefaultStartRun(StartRunMsg msg)
        {
            if (MainMenu.Instance) MainMenu.Instance.BeginNetworkRun(msg);
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            bool wasLeaving = leaving;
            if (!wasLeaving)
            {
                LastError = rejectReason ?? (everConnected ? "The host left the game." : "Couldn't connect to the host.");
                Debug.Log("[Spellcaster] disconnected: " + LastError);
            }
            if (SteamService.Ready && SteamLobby != CSteamID.Nil && !NetworkServer.active)
            {
                SteamMatchmaking.LeaveLobby(SteamLobby);
                SteamLobby = CSteamID.Nil;
            }
            Roster.Clear();
            MyId = -1;
            Status = null;
            NetPlayers.EndRun();
            bool inGame = GameState.Instance && GameState.Instance.InRun;
            InRun = false;
            if (!wasLeaving && inGame) GameState.Instance.ToMenu(); // the session is over: back to the menu with the reason
        }

        public override void OnClientError(TransportError error, string reason)
        {
            if (!leaving && rejectReason == null && !everConnected) LastError = "Couldn't connect to the host. " + reason;
        }

        // ---------------- Steam lobbies, invites, browser ----------------

        CallResult<LobbyCreated_t> lobbyCreated;
        CallResult<LobbyMatchList_t> lobbyList;
        Callback<LobbyEnter_t> lobbyEntered;
        Callback<GameLobbyJoinRequested_t> joinRequested;
        Callback<LobbyDataUpdate_t> lobbyData;

        public readonly List<LobbyInfo> Found = new List<LobbyInfo>();
        public bool Searching { get; private set; }

        void HookSteam()
        {
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => JoinSteamLobby(r.m_steamIDLobby));
            lobbyData = Callback<LobbyDataUpdate_t>.Create(r => RefreshFriendLobbies());
        }

        void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                LastError = "Steam couldn't create the lobby (" + r.m_eResult + ").";
                Leave();
                return;
            }
            SteamLobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(SteamLobby, NetConfig.LobbyGameKey, NetConfig.LobbyGameValue);
            SteamMatchmaking.SetLobbyData(SteamLobby, "version", NetConfig.Version);
            SteamMatchmaking.SetLobbyData(SteamLobby, "host", SteamService.MySteamId.ToString());
            SteamMatchmaking.SetLobbyData(SteamLobby, "name", SteamService.MyName ?? "Host");
            SteamMatchmaking.SetLobbyData(SteamLobby, "max", MaxPlayers.ToString());
            SteamMatchmaking.SetLobbyData(SteamLobby, "players", Roster.Count.ToString());
            Status = null;
            Debug.Log($"[Spellcaster] Steam lobby {SteamLobby} ({privacy}, {MaxPlayers} players)");
        }

        void OnLobbyEntered(LobbyEnter_t r)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (NetworkServer.active) return; // the host's own lobby
            if (r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                LastError = r.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull
                    ? "That lobby is full." : "Couldn't join the lobby (it may have closed).";
                Status = null;
                return;
            }
            string version = SteamMatchmaking.GetLobbyData(lobby, "version");
            if (version != NetConfig.Version)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                LastError = $"Version mismatch: the host runs {version}, you run {NetConfig.Version}.";
                Status = null;
                return;
            }
            string host = SteamMatchmaking.GetLobbyData(lobby, "host");
            if (string.IsNullOrEmpty(host)) host = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID.ToString();
            Prepare(Kind.Steam, NetConfig.MaxPlayersLimit);
            SteamLobby = lobby;
            networkAddress = host;
            Status = "Connecting to the host...";
            StartClient();
        }

        /// <summary>Looks for games: friends playing in a lobby, plus public Spellcaster lobbies.</summary>
        public void SearchLobbies()
        {
            Found.Clear();
            if (!SteamService.Ready) return;
            Searching = true;
            RefreshFriendLobbies();
            SteamMatchmaking.AddRequestLobbyListStringFilter(NetConfig.LobbyGameKey, NetConfig.LobbyGameValue, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            lobbyList.Set(SteamMatchmaking.RequestLobbyList());
        }

        void RefreshFriendLobbies()
        {
            if (!SteamService.Ready) return;
            var flags = EFriendFlags.k_EFriendFlagImmediate;
            int n = SteamFriends.GetFriendCount(flags);
            for (int i = 0; i < n; i++)
            {
                var f = SteamFriends.GetFriendByIndex(i, flags);
                if (!SteamFriends.GetFriendGamePlayed(f, out var game) || !game.m_steamIDLobby.IsValid()) continue;
                if (game.m_gameID.AppID().m_AppId != NetConfig.SteamAppId) continue;
                var lobby = game.m_steamIDLobby;
                if (SteamMatchmaking.GetLobbyData(lobby, NetConfig.LobbyGameKey) != NetConfig.LobbyGameValue)
                {
                    SteamMatchmaking.RequestLobbyData(lobby); // the answer comes back as LobbyDataUpdate
                    continue;
                }
                AddFound(lobby, true);
            }
        }

        void OnLobbyList(LobbyMatchList_t r, bool ioFailure)
        {
            Searching = false;
            if (ioFailure) return;
            for (int i = 0; i < r.m_nLobbiesMatching; i++) AddFound(SteamMatchmaking.GetLobbyByIndex(i), false);
        }

        void AddFound(CSteamID lobby, bool friend)
        {
            if (Found.Any(f => f.id == lobby)) return;
            int.TryParse(SteamMatchmaking.GetLobbyData(lobby, "max"), out int max);
            Found.Add(new LobbyInfo
            {
                id = lobby, friend = friend,
                hostName = SteamMatchmaking.GetLobbyData(lobby, "name"),
                version = SteamMatchmaking.GetLobbyData(lobby, "version"),
                members = SteamMatchmaking.GetNumLobbyMembers(lobby),
                max = max > 0 ? max : SteamMatchmaking.GetLobbyMemberLimit(lobby),
            });
        }

        // ---------------- launch arguments ----------------

        bool autoReady, quitAfterStart;

        /// <summary>
        /// "+connect_lobby id" (Steam's "Join game" while the game was closed) joins that lobby.
        /// Debug / tests: "-netjoin address" joins a direct-IP host, "-netready" readies up on arrival,
        /// "-netquitafterstart" quits a moment after the run starts.
        /// </summary>
        void ReadLaunchArguments()
        {
            var args = Environment.GetCommandLineArgs();
            string joinAddress = null;
            ulong joinLobby = 0;
            float lagMs = 0f, lossPct = 0f;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "+connect_lobby" && ulong.TryParse(next, out ulong lobby)) joinLobby = lobby;
                else if (a == "-netjoin" && next != null) joinAddress = next;
                else if (a == "-netlag" && float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ms)) lagMs = ms;
                else if (a == "-netloss" && float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lp)) lossPct = lp;
                else if (a == "-netoverlay") NetOverlay.Shown = true;
                else if (a == "-netready") autoReady = true;
                else if (a == "-netquitafterstart") quitAfterStart = true;
                else if (a == "-netreport") reportMode = true;
                else if (a == "-netwalk") walkTest = true;
                else if (a == "-nettestcombat") { combatTest = true; NetGrimoire.autoTake = true; }
                else if (a == "-nettestloot") lootTest = true;
                else if (a == "-netkey" && next != null) debugKey = next;
            }
            // Settings first, then connect.
            if (lagMs > 0f || lossPct > 0f) NetSim.Set(lagMs, lossPct);
            if (joinLobby != 0 && SteamService.Ready) JoinSteamLobby(new CSteamID(joinLobby));
            else if (joinAddress != null) JoinDirect(joinAddress);
        }

        IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Leave();
            Application.Quit();
        }
    }
}
