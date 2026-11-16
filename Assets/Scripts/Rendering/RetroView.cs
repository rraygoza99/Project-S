using UnityEngine;

namespace Spellcaster
{
    /// <summary>Renders the camera into a low-resolution, point-filtered RenderTexture.</summary>
    [RequireComponent(typeof(Camera))]
    public class RetroView : MonoBehaviour
    {
        /// <summary>Internal vertical resolution. Width follows the window aspect.</summary>
        public const int Height = 200;

        public static RenderTexture RT { get; private set; }
        /// <summary>The low-res image is ready to draw (it can be briefly lost after alt-tab).</summary>
        public static bool Ready => RT != null && RT.IsCreated();

        /// <summary>Test hook: simulate the driver dropping the render texture.</summary>
        public static void DropForTest() { if (RT != null) RT.Release(); }

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            Ensure();
        }

        void Update() => Ensure();

        void Ensure()
        {
            // Minimised / mid alt-tab: the window can report 0 size. Keep the current texture.
            if (Screen.width <= 0 || Screen.height <= 0) { if (RT != null && cam.targetTexture != RT) cam.targetTexture = RT; return; }
            // The graphics driver can drop render textures on focus or device loss: rebuild it.
            if (RT != null && !RT.IsCreated())
            {
                Debug.Log("[Spellcaster] render texture was lost; recreating it");
                RT.Create();
            }
            int w = Mathf.Max(1, Mathf.RoundToInt(Height * (float)Screen.width / Mathf.Max(1, Screen.height)));
            if (RT == null || RT.width != w || RT.height != Height)
            {
                if (RT != null) RT.Release();
                RT = new RenderTexture(w, Height, 24)
                {
                    filterMode = FilterMode.Point,
                    antiAliasing = 1,
                    name = "RetroRT",
                };
                RT.Create();
            }
            if (cam.targetTexture != RT) cam.targetTexture = RT;
        }
    }
}
