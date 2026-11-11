using UnityEngine;

namespace Spellcaster
{
    /// <summary>Fallback lightning: re-randomised midpoint displacement every few frames.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class SimpleBolt : MonoBehaviour
    {
        Vector3 a, b;
        LineRenderer lr;
        float timer;
        const int Segments = 12;

        public void Init(Vector3 start, Vector3 end)
        {
            a = start;
            b = end;
            lr = GetComponent<LineRenderer>();
            lr.positionCount = Segments + 1;
            Regenerate();
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f) Regenerate();
        }

        void Regenerate()
        {
            timer = 0.04f;
            float jitter = Vector3.Distance(a, b) * 0.06f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < Segments) p += Random.insideUnitSphere * jitter;
                lr.SetPosition(i, p);
            }
        }
    }
}
