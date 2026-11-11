using UnityEditor;

namespace Spellcaster.EditorTools
{
    /// <summary>Launch helper: Unity.exe -projectPath . -executeMethod Spellcaster.EditorTools.OpenPackageManager.Open</summary>
    public static class OpenPackageManager
    {
        public static void Open() => EditorApplication.delayCall += () => UnityEditor.PackageManager.UI.Window.Open("");
    }
}
