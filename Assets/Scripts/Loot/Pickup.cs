using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// An item lying in the world. Coins and hearts are collected by walking over them (coins fly to
    /// you); everything else is taken or bought with E. Shop items carry a price.
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();
        public const float InteractRange = 2.4f;

        public ItemDef Item { get; private set; }
        public int Price { get; private set; }
        /// <summary>Free coins and hearts are collected by walking over them; shop items always need E.</summary>
        public bool IsAuto => Price == 0 && (Item.Kind == ItemKind.Coin || Item.Kind == ItemKind.Heart);
        public Vector3 LabelPosition => visual.position + Vector3.up * 0.7f;

        Transform visual;
        Renderer rend;
        MaterialPropertyBlock mpb;
        Vector3 velocity;
        float baseY, bobPhase, age;
        bool grounded;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        /// <summary>Spawns a pickup. With pop, it bounces out in a random direction (enemy drops).</summary>
        public static Pickup Spawn(ItemDef item, Vector3 position, int price = 0, bool pop = false, float height = 0.55f)
        {
            var go = new GameObject("Pickup " + item.Name);
            if (Dungeon.Instance && Dungeon.Instance.LevelRoot) go.transform.SetParent(Dungeon.Instance.LevelRoot, true);
            go.transform.position = new Vector3(position.x, height, position.z);

            float size = item.Kind == ItemKind.Coin ? 0.45f : (item.Kind == ItemKind.Heart ? 0.6f : 0.8f);
            var glow = Fx.CreateQuad(go.transform, new Color(item.Color.r, item.Color.g, item.Color.b, 0.5f), size * 1.8f);
            glow.transform.localPosition = Vector3.zero;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Icon";
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = Vector3.one * size;
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = Materials.Sprite;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad.AddComponent<Billboard>().Cylindrical = true;

            var p = go.AddComponent<Pickup>();
            p.Item = item;
            p.Price = price;
            p.visual = quad.transform;
            p.rend = r;
            p.mpb = new MaterialPropertyBlock();
            p.baseY = height;
            p.bobPhase = Random.value * 6f;
            p.grounded = !pop;
            if (pop)
            {
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f);
                p.velocity = new Vector3(d.x, Random.Range(4f, 6f), d.y);
            }
            p.ApplyIcon();
            All.Add(p);
            return p;
        }

        void OnDestroy() => All.Remove(this);

        void ApplyIcon()
        {
            mpb.SetTexture(MainTexId, Item.Icon);
            mpb.SetVector(MainTexStId, new Vector4(1, 1, 0, 0));
            mpb.SetColor(ColorId, Color.white);
            mpb.SetFloat(FlashId, 0f);
            rend.SetPropertyBlock(mpb);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (!grounded)
            {
                velocity.y -= 18f * dt;
                Vector3 step = velocity * dt;
                Vector3 pos = transform.position;
                if (step.sqrMagnitude > 1e-6f && Physics.Raycast(pos, step.normalized, out var wall, step.magnitude + 0.2f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(wall.normal.y) < 0.5f)
                {
                    velocity = Vector3.Reflect(velocity, wall.normal) * 0.4f;
                    step = Vector3.zero;
                }
                pos += step;
                if (pos.y <= baseY && velocity.y < 0f)
                {
                    pos.y = baseY;
                    grounded = true;
                }
                transform.position = pos;
            }
            else
            {
                var p = transform.position;
                p.y = baseY + 0.12f * Mathf.Sin(Time.time * 3f + bobPhase);
                transform.position = p;
            }

            var player = Player.Instance;
            if (!player || player.IsDead || !IsAuto || age < 0.4f) return;
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (Item.Kind == ItemKind.Coin && dist < 3.5f)
            {
                // coins fly to you
                transform.position += to.normalized * Mathf.Min(dist, (10f - dist * 2f) * dt);
                grounded = true;
            }
            if (dist < 1.1f && Run.Instance && Run.Instance.CanAcquire(Item, out _)) TryCollect(Run.Instance, quiet: true);
        }

        public string Prompt => Price > 0 ? $"E: buy {Item.Name} - {Price} gold" : $"E: take {Item.Name}";

        /// <summary>Takes (or buys) the item. Returns false and tells the player why if it can't.</summary>
        public bool TryCollect(Run run, bool quiet = false)
        {
            if (!this || !run) return false;
            if (Price > run.Gold)
            {
                Deny(run, "Not enough gold", quiet);
                return false;
            }
            if (!run.CanAcquire(Item, out var reason))
            {
                Deny(run, reason, quiet);
                return false;
            }
            if (Price > 0)
            {
                run.Gold -= Price;
                Sfx.Play("buy", 0.7f);
            }
            run.Acquire(Item);
            Fx.Flash(visual.position, Item.Color, 1.2f, 0.3f, 1.5f);
            All.Remove(this);
            Destroy(gameObject);
            return true;
        }

        static void Deny(Run run, string reason, bool quiet)
        {
            if (quiet) return;
            Sfx.Play("fizzle", 0.4f, 1.3f);
            if (SpellSystem.Instance) SpellSystem.Instance.Flash(reason);
        }

        /// <summary>The closest item you can take or buy with E, if any is in reach.</summary>
        public static Pickup NearestInteractable(Vector3 pos)
        {
            Pickup best = null;
            float bestD = InteractRange * InteractRange;
            foreach (var p in All)
            {
                if (!p || p.IsAuto) continue;
                Vector3 d = p.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = p; }
            }
            return best;
        }
    }
}
