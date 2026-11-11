using UnityEngine;

namespace Spellcaster
{
    /// <summary>Small helpers for glowing additive quads and one-shot flashes.</summary>
    public static class Fx
    {
        public static Renderer CreateQuad(Transform parent, Color color, float size, bool billboard = true)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "FxQuad";
            q.transform.SetParent(parent, false);
            q.transform.localScale = Vector3.one * size;
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = Materials.Fx;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Materials.SetColor(r, color);
            if (billboard) q.AddComponent<Billboard>().Cylindrical = false;
            return r;
        }

        /// <summary>A flat ring on the floor that expands to radius and fades (novas).</summary>
        public static void Ring(Vector3 center, Color color, float radius, float duration)
        {
            var go = new GameObject("Ring");
            go.transform.position = center;
            var r = CreateQuad(go.transform, color, radius * 0.3f, billboard: false);
            r.transform.localRotation = Quaternion.Euler(90, 0, 0);
            r.sharedMaterial = RingMaterial;
            go.AddComponent<FxFade>().Init(r, color, duration, (radius * 2f) / (radius * 0.3f) - 1f);
        }

        static Material ringMaterial;
        static Material RingMaterial => ringMaterial ? ringMaterial
            : (ringMaterial = new Material(Materials.Fx) { name = "FxRing", mainTexture = PixelArt.RingTex });

        /// <summary>A glow that grows and fades out, then destroys itself.</summary>
        public static void Flash(Vector3 position, Color color, float size, float duration, float grow = 1f)
        {
            var go = new GameObject("Flash");
            go.transform.position = position;
            var r = CreateQuad(go.transform, color, size);
            go.AddComponent<FxFade>().Init(r, color, duration, grow);
        }
    }
}
