using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    public enum PropKind { Barrel, Crate }

    /// <summary>
    /// Breakable obstacle. Explosive barrels blow up (hurting enemies, you and other barrels);
    /// crates may drop loot. Both block movement and shots until broken.
    /// </summary>
    public class Prop : MonoBehaviour
    {
        public static readonly List<Prop> All = new List<Prop>();
        public PropKind Kind { get; private set; }
        public float HP { get; private set; }
        public Vector3 Center => transform.position + Vector3.up * 0.6f;
        bool broken;
        Renderer rend;
        float flash;

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        public static Prop Create(PropKind kind, Transform parent, Vector3 worldPos)
        {
            GameObject go;
            if (kind == PropKind.Barrel)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.localScale = new Vector3(1f, 0.65f, 1f);
                go.GetComponent<Renderer>().sharedMaterial = Materials.World("barrel",
                    PixelArt.Planks("tex_barrel", C(170, 40, 30), C(70, 60, 60), 3), Color.white, 1.3f);
                worldPos.y = 0.65f;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = Vector3.one * 1.2f;
                go.GetComponent<Renderer>().sharedMaterial = Materials.World("crate",
                    PixelArt.Planks("tex_crate", C(150, 105, 60), C(90, 80, 70), 9), Color.white, 1.2f);
                worldPos.y = 0.6f;
                go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 4) * 90f + Random.Range(-12f, 12f), 0);
            }
            go.name = kind.ToString();
            go.layer = Layers.Prop;
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var p = go.AddComponent<Prop>();
            p.Kind = kind;
            p.HP = kind == PropKind.Barrel ? 10f : 18f;
            p.rend = r;
            All.Add(p);
            return p;
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            if (flash <= 0f) return;
            flash -= Time.deltaTime;
            Materials.SetColor(rend, flash > 0f ? new Color(2f, 1.6f, 1.4f) : Color.white);
        }

        public void TakeDamage(float amount)
        {
            if (broken || amount <= 0f) return;
            HP -= amount;
            flash = 0.08f;
            Materials.SetColor(rend, new Color(2f, 1.6f, 1.4f));
            if (HP <= 0f) Break();
            else Sfx.Play("crate", 0.4f, Random.Range(1.1f, 1.3f));
        }

        void Break()
        {
            broken = true;
            All.Remove(this);
            var pos = transform.position;
            GetComponent<Collider>().enabled = false;
            if (Kind == PropKind.Barrel)
            {
                Combat.Explode(Center, 4.2f, 50f, 20f, Combat.BombColor, this);
            }
            else
            {
                Sfx.Play("crate", 0.7f, 0.8f);
                Fx.Flash(Center, new Color(0.8f, 0.6f, 0.35f), 1.6f, 0.3f, 1.2f);
                Loot.CrateDrop(pos);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Floor spikes that shoot up on a cycle, with a short warning. Hurts you and walking enemies.</summary>
    public class SpikeTrap : MonoBehaviour
    {
        public const float Size = 2.4f;
        public const float Cycle = 2.8f, UpTime = 0.7f, WarnTime = 0.5f;
        public const float PlayerDamage = 12f, EnemyDamage = 20f;

        Transform spikes;
        float t;
        bool wasUp, hitPlayer;
        readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
        public bool IsUp { get; private set; }

        public static SpikeTrap Create(Transform parent, Vector3 worldPos, float phase)
        {
            var go = new GameObject("Spikes");
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(worldPos.x, 0f, worldPos.z);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(go.transform, false);
            plate.transform.localPosition = new Vector3(0, 0.01f, 0);
            plate.transform.localScale = new Vector3(Size, 0.04f, Size);
            plate.GetComponent<Renderer>().sharedMaterial = Materials.World("spikeplate",
                PixelArt.Bars("tex_spikeplate", new Color32(70, 66, 74, 255)), Color.white, 0.6f);

            var spikes = new GameObject("Points").transform;
            spikes.SetParent(go.transform, false);
            var steel = Materials.World("steel", PixelArt.Tiles("tex_steel", new Color32(170, 170, 180, 255), new Color32(90, 90, 100, 255), 4), Color.white, 1f);
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.transform.SetParent(spikes, false);
                s.transform.localPosition = new Vector3((i - 1.5f) * 0.55f, 0.35f, (j - 1.5f) * 0.55f);
                s.transform.localScale = new Vector3(0.1f, 0.7f, 0.1f);
                s.transform.localRotation = Quaternion.Euler(0, 45f, 0);
                var r = s.GetComponent<Renderer>();
                r.sharedMaterial = steel;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var trap = go.AddComponent<SpikeTrap>();
            trap.spikes = spikes;
            trap.t = phase * Cycle;
            spikes.localPosition = new Vector3(0, -0.75f, 0);
            return trap;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t = (t + dt) % Cycle;
            IsUp = t > Cycle - UpTime;
            bool warn = !IsUp && t > Cycle - UpTime - WarnTime;
            float targetY = IsUp ? 0f : (warn ? -0.55f : -0.75f);
            var lp = spikes.localPosition;
            lp.y = Mathf.MoveTowards(lp.y, targetY, (IsUp ? 12f : 3f) * dt);
            spikes.localPosition = lp;

            var player = Player.Instance;
            if (IsUp && !wasUp)
            {
                hitPlayer = false;
                hitEnemies.Clear();
                if (player && (player.transform.position - transform.position).sqrMagnitude < 15f * 15f)
                    Sfx.Play("spikes", 0.45f, Random.Range(0.9f, 1.1f));
            }
            wasUp = IsUp;
            if (!IsUp) return;

            if (!hitPlayer && player && !player.IsDead && Inside(player.transform.position, 0.3f) && player.transform.position.y < 0.6f)
            {
                hitPlayer = true;
                player.TakeDamage(PlayerDamage);
            }
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.IsAlive || e.Type.Hover > 0f || hitEnemies.Contains(e) || !Inside(e.transform.position, e.Type.Radius)) continue;
                hitEnemies.Add(e);
                e.TakeDamage(EnemyDamage);
            }
        }

        bool Inside(Vector3 p, float pad)
        {
            float h = Size * 0.5f + pad;
            return Mathf.Abs(p.x - transform.position.x) < h && Mathf.Abs(p.z - transform.position.z) < h;
        }
    }
}
