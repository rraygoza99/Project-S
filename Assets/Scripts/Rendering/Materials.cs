using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Shared materials built from the shaders in Resources/Shaders.</summary>
    public static class Materials
    {
        static readonly Dictionary<string, Material> worldCache = new Dictionary<string, Material>();
        static Material sprite, fx;

        public static Material World(string key, Texture tex, Color color, float tiling)
        {
            if (worldCache.TryGetValue(key, out var m) && m) return m;
            m = new Material(Load("World")) { name = key, mainTexture = tex, color = color };
            m.SetFloat("_Tiling", tiling);
            worldCache[key] = m;
            return m;
        }

        public static Material Sprite => sprite ? sprite : (sprite = new Material(Load("Sprite")) { name = "Sprite" });

        public static Material Fx => fx ? fx : (fx = new Material(Load("FX")) { name = "FX", mainTexture = PixelArt.Glow });

        public static void SetFog(Color color, float start, float end)
        {
            Shader.SetGlobalColor("_SC_FogColor", color);
            Shader.SetGlobalVector("_SC_FogParams", new Vector4(start, end, 0, 0));
        }

        static Shader Load(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (!s) Debug.LogError("Missing shader Resources/Shaders/" + name);
            return s;
        }

        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}
