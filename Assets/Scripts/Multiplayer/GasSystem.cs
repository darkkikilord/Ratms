using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  NUBES DE GAS VENENOSO (0.21, bidón de gas). Como el fuego
    ///  (<see cref="FireSystem"/>), no son objetos de red: el servidor dice
    ///  dónde nacen (<see cref="NetworkTurnManager.GasClientRpc"/>) y cada PC
    ///  las simula igual: suben un poco, se frenan y el VIENTO las arrastra.
    ///  El daño lo decide SOLO el servidor (<see cref="ServerTick"/>): quien
    ///  respire el gas queda envenenado unos turnos y pierde algo de vida.
    /// </summary>
    public class GasSystem : MonoBehaviour
    {
        private struct Puff { public Vector2 pos, vel; public float life, maxLife, size; public SpriteRenderer sr; }

        private static GasSystem instance;
        private readonly List<Puff> puffs = new();
        private Sprite soft;
        private float nextServerTick;
        private readonly Dictionary<NetworkHealth, float> lastHurt = new();

        private static GasSystem I
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[Gas]");
                    instance = go.AddComponent<GasSystem>();
                }
                return instance;
            }
        }

        /// <summary>¿Hay gas en el mapa? (el turno espera un poco a que se disipe la nube recién soltada).</summary>
        public static bool Any => instance != null && instance.puffs.Count > 0;

        /// <summary>(Todos los PCs) Nace una nube.</summary>
        public static void Spawn(Vector2 pos, Vector2 vel, float life, float size)
        {
            var g = I;
            if (g.soft == null) g.soft = SoftCircle();
            var go = new GameObject("Gas");
            go.transform.SetParent(g.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = g.soft;
            sr.sortingOrder = 220;
            g.puffs.Add(new Puff { pos = pos, vel = vel, life = life, maxLife = life, size = size, sr = sr });
        }

        /// <summary>(Servidor) Crea una nube grande en <paramref name="at"/> y la reparte a todos.</summary>
        public static void ServerRelease(Vector2 at, int count = 10)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm == null || !tm.IsServer) return;
            var pos = new Vector2[count];
            var vel = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                pos[i] = at + Random.insideUnitCircle * 0.5f;
                vel[i] = Random.insideUnitCircle * 1.6f + Vector2.up * 0.6f;
            }
            tm.GasClientRpc(pos, vel);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float wind = NetworkTurnManager.WindAcceleration;
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i];
                p.life -= dt;
                if (p.life <= 0f || p.sr == null)
                {
                    if (p.sr != null) Destroy(p.sr.gameObject);
                    puffs.RemoveAt(i);
                    continue;
                }
                p.vel += new Vector2(wind * 0.35f, 0.12f) * dt;   // el viento lo arrastra, sube despacio
                p.vel *= Mathf.Exp(-1.2f * dt);
                p.pos += p.vel * dt;
                float age = 1f - p.life / p.maxLife;
                float grow = Mathf.Lerp(0.6f, 1.25f, Mathf.Clamp01(age * 3f));
                float alpha = Mathf.Clamp01(p.life / 1.5f) * Mathf.Clamp01(age * 8f) * 0.55f;
                p.sr.transform.position = new Vector3(p.pos.x, p.pos.y, -1f);
                p.sr.transform.localScale = Vector3.one * p.size * grow * 2.2f;
                p.sr.color = new Color(0.45f, 0.75f, 0.25f, alpha);
                puffs[i] = p;
            }
            if (puffs.Count == 0 && instance == this && lastHurt.Count > 0) lastHurt.Clear();

            var tm = NetworkTurnManager.Instance;
            if (tm != null && tm.IsServer && puffs.Count > 0 && Time.time >= nextServerTick)
            {
                nextServerTick = Time.time + 0.4f;
                ServerTick();
            }
        }

        /// <summary>(Servidor) Quien esté dentro de una nube: envenenada y un poco de daño (una vez por segundo como mucho).</summary>
        private void ServerTick()
        {
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
            {
                if (h == null || !h.IsSpawned || h.IsDead.Value) continue;
                Vector2 b = h.BodyCenter;
                bool inside = false;
                foreach (var p in puffs)
                {
                    float r = p.size * 1.15f;   // 0.22: casi lo que se ve (antes 0.9: la nube "no hacía nada")
                    if ((p.pos - b).sqrMagnitude < r * r && p.life > 0.4f) { inside = true; break; }
                }
                if (!inside) continue;
                if (lastHurt.TryGetValue(h, out float t) && Time.time - t < 1f) continue;
                lastHurt[h] = Time.time;
                h.PoisonServer(3);
                h.ApplyDamageServer(3, b, DamageType.Poison);
            }
        }

        private static Sprite SoftCircle()
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    float wobble = 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * wobble));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
    }
}
