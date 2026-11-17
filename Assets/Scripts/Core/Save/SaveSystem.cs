using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>The whole save file: a header plus one JSON block per system ("section").</summary>
    [Serializable]
    public class SaveFile
    {
        public int saveVersion;
        public string gameVersion;
        public string timestamp;
        public int runSeed;
        public List<SaveSectionData> sections = new List<SaveSectionData>();

        public string Get(string key) => sections.FirstOrDefault(s => s.key == key)?.json;
    }

    [Serializable]
    public class SaveSectionData
    {
        public string key;
        public string json;
    }

    /// <summary>One system's part of the save. Register it with SaveSystem.Register (see the README).</summary>
    public interface ISaveSection
    {
        /// <summary>Stable name of the block in the file ("dungeon", "run"...). Never rename it.</summary>
        string Key { get; }
        /// <summary>Load order: lower first (the dungeon is rebuilt before the player is put back in it).</summary>
        int Order { get; }
        /// <summary>The system's state as JSON, or null to leave the block out.</summary>
        string Save();
        void Load(string json);
    }

    /// <summary>A section from a plain [Serializable] data class and two functions.</summary>
    public sealed class SaveSection<T> : ISaveSection where T : class
    {
        readonly Func<T> save;
        readonly Action<T> load;
        public string Key { get; }
        public int Order { get; }

        public SaveSection(string key, int order, Func<T> save, Action<T> load)
        {
            Key = key;
            Order = order;
            this.save = save;
            this.load = load;
        }

        public string Save()
        {
            var data = save();
            return data == null ? null : JsonUtility.ToJson(data);
        }

        public void Load(string json) => load(JsonUtility.FromJson<T>(json));
    }

    /// <summary>
    /// One save slot for the current run, in Application.persistentDataPath/save.json.
    /// Writes are atomic (temp file, then replace, keeping the previous file as save.json.bak).
    /// Loading migrates older versions, falls back to the backup if the file is broken, and never throws:
    /// on failure it sets LastError and Continue is hidden.
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>Bump when the format changes, and add a migration from the previous version below.</summary>
        public const int Version = 1;

        /// <summary>saveVersion n -> n + 1. Example: { 1, f => { /* rename a key, fill a default */ } }.</summary>
        static readonly Dictionary<int, Action<SaveFile>> Migrations = new Dictionary<int, Action<SaveFile>>();

        static readonly List<ISaveSection> sections = new List<ISaveSection>();

        /// <summary>Adds (or replaces, by key) a system's section. Systems call this when they are created.</summary>
        public static void Register(ISaveSection section)
        {
            sections.RemoveAll(s => s.Key == section.Key);
            sections.Add(section);
        }

        public static IEnumerable<string> SectionKeys => sections.OrderBy(s => s.Order).Select(s => s.Key);

        // ---------------- files ----------------

        /// <summary>The self-test uses its own folder, so it never touches a player's save.</summary>
        public static string Folder => AutoTest.Active ? Path.Combine(Application.persistentDataPath, "autotest") : Application.persistentDataPath;
        public static string FilePath => Path.Combine(Folder, "save.json");
        public static string BackupPath => Path.Combine(Folder, "save.json.bak");
        static string TempPath => Path.Combine(Folder, "save.json.tmp");

        /// <summary>Why the last load or save failed (shown on the main menu), or null.</summary>
        public static string LastError { get; private set; }
        /// <summary>Unscaled time of the last successful save (the "Saving..." icon).</summary>
        public static float LastSaveTime { get; private set; } = -10f;
        /// <summary>The save exists but couldn't be applied: Continue stays hidden until the next save.</summary>
        static bool broken;
        static int validCache = -1;
        static string cacheFolder, describeCache;

        /// <summary>A save that reads and migrates fine exists (the file or its backup). Cached until the next save or delete.</summary>
        public static bool HasValidSave
        {
            get
            {
                if (broken) return false;
                if (cacheFolder != Folder) { cacheFolder = Folder; validCache = -1; }
                if (validCache < 0)
                {
                    validCache = (Read(FilePath) ?? Read(BackupPath)) != null ? 1 : 0;
                    describeCache = null;
                }
                return validCache == 1;
            }
        }

        /// <summary>Saving makes sense now: a real run is on (not the tutorial or the practice tool).</summary>
        public static bool CanSave
        {
            get
            {
                var gs = GameState.Instance;
                return gs && gs.InRun && !GameState.PracticeMode && !GameState.TutorialMode && !GameState.SaveProtected && Player.Instance && !Player.Instance.IsDead
                    && !Net.NetSession.IsClientOnly; // in multiplayer only the host saves
            }
        }

        public static SaveFile Capture()
        {
            var f = new SaveFile
            {
                saveVersion = Version,
                gameVersion = Application.version,
                timestamp = DateTime.UtcNow.ToString("o"),
                runSeed = RunRandom.RunSeed,
            };
            foreach (var s in sections.OrderBy(s => s.Order))
            {
                string json = s.Save();
                if (json != null) f.sections.Add(new SaveSectionData { key = s.Key, json = json });
            }
            return f;
        }

        public static string ToJson(SaveFile f) => JsonUtility.ToJson(f, true);

        /// <summary>Writes the current run. Returns false (and sets LastError) instead of throwing.</summary>
        public static bool Save()
        {
            try
            {
                WriteAtomic(ToJson(Capture()));
                LastSaveTime = Time.unscaledTime;
                LastError = null;
                broken = false;
                validCache = -1;
                Debug.Log("[Spellcaster] run saved to " + FilePath);
                return true;
            }
            catch (Exception e)
            {
                LastError = "Couldn't save the run: " + e.Message;
                Debug.LogError("[Spellcaster] " + LastError);
                return false;
            }
        }

        static void WriteAtomic(string json)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(TempPath, json);
            if (!File.Exists(FilePath)) { File.Move(TempPath, FilePath); return; }
            try
            {
                File.Replace(TempPath, FilePath, BackupPath); // atomic swap; the old file becomes the backup
            }
            catch (Exception)
            {
                // Some file systems can't replace: keep a backup by hand, then move into place.
                File.Copy(FilePath, BackupPath, true);
                File.Delete(FilePath);
                File.Move(TempPath, FilePath);
            }
        }

        /// <summary>Parses and migrates a file; null if it's missing, unreadable, from a newer game or can't be migrated.</summary>
        public static SaveFile Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Spellcaster] save {path} unreadable: {e.Message}");
                return null;
            }
        }

        public static SaveFile Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            SaveFile f;
            try { f = JsonUtility.FromJson<SaveFile>(text); }
            catch (Exception) { return null; }
            if (f == null || f.saveVersion <= 0 || f.sections == null || f.sections.Count == 0 || f.runSeed == 0) return null;
            if (f.saveVersion > Version) return null; // written by a newer game
            while (f.saveVersion < Version)
            {
                if (!Migrations.TryGetValue(f.saveVersion, out var migrate)) return null;
                migrate(f);
                f.saveVersion++;
            }
            return f;
        }

        /// <summary>Puts the saved run back: run seed, then every section in order. Throws if a section can't load.</summary>
        public static void Apply(SaveFile f)
        {
            RunRandom.RunSeed = f.runSeed;
            foreach (var s in sections.OrderBy(s => s.Order))
            {
                string json = f.Get(s.Key);
                if (json != null) s.Load(json);
            }
        }

        /// <summary>Loads the save, or its backup if the save is broken. False (with LastError) if neither works.</summary>
        public static bool LoadRun()
        {
            foreach (var path in new[] { FilePath, BackupPath })
            {
                var f = Read(path);
                if (f == null) continue;
                try
                {
                    Apply(f);
                    LastError = path == BackupPath ? "Your last save was damaged; continued from the one before it." : null;
                    Debug.Log("[Spellcaster] run loaded from " + path);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Spellcaster] couldn't apply save {path}: {e}");
                }
            }
            broken = true;
            LastError = "Your saved run couldn't be loaded (the file is damaged or from another version). Start a new run.";
            return false;
        }

        /// <summary>Removes the save and its backup (death, victory, a new run).</summary>
        public static void Delete()
        {
            foreach (var p in new[] { FilePath, BackupPath, TempPath })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { Debug.LogWarning($"[Spellcaster] couldn't delete {p}: {e.Message}"); }
            }
            validCache = -1;
            broken = false;
        }

        /// <summary>Forgets a load error (after the menu has shown it) and re-checks the files.</summary>
        public static void ClearError()
        {
            LastError = null;
            validCache = -1;
        }

        /// <summary>A one-line summary for the menu ("Floor 2 - 34 gold - 3 items"), or null.</summary>
        public static string Describe()
        {
            if (!HasValidSave) return null;
            if (describeCache != null) return describeCache;
            var f = Read(FilePath) ?? Read(BackupPath);
            if (f == null) return null;
            try
            {
                var d = JsonUtility.FromJson<DungeonSave>(f.Get("dungeon") ?? "{}");
                var r = JsonUtility.FromJson<RunSave>(f.Get("run") ?? "{}");
                return describeCache = $"Floor {d.floor} - {r.gold} gold - {r.items?.Count ?? 0} items";
            }
            catch (Exception) { return null; }
        }
    }
}
