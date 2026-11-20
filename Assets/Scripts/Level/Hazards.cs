using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>Shared bits for floor hazards: the room they belong to (they stop once its fight is over).</summary>
    public abstract class Hazard : MonoBehaviour
    {
        Room room;
        bool roomLooked;

        /// <summary>The fight in this hazard's room is over (spikes, jets and pods stand down).</summary>
        public bool Disarmed
        {
            get
            {
                if (!roomLooked && Dungeon.Instance) { roomLooked = true; room = Dungeon.Instance.RoomAt(transform.position); }
                return room != null && room.HasEnemies && room.Cleared;
            }
        }

        protected static bool PlayerOnFloor(Player p) => p && !p.IsDead && p.transform.position.y < 0.6f;

        protected static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        protected static Material Mat(string key, Texture2D tex, float tiling) => Materials.World(key, tex, Color.white, tiling);

        protected static GameObject Slab(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }

    /// <summary>
    /// Lava (floor 3): burns you and walking enemies standing in it. Permanent pools are part of a room;
    /// temporary ones are the burning ground the Behemoth leaves when it rolls.
    /// </summary>
    public class LavaPool : Hazard
    {
        public static readonly List<LavaPool> All = new List<LavaPool>();
        public const float PlayerDps = 16f, EnemyDps = 20f, Tick = 0.5f;
        /// <summary>Burns enemies only (a Blink Dash + Burning fire trail).</summary>
        public bool SparePlayers;
        public float Radius { get; private set; }
        public bool Temporary => life > 0f;
        float life, age, tick;
        Renderer glow, disc;

        public static LavaPool Create(Transform parent, Vector3 pos, float radius)
        {
            var go = new GameObject("Lava");
            go.transform.SetParent(parent, false);
            go.transform.position = Fx.Ground(pos);
            var lp = go.AddComponent<LavaPool>();
            lp.Radius = radius;
            lp.disc = Slab(go.transform, PrimitiveType.Cylinder, Fx.Ground(pos) + Vector3.down * 0.03f, new Vector3(radius * 2f, 0.02f, radius * 2f),
                Mat("lava", ThemeTextures.Lava("tex_lava", 5), 1.2f), "Molten").GetComponent<Renderer>();
            // A dark rim makes the edge readable.
            Slab(go.transform, PrimitiveType.Cylinder, Fx.Ground(pos) + Vector3.down * 0.045f, new Vector3(radius * 2f + 0.5f, 0.02f, radius * 2f + 0.5f),
                Mat("lavarim", ThemeTextures.Basalt("tex_lavarim", new Color32(40, 26, 24, 255), new Color32(200, 70, 20, 255), 3), 1f), "Rim");
            lp.glow = Fx.CreateQuad(go.transform, new Color(1f, 0.45f, 0.1f, 0.5f), radius * 2.6f, billboard: false);
            lp.glow.transform.position = Fx.Ground(pos) + Vector3.up * 0.04f;
            lp.glow.transform.rotation = Quaternion.Euler(90, 0, 0);
            return lp;
        }

        /// <summary>Burning ground that fades after a few seconds (no parent: it outlives nothing).</summary>
        public static LavaPool CreateTemporary(Vector3 pos, float radius, float seconds)
        {
            if (Net.NetCombat.HostMirroring) Net.NetCombat.Lava(pos, radius, seconds);
            var go = new GameObject("BurningGround");
            go.transform.position = Fx.Ground(pos);
            if (Dungeon.Instance && Dungeon.Instance.LevelRoot) go.transform.SetParent(Dungeon.Instance.LevelRoot, true);
            var lp = go.AddComponent<LavaPool>();
            lp.Radius = radius;
            lp.life = seconds;
            lp.glow = Fx.CreateQuad(go.transform, new Color(1f, 0.4f, 0.08f, 0.8f), radius * 2.4f, billboard: false);
            lp.glow.transform.position = Fx.Ground(pos) + Vector3.up * 0.05f;
            lp.glow.transform.rotation = Quaternion.Euler(90, 0, 0);
            return lp;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Is this point inside any lava (tests, AI)?</summary>
        public static bool At(Vector3 p)
        {
            foreach (var l in All) if (l && FlatDistance(p, l.transform.position) < l.Radius) return true;
            return false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            float fade = 1f;
            if (life > 0f)
            {
                fade = Mathf.Clamp01((life - age) / 0.8f);
                if (age >= life) { Destroy(gameObject); return; }
            }
            if (glow) Materials.SetColor(glow, new Color(1f, 0.42f + 0.08f * Mathf.Sin(age * 3f), 0.1f, (life > 0f ? 0.8f : 0.45f) * fade));

            tick -= dt;
            if (tick > 0f) return;
            tick = Tick;
            foreach (var p in Players.All.ToArray())
            {
                if (SparePlayers) break;
                if (!PlayerOnFloor(p) || FlatDistance(p.transform.position, transform.position) >= Radius + 0.15f) continue;
                p.TakeDamage(PlayerDps * Tick);
                Fx.Flash(p.transform.position + Vector3.up * 0.3f, new Color(1f, 0.5f, 0.1f), 1.2f, 0.25f, 1.2f);
                Sfx.Play("burn", 0.4f, Random.Range(0.9f, 1.1f));
            }
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.IsAlive || e.Type.Flying || e.Type.IsBoss) continue;
                if (FlatDistance(e.transform.position, transform.position) > Radius + e.Type.Radius * 0.5f) continue;
                e.TakeDamage(EnemyDps * Tick, Element.Fire, silent: true);
                e.Ignite(6f, 2f);
            }
        }
    }

    /// <summary>Fire jet (floor 3): a vent that glows as a warning, then shoots a column of flame on a cycle.</summary>
    public class FireJet : Hazard
    {
        public const float Cycle = 3.4f, UpTime = 0.8f, WarnTime = 0.7f, Reach = 1.1f;
        public const float PlayerDamage = 14f, EnemyDamage = 22f;
        public bool IsUp { get; private set; }
        float t;
        bool wasUp;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();
        readonly HashSet<Player> hitPlayers = new HashSet<Player>();
        Renderer[] flames;
        Renderer warnGlow;

        public static FireJet Create(Transform parent, Vector3 pos, float phase)
        {
            var go = new GameObject("FireJet");
            go.transform.SetParent(parent, false);
            go.transform.position = Fx.Ground(pos);
            Slab(go.transform, PrimitiveType.Cylinder, Fx.Ground(pos), new Vector3(1.6f, 0.04f, 1.6f),
                Mat("vent", PixelArt.Bars("tex_vent", new Color32(80, 70, 66, 255)), 0.6f), "Vent");
            var j = go.AddComponent<FireJet>();
            j.t = phase * Cycle;
            j.warnGlow = Fx.CreateQuad(go.transform, new Color(1f, 0.4f, 0.1f, 0f), 2.2f, billboard: false);
            j.warnGlow.transform.position = Fx.Ground(pos) + Vector3.up * 0.06f;
            j.warnGlow.transform.rotation = Quaternion.Euler(90, 0, 0);
            j.flames = new Renderer[5];
            for (int i = 0; i < j.flames.Length; i++)
            {
                j.flames[i] = Fx.CreateQuad(go.transform, new Color(1f, 0.55f, 0.15f), 1.3f - i * 0.12f);
                j.flames[i].transform.position = Fx.Ground(pos) + Vector3.up * (0.4f + i * 0.55f);
                j.flames[i].gameObject.SetActive(false);
            }
            return j;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Disarmed)
            {
                IsUp = false;
                foreach (var f in flames) f.gameObject.SetActive(false);
                Materials.SetColor(warnGlow, new Color(0, 0, 0, 0));
                return;
            }
            t = Net.NetClock.Cycle(transform.position, Cycle, t, dt);
            IsUp = t > Cycle - UpTime;
            bool warn = !IsUp && t > Cycle - UpTime - WarnTime;
            float w = warn ? (t - (Cycle - UpTime - WarnTime)) / WarnTime : (IsUp ? 1f : 0f);
            Materials.SetColor(warnGlow, new Color(1f, 0.4f, 0.1f, 0.7f * w * (0.7f + 0.3f * Mathf.Sin(t * 30f))));
            for (int i = 0; i < flames.Length; i++)
            {
                flames[i].gameObject.SetActive(IsUp || (warn && i == 0));
                if (IsUp) flames[i].transform.localScale = Vector3.one * (1.3f - i * 0.12f) * (0.85f + 0.3f * Mathf.Sin(t * 25f + i));
            }
            var p = Player.Instance;
            if (IsUp && !wasUp)
            {
                hitPlayers.Clear();
                hit.Clear();
                if (p && FlatDistance(p.transform.position, transform.position) < 15f) Sfx.Play("burn", 0.6f, 0.7f);
            }
            wasUp = IsUp;
            if (!IsUp) return;
            foreach (var q in Players.All.ToArray())
            {
                if (hitPlayers.Contains(q) || !PlayerOnFloor(q) || FlatDistance(q.transform.position, transform.position) >= Reach) continue;
                hitPlayers.Add(q);
                q.TakeDamage(PlayerDamage);
            }
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e.IsAlive || e.Type.Flying || hit.Contains(e) || FlatDistance(e.transform.position, transform.position) > Reach + e.Type.Radius * 0.5f) continue;
                hit.Add(e);
                e.TakeDamage(EnemyDamage, Element.Fire);
                e.Ignite(6f, 3f);
            }
        }
    }

    /// <summary>Ice patch (floor 2): you slide on it (you keep your speed and turn slowly). Enemies grip fine.</summary>
    public class IcePatch : Hazard
    {
        public static readonly List<IcePatch> All = new List<IcePatch>();
        /// <summary>How quickly you can change speed on ice, compared to normal ground.</summary>
        public const float Grip = 0.12f;
        Vector2 half;

        public static IcePatch Create(Transform parent, Vector3 pos, Vector2 size, float yaw)
        {
            var go = Slab(parent, PrimitiveType.Cube, Fx.Ground(pos) + Vector3.down * 0.035f, new Vector3(size.x, 0.02f, size.y),
                Mat("icepatch", ThemeTextures.Ice("tex_icepatch", new Color32(170, 215, 240, 255), 4), 1f), "Ice");
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var ip = go.AddComponent<IcePatch>();
            ip.half = size * 0.5f;
            return ip;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 p)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            // InverseTransformPoint divides by scale: local units are fractions of the slab.
            return Mathf.Abs(l.x) < 0.5f && Mathf.Abs(l.z) < 0.5f;
        }

        /// <summary>Grip at a point: Grip on ice, 1 elsewhere (the player's acceleration is multiplied by it).</summary>
        public static float GripAt(Vector3 p)
        {
            if (p.y > 0.6f) return 1f;
            foreach (var i in All) if (i && i.Contains(p)) return Grip;
            return 1f;
        }
    }

    /// <summary>
    /// Ceiling pod (floor 2): a plant hanging from the ceiling. Walk under it and its tentacle drops on you
    /// (a short warning: the pod shakes and glows first).
    /// </summary>
    public class CeilingPod : Hazard
    {
        public const float TriggerRange = 1.6f, HitRange = 1.9f, Damage = 12f, Cooldown = 2.6f, Warn = 0.4f, Drop = 0.35f, Hold = 0.4f;
        public int Strikes { get; private set; }
        public bool Striking => phase > 0;
        int phase; // 0 idle, 1 warn, 2 drop, 3 hold, 4 retract
        float t, cd;
        bool struck;
        Transform pod, tentacle;
        Renderer podR, tentR, glow;
        MaterialPropertyBlock mpb;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex"), StId = Shader.PropertyToID("_MainTex_ST");
        const float PodY = Dungeon.WallH - 0.75f, TentacleLen = 3.6f;

        public static CeilingPod Create(Transform parent, Vector3 pos)
        {
            var go = new GameObject("CeilingPod");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var cp = go.AddComponent<CeilingPod>();
            cp.mpb = new MaterialPropertyBlock();
            cp.pod = Quad(go.transform, "Pod", out cp.podR);
            cp.pod.localPosition = new Vector3(0, PodY, 0);
            cp.pod.localScale = new Vector3(1.8f, 1.8f, 1f);
            cp.tentacle = Quad(go.transform, "Tentacle", out cp.tentR);
            cp.glow = Fx.CreateQuad(go.transform, new Color(0.5f, 1f, 0.4f, 0f), 2.4f, billboard: false);
            cp.glow.transform.position = Fx.Ground(pos) + Vector3.up * 0.05f;
            cp.glow.transform.rotation = Quaternion.Euler(90, 0, 0);
            if (Sprites.PodBody == null)
            {
                // No art pack: a green glow for the pod and a green strip for the tentacle.
                cp.podR.sharedMaterial = Materials.Fx;
                cp.tentR.sharedMaterial = Materials.Fx;
            }
            cp.SetTentacle(0f);
            return cp;
        }

        static Transform Quad(Transform parent, string name, out Renderer r)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(parent, false);
            r = q.GetComponent<Renderer>();
            r.sharedMaterial = Materials.Sprite;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.AddComponent<Billboard>().Cylindrical = true;
            return q.transform;
        }

        /// <summary>0 = tentacle up in the pod, 1 = fully down to the floor.</summary>
        void SetTentacle(float k)
        {
            float len = Mathf.Lerp(0.4f, TentacleLen, k);
            tentacle.localScale = new Vector3(len * 93f / 153f * 0.6f + 0.3f, len, 1f);
            tentacle.localPosition = new Vector3(0, PodY - 0.3f - len * 0.5f, 0);
            var anim = Sprites.PodTentacle;
            if (anim != null)
            {
                mpb.SetTexture(MainTexId, anim.Texture);
                mpb.SetVector(StId, anim.Frames[Mathf.Clamp(Mathf.RoundToInt(k * (anim.Count - 1)), 0, anim.Count - 1)]);
                mpb.SetColor("_Color", Color.white);
                tentR.SetPropertyBlock(mpb);
            }
            else Materials.SetColor(tentR, new Color(0.4f, 1f, 0.4f, k > 0.05f ? 1f : 0f));
        }

        void SetPod(float shake)
        {
            var anim = Sprites.PodBody;
            pod.localPosition = new Vector3(Mathf.Sin(Time.time * 60f) * 0.08f * shake, PodY, 0);
            if (anim != null)
            {
                mpb.SetTexture(MainTexId, anim.Texture);
                mpb.SetVector(StId, anim.Frames[anim.FrameAt(Time.time)]);
                mpb.SetColor("_Color", Color.Lerp(Color.white, new Color(1.4f, 1.6f, 1.2f), shake));
                podR.SetPropertyBlock(mpb);
            }
            else Materials.SetColor(podR, new Color(0.4f, 0.9f + 0.1f * shake, 0.3f, 1f));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            cd -= dt;
            // The nearest player on the floor triggers it; the tentacle hits everyone under it.
            Player p = null;
            float dist = 99f;
            foreach (var q in Players.All)
            {
                if (!PlayerOnFloor(q)) continue;
                float dq = FlatDistance(q.transform.position, transform.position);
                if (dq < dist) { dist = dq; p = q; }
            }
            if (phase == 0)
            {
                SetPod(0f);
                Materials.SetColor(glow, new Color(0, 0, 0, 0));
                if (!Disarmed && cd <= 0f && PlayerOnFloor(p) && dist < TriggerRange) { phase = 1; t = 0f; struck = false; Sfx.Play("crate", 0.4f, 1.6f); }
                return;
            }
            t += dt;
            SetPod(phase == 1 ? Mathf.Clamp01(t / Warn) : 0.3f);
            Materials.SetColor(glow, new Color(0.5f, 1f, 0.4f, phase <= 2 ? 0.5f : 0.2f));
            switch (phase)
            {
                case 1: if (t >= Warn) { phase = 2; t = 0f; } break;
                case 2:
                    SetTentacle(t / Drop);
                    if (t >= Drop)
                    {
                        phase = 3; t = 0f;
                        if (!struck)
                            foreach (var q in Players.All.ToArray())
                                if (PlayerOnFloor(q) && FlatDistance(q.transform.position, transform.position) < HitRange)
                                {
                                    if (!struck) { Strikes++; Sfx.Play("claw", 0.6f, 1.2f); }
                                    struck = true;
                                    q.TakeDamage(Damage);
                                }
                        foreach (var e in Enemy.All.ToArray())
                            if (e.IsAlive && !e.Type.Flying && FlatDistance(e.transform.position, transform.position) < HitRange) e.TakeDamage(Damage * 1.5f);
                    }
                    break;
                case 3: if (t >= Hold) { phase = 4; t = 0f; } break;
                case 4:
                    SetTentacle(1f - t / Drop);
                    if (t >= Drop) { phase = 0; cd = Cooldown; SetTentacle(0f); }
                    break;
            }
        }
    }
}
