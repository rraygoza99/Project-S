using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellcaster
{
    /// <summary>
    /// Builds the entire game from code whenever a scene loads, so the project needs no
    /// hand-authored scenes or prefabs. Restarting = reloading the (empty) scene.
    /// </summary>
    public static class Bootstrap
    {
        public static readonly Color FogColor = new Color(0.06f, 0.035f, 0.08f); // floor 1; each floor sets its own

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded += (_, __) => Build();
            Build();
        }

        static void Build()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            Materials.SetFog(FogColor, 5f, 30f);

            var root = new GameObject("Game");
            root.AddComponent<GameState>();
            root.AddComponent<Run>();
            var dungeon = root.AddComponent<Dungeon>();

            // The world camera renders into a low-res texture; this camera just clears the backbuffer,
            // and the HUD blits the low-res image with nearest-neighbour filtering.
            var present = new GameObject("PresentCamera").AddComponent<Camera>();
            present.cullingMask = 0;
            present.clearFlags = CameraClearFlags.SolidColor;
            present.backgroundColor = Color.black;
            present.depth = -100;

            var player = Player.Create(new Vector3(0f, 0.1f, -3f));
            Sfx.Init(player.Cam.gameObject);

            root.AddComponent<SpellSystem>();
            root.AddComponent<EnemySpawner>();
            root.AddComponent<HUD>();
            root.AddComponent<SpellForgeUI>();
            dungeon.BuildFloor(1);
            AutoTest.TryStart(root);
        }
    }
}
