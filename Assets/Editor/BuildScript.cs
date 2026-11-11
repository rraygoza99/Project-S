using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spellcaster.EditorTools
{
    /// <summary>
    /// Batch-mode build entry point used by play.ps1. The scene is empty on purpose:
    /// Bootstrap builds the whole game from code at runtime.
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputPath = "Build/Spellcaster.exe";

        [MenuItem("Spellcaster/Build Windows Player")]
        public static void BuildWindows()
        {
            EnsureScene();

            PlayerSettings.productName = "Spellcaster";
            PlayerSettings.companyName = "SpellcasterMVP";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("SPELLCASTER BUILD FAILED: " + report.summary.result);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            Debug.Log("SPELLCASTER BUILD OK: " + Path.GetFullPath(OutputPath));
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
