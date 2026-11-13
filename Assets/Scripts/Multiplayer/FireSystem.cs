using System.Collections.Generic;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  FUEGO (0.18): llamas que vuelan, caen, se quedan ardiendo en el suelo
    ///  y QUEMAN a las ratas que pisan.
    ///
    ///  Cómo funciona en red (igual que las cajas y barriles):
    ///   • El SERVIDOR decide dónde nace el fuego y lo avisa a todos con
    ///     <see cref="NetworkTurnManager.IgniteClientRpc"/> (posiciones y
    ///     velocidades iniciales). Cada PC simula las llamas por su cuenta:
    ///     el terreno es idéntico, así que caen casi igual en todos.
    ///   • El DAÑO solo lo calcula el servidor (cada 0.5 s, con SUS llamas),
    ///     como el resto de daño del juego. Así nadie ve números distintos.
    ///   • Las llamas NO son objetos de red: son baratas (un SpriteRenderer
    ///     cada una, reciclados) y puede haber cien a la vez.
    ///
    ///  Lo usan: lanzallamas, napalm aéreo y los barriles que revientan.
    ///  Una llama que toca un BARRIL lo hace explotar (lo decide el servidor).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class FireSystem : MonoBehaviour
    {
        private const int MaxFlames = 240;
        private const float Gravity = 13f;
        private const float DamageTick = 0.5f;
        private const int DamagePerTick = 4;

        private struct Flame
        {
            public Vector2 pos, vel;
            public float life, maxLife, age, phase;
            public bool stuck;
            public float nextFx;
        }

        private static FireSystem instance;
        private readonly List<Flame> flames = new();
        private readonly List<SpriteRenderer> pool = new();
        private float nextDamageTick, nextBurnSound;
        private WaterDeathZone water;
        private readonly List<NetworkHealth> rats = new();
        private float ratsRefreshAt;

        /// <summary>Llamas vivas ahora mismo (HUD, pruebas).</summary>
        public static int ActiveCount => instance != null ? instance.flames.Count : 0;

        private static FireSystem I
        {
            get
            {
                if (instance == null)
                {
                    // De la escena (NO persistente): al cambiar de escena se apaga todo.
                    var go = new GameObject("[Fuego]");
                    instance = go.AddComponent<FireSystem>();
                }
                return instance;
            }
        }

        /// <summary>
        ///  (SERVIDOR) Prende fuego: <paramref name="count"/> llamas que salen
        ///  de <paramref name="at"/> con velocidad <paramref name="baseVel"/> ±
        ///  <paramref name="spread"/>. Las ven y simulan todos los clientes.
        /// </summary>
        public static void ServerIgnite(Vector2 at, Vector2 baseVel, float spread, int count, float minLife = 3.5f, float maxLife = 6f)
        {
            if (count <= 0) return;
            var pos = new Vector2[count];
            var vel = new Vector2[count];
            var life = new float[count];
            for (int i = 0; i < count; i++)
            {
                pos[i] = at + Random.insideUnitCircle * 0.12f;
                vel[i] = baseVel + Random.insideUnitCircle * spread;
                life[i] = Random.Range(minLife, maxLife);
            }
            var tm = NetworkTurnManager.Instance;
            if (tm != null && tm.IsSpawned) tm.IgniteClientRpc(pos, vel, life);
            else for (int i = 0; i < count; i++) Spawn(pos[i], vel[i], life[i]);   // sin red (pruebas)
        }

        /// <summary>(Local) Crea una llama. Lo llama el ClientRpc en cada PC.</summary>
        public static void Spawn(Vector2 pos, Vector2 vel, float life)
        {
            var fx = I;
            if (fx.flames.Count >= MaxFlames) fx.flames.RemoveAt(0);   // la más vieja se apaga
            fx.flames.Add(new Flame { pos = pos, vel = vel, life = life, maxLife = life, phase = Random.value * 10f });
        }

        /// <summary>¿Hay fuego a menos de <paramref name="radius"/> de este punto? (IA, pruebas)</summary>
        public static int CountNear(Vector2 at, float radius)
        {
            if (instance == null) return 0;
            int n = 0;
            float r2 = radius * radius;
            foreach (var f in instance.flames) if ((f.pos - at).sqrMagnitude <= r2) n++;
            return n;
        }

        /// <summary>SOLO PRUEBAS: apaga todo el fuego local de golpe.</summary>
        public static void TestClearAll()
        {
            if (instance != null) instance.flames.Clear();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (water == null) water = FindFirstObjectByType<WaterDeathZone>();
            if (Time.time >= ratsRefreshAt)
            {
                ratsRefreshAt = Time.time + 0.5f;
                rats.Clear();
                foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
                    if (h != null && h.IsSpawned && !h.IsDead.Value) rats.Add(h);
            }

            float wind = NetworkTurnManager.WindAcceleration;
            for (int i = flames.Count - 1; i >= 0; i--)
            {
                var f = flames[i];
                f.age += dt;
                f.life -= dt;

                // Al agua: se apaga con un chorro de vapor.
                if (water != null && f.pos.y < water.SurfaceY)
                {
                    Fx.Sparks(f.pos, 2, new Color(0.85f, 0.9f, 1f, 0.8f), 0.5f, 1.5f);
                    f.life = 0f;
                }

                if (f.life <= 0f) { flames.RemoveAt(i); continue; }

                if (!f.stuck) MoveFlying(ref f, dt, wind);
                else if (!DestructibleTerrain.IsSolidAtWorld(f.pos + Vector2.down * 0.06f))
                {
                    // Le han volado el suelo de debajo: vuelve a caer.
                    f.stuck = false;
                    f.vel = Vector2.zero;
                }

                // Humo, chispas y chamuscado del suelo (solo visual, local).
                if (Time.time >= f.nextFx)
                {
                    f.nextFx = Time.time + Random.Range(0.25f, 0.6f);
                    if (Random.value < 0.35f) Fx.Sparks(f.pos + Vector2.up * 0.1f, 1, new Color(1f, 0.7f, 0.2f, 1f), 0.6f, 1.6f);
                    if (f.stuck) DestructibleTerrain.StainAtWorld(f.pos + Vector2.down * 0.05f, new Color32(30, 22, 18, 255), 1, 0.35f);
                }
                flames[i] = f;
            }

            Draw();

            // Crepitar mientras haya fuego.
            if (flames.Count > 0 && Time.time >= nextBurnSound)
            {
                nextBurnSound = Time.time + Random.Range(0.35f, 0.7f);
                Sfx.Play(Sfx.Id.Burn, Mathf.Clamp01(flames.Count / 30f) * 0.6f + 0.15f);
            }

            var nm = NetworkManager.Singleton;
            bool server = nm == null || !nm.IsListening || nm.IsServer;
            if (server && Time.time >= nextDamageTick)
            {
                nextDamageTick = Time.time + DamageTick;
                if (flames.Count > 0) ServerBurn();
            }
        }

        /// <summary>
        ///  Llama en el aire: gravedad, viento, choque con el terreno (se
        ///  queda pegada al suelo o resbala por la pared) y con las RATAS
        ///  (la llama se les queda encima: así el lanzallamas "pega").
        /// </summary>
        private void MoveFlying(ref Flame f, float dt, float wind)
        {
            f.vel.y -= Gravity * dt;
            f.vel.x += wind * 0.5f * dt;
            f.vel *= 1f - 0.6f * dt;   // el aire la frena (fuego líquido, no bala)

            Vector2 next = f.pos + f.vel * dt;
            if (DestructibleTerrain.IsSolidAtWorld(next))
            {
                if (DestructibleTerrain.IsSolidAtWorld(new Vector2(f.pos.x, next.y)) && f.vel.y < 0f)
                {
                    // Suelo: se queda ardiendo encima (subimos hasta el aire).
                    Vector2 p = new Vector2(f.pos.x, next.y);
                    for (int k = 0; k < 12 && DestructibleTerrain.IsSolidAtWorld(p); k++) p.y += 1f / 32f;
                    f.pos = p;
                    f.vel = Vector2.zero;
                    f.stuck = true;
                }
                else
                {
                    // Pared o techo: rebota sin fuerza y cae.
                    f.vel = new Vector2(-f.vel.x * 0.15f, Mathf.Min(f.vel.y, 0f) * 0.3f);
                }
                return;
            }
            f.pos = next;

            // Contra una rata: frena en seco y cae sobre ella (le prende).
            if (f.age > 0.12f)
                foreach (var h in rats)
                {
                    if (h == null) continue;
                    Vector2 c = h.BodyCenter;
                    if (Mathf.Abs(f.pos.x - c.x) < 0.28f && f.pos.y > c.y - 0.35f && f.pos.y < c.y + 0.4f)
                    {
                        f.vel *= 0.15f;
                        break;
                    }
                }
        }

        /// <summary>(SERVIDOR) Daño a las ratas metidas en el fuego y barriles que revientan.</summary>
        private void ServerBurn()
        {
            foreach (var h in rats)
            {
                if (h == null || !h.IsSpawned || h.IsDead.Value) continue;
                Vector2 c = h.BodyCenter;
                int touching = 0;
                Vector2 src = c;
                foreach (var f in flames)
                {
                    if (Mathf.Abs(f.pos.x - c.x) < 0.34f && f.pos.y > c.y - 0.5f && f.pos.y < c.y + 0.4f)
                    {
                        touching++;
                        src = f.pos;
                    }
                }
                if (touching == 0) continue;
                int dmg = DamagePerTick + (touching >= 4 ? 2 : 0);
                // Fuego "por abajo": la zona de mutilación son las patas (src bajo el cuerpo).
                h.ApplyDamageServer(dmg, c, DamageType.Fire, src + Vector2.down * 0.3f);
                // Saltito de dolor alejándose de la llama (como quien pisa brasas).
                float away = Mathf.Sign(c.x - src.x);
                if (away == 0f) away = Random.value < 0.5f ? -1f : 1f;
                h.ApplyKnockbackClientRpc(new Vector2(away * 2.2f, 3.2f), 0.15f);
                BurningFxClientRpcFor(h);
            }

            var tm = NetworkTurnManager.Instance;
            if (tm != null)
                foreach (var f in flames)
                    if (f.stuck || f.age > 0.2f) tm.ServerFireTouches(f.pos);
        }

        private static void BurningFxClientRpcFor(NetworkHealth h)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && tm.IsSpawned) tm.BurningFxClientRpc(h.NetworkObjectId);
        }

        // ============================================================
        //  DIBUJO
        // ============================================================
        private void Draw()
        {
            while (pool.Count < flames.Count)
            {
                var go = new GameObject("Llama");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 70;
                pool.Add(sr);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                var sr = pool[i];
                if (i >= flames.Count)
                {
                    if (sr.enabled) sr.enabled = false;
                    continue;
                }
                var f = flames[i];
                if (!sr.enabled) sr.enabled = true;
                int frame = (int)((Time.time + f.phase) * 12f);
                sr.sprite = WeaponArt.Flame(frame);
                // Crece al prender y encoge al apagarse.
                float lifeT = Mathf.Clamp01(f.life / Mathf.Max(0.01f, f.maxLife));
                float grow = Mathf.Clamp01(f.age * 6f);
                float s = (0.55f + 0.75f * Mathf.Sqrt(lifeT)) * grow;
                float flick = 1f + 0.12f * Mathf.Sin((Time.time + f.phase) * 23f);
                sr.transform.position = new Vector3(f.pos.x, f.pos.y - 0.03f, 0f);
                sr.transform.localScale = new Vector3(s * flick, s * (2f - flick), 1f);
                sr.flipX = (frame / 3) % 2 == 0;
                sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(lifeT * 4f));
            }
        }
    }
}
