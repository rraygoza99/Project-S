using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Autosaves when a fight room is cleared (once any reward panel is closed, so the reward taken is
    /// in the save), shows the "Saving..." icon, and gives dev mode its save keys:
    /// F5 save now, F9 load (continue the save), F8 delete the save, F7 print it and open its folder.
    /// </summary>
    public class SaveRunner : MonoBehaviour
    {
        public static SaveRunner Instance { get; private set; }
        string pendingRoom;
        /// <summary>A checkpoint is waiting for a reward panel to close.</summary>
        public bool Pending => pendingRoom != null;
        public int Autosaves { get; private set; }

        void Awake() => Instance = this;

        void OnEnable()
        {
            Dungeon.RoomCleared += OnRoomCleared;
            HUD.DrawOverlay += DrawIcon;
        }

        void OnDisable()
        {
            Dungeon.RoomCleared -= OnRoomCleared;
            HUD.DrawOverlay -= DrawIcon;
        }

        void OnRoomCleared(Room room)
        {
            if (SaveSystem.CanSave) pendingRoom = room.Id;
        }

        void Update()
        {
            var gs = GameState.Instance;
            if (pendingRoom != null)
            {
                if (!SaveSystem.CanSave) pendingRoom = null;
                else if (!gs.ForgeOpen) // the reward panel (and the Forge) are closed: the reward is in
                {
                    Dungeon.Instance.CheckpointRoomId = pendingRoom;
                    pendingRoom = null;
                    if (SaveSystem.Save()) Autosaves++;
                }
            }

            if (!Settings.DevMode || AutoTest.Active || !gs || !gs.InRun) return;
            if (Input.GetKeyDown(KeyCode.F5)) Flash(ForceSave() ? "Saved" : "Save failed");
            if (Input.GetKeyDown(KeyCode.F9)) ForceLoad();
            if (Input.GetKeyDown(KeyCode.F8)) { SaveSystem.Delete(); Flash("Save deleted"); }
            if (Input.GetKeyDown(KeyCode.F7)) PrintSave();
        }

        static void Flash(string s) { if (SpellSystem.Instance) SpellSystem.Instance.Flash(s); }

        /// <summary>Saves now (debug). The checkpoint becomes the current room if it is safe (no fight on).</summary>
        public static bool ForceSave()
        {
            var d = Dungeon.Instance;
            if (d && d.Current != null && !d.InCombat) d.CheckpointRoomId = d.Current.Id;
            return SaveSystem.Save();
        }

        /// <summary>Reloads the world and continues the save (debug: the same path as Continue).</summary>
        public static void ForceLoad()
        {
            if (!SaveSystem.HasValidSave) { Flash("No save to load"); return; }
            GameState.PendingContinue = true;
            GameState.Instance.ToMenu();
        }

        public static void PrintSave()
        {
            string path = SaveSystem.FilePath;
            string text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "(no save)";
            Debug.Log($"[Spellcaster] save file {path}:\n{text}");
            Flash("Save printed to the log");
            Application.OpenURL("file:///" + SaveSystem.Folder.Replace('\\', '/'));
        }

        /// <summary>A small "Saving..." with a spinning rune, bottom right, for a moment after each save.</summary>
        static void DrawIcon(float W, float H, float u)
        {
            float age = Time.unscaledTime - SaveSystem.LastSaveTime;
            if (age < 0f || age > 1.6f) return;
            float a = Mathf.Clamp01((1.6f - age) * 3f);
            var box = new Rect(W - 12f * u, H - 6.2f * u, 2.2f * u, 2.2f * u);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(age * 360f, box.center);
            HUD.DrawTinted(box, PixelArt.RingTex, new Color(0.8f, 0.7f, 1f, a));
            GUI.matrix = m;
            var st = new GUIStyle(HUD.Small);
            st.normal.textColor = new Color(0.9f, 0.85f, 1f, a);
            HUD.Shadowed(new Rect(box.xMax + 0.5f * u, box.y + 0.2f * u, 10f * u, 2f * u), "Saving...", st);
        }
    }
}
