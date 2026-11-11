using UnityEngine;

namespace Spellcaster
{
    public class FxFade : MonoBehaviour
    {
        Renderer rend;
        Color color;
        float duration, grow, t;
        Vector3 baseScale;

        public void Init(Renderer r, Color c, float dur, float growBy)
        {
            rend = r;
            color = c;
            duration = Mathf.Max(0.01f, dur);
            grow = growBy;
            baseScale = r.transform.localScale;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = t / duration;
            if (k >= 1f || !rend)
            {
                Destroy(gameObject);
                return;
            }
            rend.transform.localScale = baseScale * (1f + grow * k);
            var c = color;
            c.a = color.a * (1f - k);
            Materials.SetColor(rend, c);
        }
    }
}
