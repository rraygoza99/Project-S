using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Spellcaster.Net
{
    /// <summary>
    /// Starts Steamworks once at launch. If Steam isn't running (or the library is missing), the game
    /// carries on: single-player works and the multiplayer menu shows <see cref="Error"/>.
    /// </summary>
    public static class SteamService
    {
        public static bool Ready { get; private set; }
        /// <summary>Why online play is unavailable (null while Steam works).</summary>
        public static string Error { get; private set; }
        static bool tried;

        public static ulong MySteamId => Ready ? SteamUser.GetSteamID().m_SteamID : 0UL;
        public static string MyName => Ready ? SteamFriends.GetPersonaName() : null;

        public static void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                if (!Packsize.Test()) { Error = "Steam library mismatch (wrong platform build)."; return; }
                if (!DllCheck.Test()) { Error = "Steam library files are the wrong version."; return; }
                // Outside the Steam client, the App ID comes from the environment or steam_appid.txt.
                Environment.SetEnvironmentVariable("SteamAppId", NetConfig.SteamAppId.ToString());
                Environment.SetEnvironmentVariable("SteamGameId", NetConfig.SteamAppId.ToString());
                var result = SteamAPI.InitEx(out string msg);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    Error = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                        ? "Steam isn't running. Start Steam and log in to play online."
                        : "Steam couldn't start" + (string.IsNullOrEmpty(msg) ? "." : ": " + msg);
                    Debug.Log("[Spellcaster] Steam not available: " + Error);
                    return;
                }
                Ready = true;
                SteamNetworkingUtils.InitRelayNetworkAccess();
                var go = new GameObject("Steam");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<SteamRunner>();
                Debug.Log($"[Spellcaster] Steam ready: {MyName} (app {SteamUtils.GetAppID()})");
            }
            catch (DllNotFoundException e)
            {
                Error = "Steam library missing (steam_api64.dll).";
                Debug.LogWarning("[Spellcaster] " + Error + " " + e.Message);
            }
            catch (Exception e)
            {
                Error = "Steam couldn't start: " + e.Message;
                Debug.LogWarning("[Spellcaster] " + Error);
            }
        }

        public static string NameOf(ulong steamId)
        {
            if (!Ready || steamId == 0) return null;
            return SteamFriends.GetFriendPersonaName(new CSteamID(steamId));
        }

        // ---------------- avatars ----------------

        static readonly Dictionary<ulong, Texture2D> avatars = new Dictionary<ulong, Texture2D>();

        /// <summary>A player's Steam avatar, or null while it loads (call again next frame). Draw it flipped (rows come top-down).</summary>
        public static Texture2D Avatar(ulong steamId)
        {
            if (!Ready || steamId == 0) return null;
            if (avatars.TryGetValue(steamId, out var t)) return t;
            int handle = SteamFriends.GetLargeFriendAvatar(new CSteamID(steamId));
            if (handle <= 0 || !SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0) return null;
            var rgba = new byte[w * h * 4];
            if (!SteamUtils.GetImageRGBA(handle, rgba, rgba.Length)) return null;
            t = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            t.LoadRawTextureData(rgba);
            t.Apply();
            avatars[steamId] = t;
            return t;
        }

        /// <summary>Keeps Steam callbacks flowing and shuts Steam down on exit.</summary>
        class SteamRunner : MonoBehaviour
        {
            void Update() { if (Ready) SteamAPI.RunCallbacks(); }
            void OnApplicationQuit()
            {
                if (!Ready) return;
                Ready = false;
                SteamAPI.Shutdown();
            }
        }
    }
}
