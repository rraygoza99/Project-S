using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Jagged lightning between two points. Uses Digital Ruby's LightningBoltScript (Lightning Bolt
    /// Effect, imported into Assets/ThirdParty) when present, through reflection so the game still
    /// compiles without it; otherwise draws its own flickering midpoint-displacement bolt.
    /// </summary>
    public static class LightningFx
    {
        static bool searched;
        static Type boltType;
        static Material material;

        static Type BoltType
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    boltType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("DigitalRuby.LightningBolt.LightningBoltScript", false))
                        .FirstOrDefault(t => t != null);
                }
                return boltType;
            }
        }

        static Material Material
        {
            get
            {
                if (material) return material;
                material = new Material(Materials.Fx) { name = "LightningFx" };
                var tex = Resources.Load<Texture2D>("Lightning/bolt");
                material.mainTexture = tex ? tex : PixelArt.White;
                return material;
            }
        }

        public static void Bolt(Vector3 start, Vector3 end, Color color, float duration)
        {
            var go = new GameObject("Lightning");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = Material;
            lr.widthMultiplier = 0.35f;
            lr.textureMode = LineTextureMode.Stretch;
            lr.startColor = Color.Lerp(color, Color.white, 0.4f);
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var type = BoltType;
            if (type != null)
            {
                lr.positionCount = 0;
                var c = go.AddComponent(type);
                Set(c, "StartPosition", start);
                Set(c, "EndPosition", end);
                Set(c, "Generations", 5);
                Set(c, "Duration", 0.04f);
                Set(c, "ChaosFactor", 0.2f);
            }
            else
            {
                go.AddComponent<SimpleBolt>().Init(start, end);
            }
            UnityEngine.Object.Destroy(go, duration);
        }

        static void Set(Component c, string field, object value)
        {
            var f = c.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(c, value);
        }
    }
}
