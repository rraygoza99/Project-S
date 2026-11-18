using System.Collections.Generic;
using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// An item lying in the world. Coins and hearts are collected by walking over them (coins fly to
    /// you); everything else is taken or bought with E. Shop items carry a price.
    /// In multiplayer every pickup is instanced: each player takes their own copy (see Net.NetLoot).
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();
        public const float InteractRange = 2.4f;

        public ItemDef Item { get; private set; }
        public int Price { get; private set; }
        /// <summary>Free coins and hearts are collected by walking over them; shop items always need E.</summary>
        public bool IsAuto => Price == 0 && (Item.Kind == ItemKind.Coin || Item.Kind == ItemKind.Heart || Item.Kind == ItemKind.Essence);
        /// <summary>Coins and Essence drift to you and are vacuumed up when the room is cleared.</summary>
        bool Magnetic => Item.Kind == ItemKind.Coin || Item.Kind == ItemKind.Essence;
        public Vector3 LabelPosition => visual.position + Vector3.up * 0.7f;

        Transform visual, glowQuad;
        Renderer rend;
        MaterialPropertyBlock mpb;
        Vector3 velocity;
        float baseY, bobPhase, age;
        bool grounded, vacuum;
        /// <summary>Coins within this distance drift to you.</summary>
        public const float CoinMagnetRadius = 7f;
        /// <summary>Flying to you because the room was cleared (tests).</summary>
        public bool Vacuuming => vacuum;
        public float Age => age;

        // ---------------- multiplayer (instanced loot) ----------------

        /// <summary>Network id (0 offline).</summary>
        public int NetId;
        /// <summary>Only this player may take it (a forgotten rune), or -1 for everyone.</summary>
        public int OnlyFor = -1;
        /// <summary>A client's copy of a host pickup: taking it asks the host.</summary>
        public bool Proxy { get; private set; }
        readonly HashSet<int> takenBy = new HashSet<int>();

        public bool For(Player p) => p && (OnlyFor < 0 || OnlyFor == p.NetId) && !takenBy.Contains(p.NetId);
        /// <summary>This PC's player already took their copy (it stays for the others).</summary>
        public bool TakenByLocal => Player.Instance && takenBy.Contains(Player.Instance.NetId);

        public bool TakenByAll
        {
            get
            {
                foreach (var p in Players.All)
                    if (p && p.NetId >= 0 && (OnlyFor < 0 || OnlyFor == p.NetId) && !takenBy.Contains(p.NetId)) return false;
                return true;
            }
        }

        /// <summary>A player came back with a new connection id: what they took stays taken.</summary>
        public static void RemapTaken(int oldId, int newId)
        {
            if (oldId == newId) return;
            foreach (var p in All)
                if (p && p.takenBy.Remove(oldId)) p.takenBy.Add(newId);
        }

        public void MarkTaken(Player p)
        {
            takenBy.Add(p.NetId);
            if (p.IsLocal)
            {
                // Gone for you; the others still see theirs.
                Fx.Flash(visual.position, Item.Color, 1.2f, 0.3f, 1.5f);
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
        }

        public void MakeProxy(int id, Vector3 pos, Vector3 popVelocity)
        {
            NetId = id;
            Proxy = true;
            if (popVelocity.sqrMagnitude > 0f) { velocity = popVelocity; grounded = false; }
        }

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        /// <summary>Spawns a pickup. With pop, it bounces out in a random direction (enemy drops).</summary>
        public static Pickup Spawn(ItemDef item, Vector3 position, int price = 0, bool pop = false, float height = 0.55f, int onlyFor = -1)
        {
            // A multiplayer client only shows the host's pickups (its own floor stock comes from the host).
            if (Net.NetSession.IsClientOnly && Net.NetPlayers.Running && !Net.NetLoot.SpawningProxy) return null;
            var go = new GameObject("Pickup " + item.Name);
            if (Dungeon.Instance && Dungeon.Instance.LevelRoot) go.transform.SetParent(Dungeon.Instance.LevelRoot, true);
            go.transform.position = new Vector3(position.x, height, position.z);

            float size = item.Kind == ItemKind.Coin ? 0.45f : item.Kind == ItemKind.Essence ? 0.5f : (item.Kind == ItemKind.Heart ? 0.6f : 0.8f);
            bool essence = item.Kind == ItemKind.Essence;
            var glow = Fx.CreateQuad(go.transform, new Color(item.Color.r, item.Color.g, item.Color.b, essence ? 0.75f : 0.5f), size * (essence ? 2.6f : 1.8f));
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
            p.OnlyFor = onlyFor; // set before the host announces it, so other players never get a copy
            p.visual = quad.transform;
            p.glowQuad = glow.transform;
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
            Net.NetLoot.OnSpawned(p, p.velocity, pop);
            return p;
        }

        /// <summary>A pickup only one player can take (multiplayer: a forgotten rune at their feet).</summary>
        public static Pickup SpawnFor(ItemDef item, Vector3 position, int playerNetId, bool pop = false)
        {
            var p = Spawn(item, position, pop: pop, height: item.Kind == ItemKind.Essence ? Items.EssenceHeight : 0.55f, onlyFor: playerNetId);
            if (!p) return null;
            if (Player.Instance && Player.Instance.NetId != playerNetId)
                foreach (var r in p.GetComponentsInChildren<Renderer>()) r.enabled = false; // not yours: not shown here
            return p;
        }

        void OnDestroy() => All.Remove(this);

        /// <summary>Resting height above the floor (saved with the pickup).</summary>
        public float Height => baseY;

        /// <summary>Removes it at once (loading a save replaces the floor's pickups).</summary>
        public void Discard()
        {
            All.Remove(this);
            Destroy(gameObject);
        }

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
                bool essence = Item.Kind == ItemKind.Essence;
                // Essence floats higher and slower, and its glow breathes.
                p.y = baseY + (essence ? 0.18f * Mathf.Sin(Time.time * 2f + bobPhase) : 0.12f * Mathf.Sin(Time.time * 3f + bobPhase));
                transform.position = p;
                if (essence && glowQuad) glowQuad.localScale = Vector3.one * (1f + 0.18f * Mathf.Sin(Time.time * 4f + bobPhase));
            }

            if (!IsAuto || age < 0.4f) return;
            // Multiplayer host: each player gets their own copy when in reach (the object stays put).
            if (Net.NetLoot.Instanced && !Proxy) { Net.NetLoot.HostAuto(this); return; }

            // Offline, or a client's copy flying to its own player (the host decides who gets it).
            var player = Proxy ? Player.Instance : Players.Nearest(transform.position);
            if (!player || player.IsDead) return;
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (Magnetic && !vacuum)
            {
                // Once the fight in this room is over, every coin left in it comes to you.
                var dg = Dungeon.Instance;
                var room = dg ? dg.Current : null;
                if (room != null && room.HasEnemies && room.Cleared && room.Contains(transform.position)) vacuum = true;
            }
            if (Magnetic && (vacuum || dist < CoinMagnetRadius))
            {
                // coins fly to you: gently at the edge of the magnet, fast up close, fast when vacuumed
                float speed = vacuum ? 16f : Mathf.Lerp(12f, 3f, dist / CoinMagnetRadius);
                transform.position += to.normalized * Mathf.Min(dist, speed * dt);
                grounded = true;
            }
            if (Proxy)
            {
                if (dist < 1.1f) Net.NetLoot.RequestTake(this); // the host also hands it over by itself
                return;
            }
            if (dist < 1.1f && player.Run && player.Run.CanAcquire(Item, out _)) TryCollect(player.Run, quiet: true);
        }

        public string Prompt => Price > 0
            ? $"{Bindings.Short(GameAction.Interact)}: buy {Item.Name} - {Price} gold"
            : $"{Bindings.Short(GameAction.Interact)}: take {Item.Name}";

        /// <summary>Takes (or buys) the item. Returns false and tells the player why if it can't.</summary>
        public bool TryCollect(Run run, bool quiet = false)
        {
            if (!this || !run) return false;
            if (Proxy) { Net.NetLoot.RequestTake(this, force: !quiet); return false; } // the host decides
            if (Net.NetLoot.Instanced) return Net.NetLoot.Take(this, run.Owner, quiet);
            if (!CollectInto(run, out var reason))
            {
                Deny(run, reason, quiet);
                return false;
            }
            Fx.Flash(visual.position, Item.Color, 1.2f, 0.3f, 1.5f);
            All.Remove(this);
            Destroy(gameObject);
            return true;
        }

        /// <summary>Pays and applies the item to this run (the pickup itself stays). False with a reason if it can't.</summary>
        public bool CollectInto(Run run, out string reason)
        {
            reason = null;
            if (Price > run.Gold) { reason = "Not enough gold"; return false; }
            if (!run.CanAcquire(Item, out reason)) return false;
            if (Price > 0)
            {
                run.Gold -= Price;
                if (run.Owner && run.Owner.IsLocal) Progress.Add("items_bought");
                Sfx.Play("buy", 0.7f);
            }
            run.Acquire(Item);
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
            var me = Player.Instance;
            foreach (var p in All)
            {
                if (!p || p.IsAuto) continue;
                if (me && !p.Proxy && p.NetId != 0 && !p.For(me)) continue; // yours is taken (or not yours)
                Vector3 d = p.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = p; }
            }
            return best;
        }
    }
}
