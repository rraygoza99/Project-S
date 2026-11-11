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

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            Ensure();
        }

        void Update() => Ensure();

        void Ensure()
        {
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
