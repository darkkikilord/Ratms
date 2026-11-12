using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ratms.Networking;
using Ratms.World;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  PRUEBA AUTOMÁTICA DE PARTIDA — el juego "se juega solo" unos segundos,
    ///  saca capturas PNG y escribe un informe. Pensado para que Claude pueda
    ///  "ver" el juego sin que nadie esté delante.
    ///
    ///  Solo se activa si el ejecutable se lanza con el argumento
    ///  <c>-ratmsAutoTest</c>. En una partida normal NO hace absolutamente nada
    ///  (ni siquiera crea su GameObject), así que es seguro dejarlo en el juego.
    ///
    ///  Qué hace, en orden:
    ///   1. Captura el menú principal.
    ///   2. Arranca un HOST LOCAL (127.0.0.1, sin Relay ni internet) y carga
    ///      la escena de juego igual que el botón "Empezar partida".
    ///   3. Espera a que aparezca su rata y le toque el turno → captura.
    ///   4. Equipa el bazooka (captura con la trayectoria) y dispara hacia el
    ///      centro del mapa → capturas del misil en vuelo y de la explosión.
    ///   5. Equipa la granada y la lanza → capturas.
    ///   6. Escribe <c>informe.txt</c> con la línea de tiempo, el estado de las
    ///      ratas en cada captura y TODOS los errores/excepciones que salieron
    ///      en consola. Después cierra el juego.
    ///
    ///  Argumentos opcionales:
    ///   -autoTestOut  &lt;carpeta&gt;   dónde guardar capturas e informe
    ///   -autoTestScene &lt;escena&gt;   escena de juego (por defecto Escenario_TerrainTest)
    ///   -autoTestBiome &lt;Meadow|Sewer&gt;  bioma a forzar
    ///
    ///  Normalmente no se lanza a mano: usa <c>tools/autotest.ps1</c>, que
    ///  compila la build y la ejecuta con estos argumentos.
    /// </summary>
    public class AutoPlayTest : MonoBehaviour
    {
        private const string ArgEnable = "-ratmsAutoTest";

        [Tooltip("Si la prueba tarda más que esto (segundos), se aborta y se escribe el informe igualmente.")]
        [SerializeField] private float maxSeconds = 240f;

        private string outDir;
        private string gameScene = "Escenario_TerrainTest";
        private BiomeType? forcedBiome;

        private readonly StringBuilder timeline = new();
        private readonly List<string> problems = new();
        private readonly List<string> warnings = new();
        private int errorCount;
        private int warningCount;
        private bool finished;
        private string failReason;

        // =========================================================
        // ARRANQUE (solo si hay -ratmsAutoTest en la línea de comandos)
        // =========================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!HasArg(ArgEnable)) return;

            var go = new GameObject("[AutoPlayTest]");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPlayTest>();
        }

        private void Awake()
        {
            // Capturamos TODO lo que pase por la consola desde el primer momento
            Application.logMessageReceived += OnLog;

            outDir = GetArg("-autoTestOut") ??
                     Path.Combine(Application.persistentDataPath, "AutoTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outDir);

            gameScene = GetArg("-autoTestScene") ?? gameScene;
            string biomeArg = GetArg("-autoTestBiome");
            if (biomeArg != null && Enum.TryParse(biomeArg, true, out BiomeType b))
                forcedBiome = b;

            Log($"Prueba automática iniciada. Salida: {outDir}");
            Log($"Escena: {gameScene} · Bioma: {(forcedBiome?.ToString() ?? "por defecto")} · Resolución: {Screen.width}x{Screen.height}");
        }

        private void Start() => StartCoroutine(Run());

        private void Update()
        {
            // Vigilante: si algo se queda colgado (la rata no aparece, etc.),
            // no nos quedamos abiertos para siempre.
            if (!finished && Time.realtimeSinceStartup > maxSeconds)
                Finish($"TIMEOUT: la prueba superó {maxSeconds}s");
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        // =========================================================
        // GUION DE LA PRUEBA
        // =========================================================
        private IEnumerator Run()
        {
            // --- 1) Menú principal ---------------------------------
            yield return new WaitForSeconds(2.5f);
            yield return Capture("01_menu");

            // --- 2) Host local + carga de la escena de juego --------
            NetworkManager nm = null;
            yield return WaitFor(() => (nm = NetworkManager.Singleton) != null, 10f, "NetworkManager en el menú");
            if (finished) yield break;

            if (forcedBiome.HasValue) BiomeManager.SelectedBiomeType = forcedBiome.Value;

            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            if (menu != null) menu.HideAllPanels();

            // Conexión directa por loopback: sin Relay, sin internet y sin
            // que Windows pregunte por el firewall (solo escucha en 127.0.0.1).
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");

            if (!nm.StartHost()) { Finish("No se pudo arrancar el host local"); yield break; }
            Log("Host local arrancado. Cargando escena de juego...");
            nm.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);

            // --- 3) Esperar rata + turno ----------------------------
            NetworkPlayerMovement rat = null;
            yield return WaitFor(() => (rat = FindLocalRat()) != null, 20f, "que aparezca la rata local");
            if (finished) yield break;

            yield return WaitFor(() => NetworkTurnManager.Instance != null &&
                                       NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId),
                                 15f, "que empiece el turno de la rata");
            if (finished) yield break;

            yield return new WaitForSeconds(1.5f);   // que caiga y se asiente
            yield return Capture("02_partida");

            // --- 3b) Movimiento: andar, frenar, saltar ---------------
            yield return MovementChecks(rat);

            // --- 3c) Viento: misil soltado en el cielo -----------------
            yield return WindTest();
            yield return WaitMyTurn(nm);
            if (finished) yield break;

            // --- 4) Bazooka -----------------------------------------
            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            var bazooka = rat.GetComponentInChildren<NetworkBazooka>();
            Vector2 aim = AimTowardsMapCenter(rat.transform.position);

            if (weapons != null && bazooka != null)
            {
                weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Bazooka;
                yield return new WaitForSeconds(0.6f);
                yield return Capture("03_apuntando_bazooka");

                // Simulamos "mantener ESPACIO ~1s y soltar" en un solo frame.
                bazooka.StartCharging();
                bazooka.TickCharging(1.1f, aim);
                bool fired = bazooka.ReleaseShot(aim);
                float wind = NetworkTurnManager.Instance != null ? NetworkTurnManager.Instance.Wind.Value : 0f;
                Log($"Bazooka disparado: {fired} (dirección {aim}) · viento {wind:+0.0;-0.0;0}");

                // VIENTO: medimos cuánto cambia la velocidad horizontal del
                // misil en vuelo. Sin viento debería quedarse igual; con viento,
                // cambiar en el sentido del viento.
                NetworkMissile missile = null;
                yield return WaitFor(() => (missile = FindFirstObjectByType<NetworkMissile>()) != null, 1f, "que aparezca el misil", fatal: false);
                if (missile != null)
                {
                    var mrb = missile.GetComponent<Rigidbody2D>();
                    yield return new WaitForFixedUpdate();
                    float vx0 = mrb != null ? mrb.linearVelocity.x : 0f;
                    float t0 = Time.time;
                    yield return new WaitForSeconds(0.3f);
                    if (missile != null && mrb != null)
                    {
                        float dvx = mrb.linearVelocity.x - vx0;
                        float expected = NetworkTurnManager.WindAcceleration * (Time.time - t0);
                        bool ok = Mathf.Abs(wind) < 0.05f ? Mathf.Abs(dvx) < 0.05f : Mathf.Sign(dvx) == Mathf.Sign(wind) && Mathf.Abs(dvx - expected) < 0.35f;
                        Log($"Viento sobre el misil → Δvx {dvx:+0.00;-0.00} en {(Time.time - t0):0.00}s (esperado ≈ {expected:+0.00;-0.00}) {(ok ? "✓" : "⚠ no cuadra")}");
                        if (!ok) Problem($"Viento: el misil cambió vx {dvx:0.00} y se esperaba ≈ {expected:0.00}");
                    }
                    else Log("Viento: el misil explotó antes de medir (muy cerca).");
                }

                yield return Capture("04_misil_en_vuelo");
                yield return WaitFor(() => FindFirstObjectByType<NetworkMissile>() == null, 6f, "que el misil explote", fatal: false);
                yield return Capture("05_explosion_bazooka");
                yield return new WaitForSeconds(1.5f);
                yield return Capture("06_tras_bazooka");
            }
            else Problem("La rata no tiene WeaponsMenuUI o NetworkBazooka — no se prueba el bazooka");

            // --- 5) Granada -----------------------------------------
            var thrower = rat != null ? rat.GetComponentInChildren<NetworkGrenadeThrower>() : null;
            if (rat != null && rat.gameObject.activeInHierarchy && weapons != null && thrower != null)
            {
                weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Grenade;
                yield return new WaitForSeconds(0.6f);

                aim = AimTowardsMapCenter(rat.transform.position);
                thrower.StartCharging();
                thrower.TickCharging(0.8f, aim);
                bool thrown = thrower.ReleaseShot(aim);
                Log($"Granada lanzada: {thrown}");

                yield return new WaitForSeconds(0.5f);
                yield return Capture("07_granada_en_vuelo");
                yield return WaitFor(() => FindFirstObjectByType<NetworkGrenade>() == null, 8f, "que la granada explote", fatal: false);
                yield return Capture("08_explosion_granada");
                yield return new WaitForSeconds(1.5f);
                yield return Capture("09_final");
            }
            else Log("Granada no probada (rata muerta o sin NetworkGrenadeThrower).");

            // --- 6) Armas nuevas, con la rata DIANA ------------------
            yield return DynamiteTest(nm, rat);
            yield return BatTest(nm, rat);
            yield return WaterTest(rat);
            yield return CountdownTest(nm);

            Finish(null);
        }

        // =========================================================
        // ARMAS NUEVAS
        // =========================================================
        /// <summary>
        ///  Dinamita por el camino REAL (pulsar ESPACIO con la dinamita
        ///  equipada): se suelta, la rata huye, explota junto a la diana.
        ///  También comprueba que no se puede usar una segunda arma.
        /// </summary>
        private IEnumerator DynamiteTest(NetworkManager nm, NetworkPlayerMovement rat)
        {
            if (finished || rat == null || !rat.gameObject.activeInHierarchy) yield break;
            Log("---- Dinamita ----");
            yield return WaitFreshTurn(nm, rat);
            if (finished) yield break;

            var rb = rat.GetComponent<Rigidbody2D>();
            yield return PlaceOnFlatSpot(rat, 0.15f, 0.35f, "dinamita");
            float side = rat.FacingRight ? 1f : -1f;
            var dummy = SpawnDummy((Vector2)rat.transform.position + new Vector2(side * 0.9f, 0.4f));
            if (dummy == null) yield break;
            var dHealth = dummy.GetComponent<NetworkHealth>();
            yield return new WaitForSeconds(0.8f);
            Vector2 dummyStart = dummy.transform.position;
            int hpBefore = dHealth.CurrentHealth.Value;

            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Dynamite;
            yield return null;
            yield return null;
            rat.TestPressFire();
            yield return new WaitForSeconds(0.3f);

            int count = FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).Count(g => g.IsDynamite.Value);
            Log($"Dinamita soltada: {(count == 1 ? "✓" : "⚠")} ({count} en el mapa) · arma usada este turno: {rat.FiredThisTurn}");
            if (count != 1) Problem($"Dinamita: se esperaba 1 en el mapa y hay {count}");

            // Un arma por turno: intentar soltar OTRA no debe hacer nada.
            rat.TestPressFire();
            yield return new WaitForSeconds(0.2f);
            int count2 = FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).Count(g => g.IsDynamite.Value);
            Log($"Segundo intento en el mismo turno → {(count2 == count ? "bloqueado ✓" : "⚠ se soltó otra")}");
            if (count2 != count) Problem("Un arma por turno: se pudo usar el arma dos veces en el mismo turno");

            yield return Capture("10_dinamita_mecha");

            // HUIR como lo haría un jugador: correr en dirección contraria
            // saltando cada medio segundo (para salir de cráteres/escalones).
            var dyn = FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).FirstOrDefault(g => g.IsDynamite.Value);
            Vector2 dynPos = dyn != null ? (Vector2)dyn.transform.position : (Vector2)rat.transform.position;
            var thrower = rat.GetComponentInChildren<NetworkGrenadeThrower>();
            float radius = thrower != null ? thrower.DynamiteRadius : 4f;

            rat.TestInputX = -side;
            float run = 0f, nextJump = 0f;
            while (run < 3.2f && !finished)
            {
                // Paramos en cuanto estamos a salvo (si no, corriendo hacia el
                // borde del mapa acabábamos en el agua).
                if (Vector2.Distance(dynPos, rat.transform.position) > radius + 1.2f) break;
                run += Time.deltaTime;
                if (run >= nextJump) { rat.TestPressJump(); rat.TestJumpHeld = true; nextJump = run + 0.5f; }
                if (run >= nextJump - 0.2f) rat.TestJumpHeld = false;
                yield return null;
            }
            rat.TestInputX = 0f;
            rat.TestJumpHeld = false;
            float escaped = Vector2.Distance(dynPos, rat.transform.position);
            Log($"Huida en 3.2s (corriendo y saltando): {escaped:0.00} u de la dinamita (radio {radius:0.0})" +
                (escaped > radius ? "  ✓ fuera del radio" : "  ⚠ no le da tiempo a salir del radio"));
            if (escaped <= radius)
            {
                // Para que la prueba pueda seguir, la sacamos nosotros.
                var gen = FindFirstObjectByType<TerrainGenerator>();
                float safeX = dynPos.x - side * (radius + 2f);
                var s = gen != null ? gen.GetSurfaceWorldPos(safeX) : null;
                var rrb = rat.GetComponent<Rigidbody2D>();
                rrb.position = s.HasValue ? new Vector2(s.Value.x, s.Value.y + 0.5f) : new Vector2(safeX, rat.transform.position.y + 2f);
                rrb.linearVelocity = Vector2.zero;
                Log("  (la prueba teletransporta la rata fuera del radio para poder seguir)");
            }
            yield return Capture("11_dinamita_huyendo");

            yield return WaitFor(() => !FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).Any(g => g.IsDynamite.Value),
                                 8f, "que explote la dinamita", fatal: false);
            yield return Capture("12_dinamita_explosion");
            yield return new WaitForSeconds(1.2f);
            rat.TestInputX = null;

            int hpAfter = dHealth.CurrentHealth.Value;
            float moved = Vector2.Distance(dummyStart, dummy.transform.position);
            Log($"Dinamita → diana: {hpBefore}→{hpAfter} hp{(dHealth.IsDead.Value ? " (MUERTA)" : "")}, desplazada {moved:0.00} u · " +
                $"rata propia: {rat.GetComponent<NetworkHealth>().CurrentHealth.Value} hp" +
                (hpAfter < hpBefore ? "  ✓" : "  ⚠ no le hizo daño"));
            if (hpAfter >= hpBefore) Problem("Dinamita: la diana no recibió daño");
            yield return Capture("13_dinamita_despues");
        }

        /// <summary>Bate por el camino real: diana delante, golpe, ¿vuela?</summary>
        private IEnumerator BatTest(NetworkManager nm, NetworkPlayerMovement rat)
        {
            if (finished || rat == null || !rat.gameObject.activeInHierarchy) yield break;
            Log("---- Bate ----");
            yield return WaitFreshTurn(nm, rat);
            if (finished) yield break;

            var rb = rat.GetComponent<Rigidbody2D>();
            yield return PlaceOnFlatSpot(rat, 0.65f, 0.85f, "bate");
            float side = rat.FacingRight ? 1f : -1f;
            var dummy = SpawnDummy((Vector2)rat.transform.position + new Vector2(side * 0.3f, 0.15f));
            if (dummy == null) yield break;
            var dHealth = dummy.GetComponent<NetworkHealth>();
            yield return new WaitForSeconds(0.8f);

            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Bat;
            rat.TestSetAim(20f);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("14_bate_listo");

            Vector2 dummyStart = dummy.transform.position;
            int hpBefore = dHealth.CurrentHealth.Value;
            Log($"Rata en ({rat.transform.position.x:0.00},{rat.transform.position.y:0.00}) mirando {(rat.FacingRight ? "derecha" : "izquierda")}, " +
                $"arma apunta ({rat.WeaponForward.x:0.00},{rat.WeaponForward.y:0.00}) · diana en ({dummyStart.x:0.00},{dummyStart.y:0.00})");
            rat.TestPressFire();
            yield return new WaitForSeconds(0.12f);
            yield return Capture("15_bate_golpe");

            // Seguimos el vuelo de la diana: distancia máxima recorrida.
            float maxDist = 0f, t = 0f;
            while (t < 2.5f && !finished)
            {
                t += Time.deltaTime;
                if (dummy != null && dummy.gameObject.activeInHierarchy)
                    maxDist = Mathf.Max(maxDist, Vector2.Distance(dummyStart, dummy.transform.position));
                yield return null;
            }
            int hpAfter = dHealth.CurrentHealth.Value;
            Log($"Bate → diana: {hpBefore}→{hpAfter} hp{(dHealth.IsDead.Value ? " (MUERTA)" : "")}, voló hasta {maxDist:0.00} u del sitio" +
                (hpAfter < hpBefore && maxDist > 1f ? "  ✓" : "  ⚠ no le dio o casi no la movió"));
            if (hpAfter >= hpBefore) Problem("Bate: la diana no recibió daño");
            yield return Capture("16_bate_despues");

            // Tras usar el bate, el bazooka no debe poder dispararse este turno.
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Bazooka;
            yield return null;
            rat.TestPressFire();
            yield return null;
            rat.TestReleaseFire();
            yield return new WaitForSeconds(0.3f);
            bool extraMissile = FindFirstObjectByType<NetworkMissile>() != null;
            Log($"Disparar bazooka tras el bate en el mismo turno → {(extraMissile ? "⚠ disparó" : "bloqueado ✓")}");
            if (extraMissile) Problem("Un arma por turno: se pudo disparar el bazooka tras usar el bate");
        }

        /// <summary>
        ///  VIENTO: fuerza un viento conocido, suelta un misil quieto en el
        ///  cielo (lejos de todo) y mide cuánto acelera en horizontal.
        ///  Después vuelve a dejar el viento como estaba.
        /// </summary>
        private IEnumerator WindTest()
        {
            if (finished) yield break;
            var tm = NetworkTurnManager.Instance;
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            GameObject prefab = null;
            foreach (var p in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                if (p.Prefab != null && p.Prefab.GetComponent<NetworkMissile>() != null) { prefab = p.Prefab; break; }
            if (tm == null || terrain == null || prefab == null) { Log("Viento: falta algo para la prueba, se omite."); yield break; }

            Log("---- Viento ----");
            float original = tm.Wind.Value;
            foreach (float testWind in new[] { 0.8f, -0.5f, 0f })
            {
                tm.Wind.Value = testWind;
                Vector2 sky = new Vector2(terrain.transform.position.x + terrain.WorldSize.x * 0.5f,
                                          terrain.transform.position.y + terrain.WorldSize.y + 8f);
                var go = Instantiate(prefab, sky, Quaternion.identity);
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                var m = go.GetComponent<NetworkMissile>();
                m.ServerInit(null, Vector2.zero);
                var mrb = go.GetComponent<Rigidbody2D>();

                yield return new WaitForFixedUpdate();
                float vx0 = mrb.linearVelocity.x, t0 = Time.fixedTime;
                yield return new WaitForSeconds(0.5f);
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedTime - t0;
                float dvx = mrb.linearVelocity.x - vx0;
                float expected = NetworkTurnManager.WindAcceleration * dt;
                bool ok = Mathf.Abs(dvx - expected) < 0.1f + Mathf.Abs(expected) * 0.15f;
                Log($"Viento {testWind:+0.0;-0.0;0} → Δvx del misil {dvx:+0.00;-0.00;0} en {dt:0.00}s (esperado {expected:+0.00;-0.00;0}) {(ok ? "✓" : "⚠")}");
                if (!ok) Problem($"Viento {testWind}: Δvx {dvx:0.00} y se esperaba {expected:0.00}");

                if (go != null && go.GetComponent<NetworkObject>().IsSpawned)
                    go.GetComponent<NetworkObject>().Despawn(destroy: true);
                yield return null;
            }
            tm.Wind.Value = original;
            yield return Capture("02d_hud_viento");
        }

        /// <summary>Una diana tirada al agua debe morir sin acabar la partida.</summary>
        private IEnumerator WaterTest(NetworkPlayerMovement rat)
        {
            if (finished) yield break;
            Log("---- Agua ----");
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            if (terrain == null) yield break;
            Vector2 drop = new Vector2(terrain.transform.position.x + terrain.WorldSize.x * 0.5f,
                                      terrain.transform.position.y - 4f);
            var tm = NetworkTurnManager.Instance;
            bool endedBefore = tm != null && tm.GameEnded.Value;
            var dummy = SpawnDummy(drop);
            if (dummy == null) yield break;
            var h = dummy.GetComponent<NetworkHealth>();
            yield return WaitFor(() => h.IsDead.Value, 3f, "que la diana muera en el agua", fatal: false);
            bool ended = !endedBefore && tm != null && tm.GameEnded.Value;
            Log($"Diana al agua → {(h.IsDead.Value ? "muerta ✓" : "⚠ sigue viva")} · partida terminada: {(ended ? "⚠ SÍ" : "no ✓")}");
            if (!h.IsDead.Value) Problem("Agua: la diana no murió al caer al agua");
            if (ended) Problem("Agua: la muerte de la diana terminó la partida");
        }

        /// <summary>Espera a los últimos segundos del turno para fotografiar la cuenta atrás en rojo.</summary>
        private IEnumerator CountdownTest(NetworkManager nm)
        {
            if (finished) yield break;
            Log("---- Cuenta atrás ----");
            yield return WaitFor(() => NetworkTurnManager.Instance != null &&
                                       NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId) &&
                                       NetworkTurnManager.Instance.TurnTimeRemaining.Value < 3.6f,
                                 40f, "los últimos segundos del turno", fatal: false);
            yield return Capture("17_cuenta_atras");
        }

        /// <summary>Spawnea una rata DIANA (server) en la posición dada.</summary>
        private NetworkPlayerMovement SpawnDummy(Vector2 pos)
        {
            GameObject prefab = null;
            foreach (var p in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                if (p.Prefab != null && p.Prefab.GetComponent<NetworkPlayerMovement>() != null) { prefab = p.Prefab; break; }
            if (prefab == null) { Problem("No encuentro el prefab de la rata en la lista de prefabs de red"); return null; }

            NetworkPlayerMovement.SpawnNextAsTestDummy = true;
            var go = Instantiate(prefab, pos, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            NetworkPlayerMovement.SpawnNextAsTestDummy = false;
            Log($"Diana creada en ({pos.x:0.0},{pos.y:0.0})");
            var dm = go.GetComponent<NetworkPlayerMovement>();
            StartCoroutine(DescribePhysicsLater(dm, 0.9f));
            return dm;
        }

        /// <summary>Diagnóstico: ¿la física "ve" a esta rata? (colliders, cuerpo, consultas).</summary>
        private IEnumerator DescribePhysicsLater(NetworkPlayerMovement m, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (m == null) yield break;
            var rb = m.GetComponent<Rigidbody2D>();
            var sb = new StringBuilder($"Física de {m.name}: rb simulated={rb?.simulated} body={rb?.bodyType} awake={rb?.IsAwake()} gravScale={rb?.gravityScale} layer={LayerMask.LayerToName(m.gameObject.layer)}" +
                                       $" | transform=({m.transform.position.x:0.00},{m.transform.position.y:0.00}) rb.position=({rb.position.x:0.00},{rb.position.y:0.00}) interp={rb.interpolation}");
            foreach (var c in m.GetComponentsInChildren<Collider2D>(true))
                sb.Append($" | col {c.GetType().Name} '{c.name}' enabled={c.enabled} trigger={c.isTrigger} active={c.gameObject.activeInHierarchy} center=({c.bounds.center.x:0.00},{c.bounds.center.y:0.00})");
            int AtPoint(Vector2 p) => Physics2D.OverlapCircleAll(p, 0.3f, ~0).Count(h => h.transform.IsChildOf(m.transform));
            var col0 = m.GetComponent<Collider2D>();
            sb.Append($" | Overlap en transform={AtPoint(m.transform.position)} en rb.position={AtPoint(rb.position)} en centro collider={(col0 != null ? AtPoint(col0.bounds.center) : -1)}");
            Log(sb.ToString());
        }

        /// <summary>Espera a un turno NUEVO de la rata (con el arma disponible).</summary>
        private IEnumerator WaitFreshTurn(NetworkManager nm, NetworkPlayerMovement rat)
        {
            yield return WaitFor(() => NetworkTurnManager.Instance != null &&
                                       NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId) &&
                                       !rat.FiredThisTurn &&
                                       NetworkTurnManager.Instance.TurnTimeRemaining.Value > 15f,
                                 45f, "un turno nuevo con arma disponible");
        }

        // =========================================================
        // MOVIMIENTO — mide andar, frenar y saltar con números
        // =========================================================
        private IEnumerator MovementChecks(NetworkPlayerMovement rat)
        {
            var rb = rat.GetComponent<Rigidbody2D>();
            if (rb == null) { Problem("La rata no tiene Rigidbody2D"); yield break; }

            var col = rat.GetComponent<Collider2D>();
            Log($"---- Movimiento ---- (tamaño collider rata: {(col != null ? col.bounds.size.ToString("0.00") : "?")}, " +
                $"interpolación={rb.interpolation}, colisión={rb.collisionDetectionMode})");
            yield return DescribePhysicsLater(rat, 0f);
            yield return PlaceOnFlatSpot(rat, 0.4f, 0.6f, "movimiento");

            // 1) Quieta: ¿se desliza sola (p. ej. en una cuesta)?
            yield return Settle(rb);
            Vector2 p0 = rb.position;
            yield return new WaitForSeconds(1.5f);
            Vector2 d = rb.position - p0;
            Log($"Quieta 1.5s → deriva X={d.x:+0.000;-0.000;0} Y={d.y:+0.000;-0.000;0}" +
                (Mathf.Abs(d.x) > 0.05f ? "  ⚠ se desliza" : ""));

            // 2) Andar a cada lado + frenada
            yield return Walk(rat, rb, +1f, 1.5f, "derecha");
            yield return Capture("02b_tras_andar");
            yield return Walk(rat, rb, -1f, 1.5f, "izquierda");

            // 3) Saltos
            yield return Jump(rat, rb, holdSeconds: 0.6f, inputX: 0f, label: "salto largo (mantener)");
            yield return Jump(rat, rb, holdSeconds: 0f,   inputX: 0f, label: "salto corto (toque)");
            yield return Jump(rat, rb, holdSeconds: 0.6f, inputX: 1f, label: "salto con carrerilla");

            // 4) Cuesta: ¿resbala estando quieta? ¿puede subirla?
            yield return SlopeCheck(rat, rb);

            rat.TestInputX = null;
            rat.TestJumpHeld = false;
        }

        private IEnumerator Walk(NetworkPlayerMovement rat, Rigidbody2D rb, float dir, float seconds, string label)
        {
            yield return Settle(rb);
            Vector2 start = rb.position;
            float maxVx = 0f, t = 0f;
            float minY = start.y, maxY = start.y;

            rat.TestInputX = dir;
            while (t < seconds && !finished)
            {
                t += Time.deltaTime;
                float vx = Mathf.Abs(rb.linearVelocity.x);
                if (vx > maxVx) maxVx = vx;
                minY = Mathf.Min(minY, rb.position.y);
                maxY = Mathf.Max(maxY, rb.position.y);
                yield return null;
            }
            // Velocidad al soltar = velocidad de "crucero".
            Vector2 atRelease = rb.position;
            float cruise = Mathf.Abs(rb.linearVelocity.x);

            rat.TestInputX = 0f;
            float stopT = 0f;
            while (stopT < 1.5f && Mathf.Abs(rb.linearVelocity.x) > 0.05f && !finished)
            {
                stopT += Time.deltaTime;
                yield return null;
            }
            Vector2 end = rb.position;
            rat.TestInputX = null;

            float dist = atRelease.x - start.x;
            float avg = Mathf.Abs(dist) / seconds;
            bool stuck = maxVx > 0.1f && avg < maxVx * 0.5f;
            Log($"Andar {label} {seconds}s → avance {dist:+0.00;-0.00} u · media {avg:0.00} u/s · máx {maxVx:0.00} · crucero {cruise:0.00} · " +
                $"altura {start.y:0.00}→{atRelease.y:0.00} (rango {maxY - minY:0.00}) · frenada {stopT:0.00}s / {Mathf.Abs(end.x - atRelease.x):0.000} u" +
                (stuck ? "  ⚠ posible atasco (media < 50% de la máx)" : "") +
                (Mathf.Abs(dist) < 0.05f ? "  ⚠ NO se ha movido" : ""));
        }

        private IEnumerator Jump(NetworkPlayerMovement rat, Rigidbody2D rb, float holdSeconds, float inputX, string label)
        {
            yield return Settle(rb);
            Vector2 start = rb.position;
            float peak = start.y, t = 0f, airT = 0f;
            bool tookOff = false, falling = false, landed = false;

            rat.TestInputX = inputX;
            rat.TestJumpHeld = holdSeconds > 0f;
            rat.TestPressJump();

            while (t < 3f && !finished)
            {
                t += Time.deltaTime;
                if (t >= holdSeconds) rat.TestJumpHeld = false;

                float y = rb.position.y, vy = rb.linearVelocity.y;
                if (y > peak) peak = y;
                if (!tookOff && y > start.y + 0.03f) tookOff = true;
                if (tookOff && vy < -0.05f) falling = true;
                if (falling && vy > -0.02f) { landed = true; airT = t; break; }
                yield return null;
            }
            rat.TestInputX = 0f;
            rat.TestJumpHeld = false;
            yield return null;
            rat.TestInputX = null;

            Log($"{label} → altura {peak - start.y:0.00} u · en el aire {(landed ? airT.ToString("0.00") + "s" : "¿?")} · avance X {rb.position.x - start.x:+0.00;-0.00}" +
                (!tookOff ? "  ⚠ NO despegó" : "") +
                (tookOff && !landed ? $"  ⚠ no aterrizó en 3s (vel. final {rb.linearVelocity.x:0.00},{rb.linearVelocity.y:0.00} · Y {start.y:0.00}→{rb.position.y:0.00})" : ""));
        }

        /// <summary>
        ///  Busca una CUESTA de ~30° en el terreno, pone la rata encima y mira
        ///  si se queda quieta o resbala. Después intenta subirla andando.
        /// </summary>
        private IEnumerator SlopeCheck(NetworkPlayerMovement rat, Rigidbody2D rb)
        {
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = gen != null ? gen.GetComponent<DestructibleTerrain>() : null;
            if (gen == null || terrain == null) { Log("Cuesta: no hay TerrainGenerator, se omite."); yield break; }

            // Solo el 70% central del mapa: los bordes están erosionados a
            // propósito y bajan al agua (no son cuestas "normales").
            float x0 = gen.transform.position.x, w = terrain.WorldSize.x;
            float bestX = float.NaN, bestAngle = 0f, bestScore = float.MaxValue;
            for (float x = x0 + w * 0.15f; x < x0 + w * 0.85f; x += 0.25f)
            {
                var a = gen.GetSurfaceWorldPos(x - 0.4f);
                var b = gen.GetSurfaceWorldPos(x + 0.4f);
                var c = gen.GetSurfaceWorldPos(x);
                if (a == null || b == null || c == null) continue;
                float angle = Mathf.Atan2(b.Value.y - a.Value.y, 0.8f) * Mathf.Rad2Deg;
                float score = Mathf.Abs(Mathf.Abs(angle) - 30f);
                if (score < bestScore) { bestScore = score; bestX = x; bestAngle = angle; }
            }
            if (float.IsNaN(bestX)) { Log("Cuesta: no se encontró ninguna, se omite."); yield break; }

            var surf = gen.GetSurfaceWorldPos(bestX).Value;
            rb.position = new Vector2(surf.x, surf.y + 0.35f);
            rb.linearVelocity = Vector2.zero;
            yield return new WaitForSeconds(0.8f);   // que caiga y se asiente
            Vector2 p0 = rb.position;
            yield return new WaitForSeconds(2f);
            Vector2 d = rb.position - p0;
            Log($"Cuesta de {Mathf.Abs(bestAngle):0}° (x={bestX:0.0}) → quieta 2s: deriva X={d.x:+0.000;-0.000;0} Y={d.y:+0.000;-0.000;0}" +
                (d.magnitude > 0.05f ? "  ⚠ resbala" : "  ✓ se queda quieta"));
            yield return Capture("02c_cuesta");

            // Subir la cuesta: el signo del ángulo dice hacia dónde sube
            float uphill = bestAngle > 0f ? 1f : -1f;
            yield return Walk(rat, rb, uphill, 1.2f, $"cuesta arriba {Mathf.Abs(bestAngle):0}°");
        }

        /// <summary>
        ///  Coloca la rata en la zona MÁS PLANA del tramo [desde, hasta] del
        ///  mapa (fracciones del ancho). Así cada bloque de la prueba empieza
        ///  en un sitio seguro y parecido, sin depender del spawn aleatorio
        ///  (que a veces cae junto al borde y la rata acaba en el agua).
        /// </summary>
        private IEnumerator PlaceOnFlatSpot(NetworkPlayerMovement rat, float fromFrac, float toFrac, string label)
        {
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = gen != null ? gen.GetComponent<DestructibleTerrain>() : null;
            if (gen == null || terrain == null || rat == null) yield break;

            float x0 = gen.transform.position.x, w = terrain.WorldSize.x;
            float bestX = float.NaN, bestScore = float.MaxValue;
            for (float x = x0 + w * fromFrac; x < x0 + w * toFrac; x += 0.25f)
            {
                var c = gen.GetSurfaceWorldPos(x);
                if (c == null) continue;
                float score = 0f;
                bool valid = true;
                for (float dx = -2f; dx <= 2f; dx += 0.5f)
                {
                    var s = gen.GetSurfaceWorldPos(x + dx);
                    if (s == null) { valid = false; break; }
                    score += Mathf.Abs(s.Value.y - c.Value.y);
                }
                if (valid && score < bestScore) { bestScore = score; bestX = x; }
            }
            if (float.IsNaN(bestX)) yield break;

            var surf = gen.GetSurfaceWorldPos(bestX).Value;
            var rb = rat.GetComponent<Rigidbody2D>();
            rb.position = new Vector2(surf.x, surf.y + 1.0f);
            rb.linearVelocity = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            yield return Settle(rb);
            Log($"({label}: rata colocada en zona llana x={bestX:0.0}, irregularidad {bestScore:0.00})");
        }

        /// <summary>Espera a que la rata esté quieta (o 2 s como mucho).</summary>
        private IEnumerator Settle(Rigidbody2D rb)
        {
            float still = 0f, t = 0f;
            while (t < 2f && still < 0.2f && !finished)
            {
                t += Time.deltaTime;
                still = rb.linearVelocity.sqrMagnitude < 0.01f ? still + Time.deltaTime : 0f;
                yield return null;
            }
        }

        private IEnumerator WaitMyTurn(NetworkManager nm)
        {
            yield return WaitFor(() => NetworkTurnManager.Instance != null &&
                                       NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId),
                                 10f, "volver a tener el turno");
        }

        // =========================================================
        // HELPERS DE LA PRUEBA
        // =========================================================
        private static NetworkPlayerMovement FindLocalRat()
        {
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                if (m.IsSpawned && m.IsOwner && !m.IsTestDummy) return m;
            return null;
        }

        /// <summary>Apunta a 45° hacia el centro del terreno (para no disparar al vacío).</summary>
        private static Vector2 AimTowardsMapCenter(Vector3 from)
        {
            float centerX = 0f;
            var terrains = FindObjectsByType<DestructibleTerrain>(FindObjectsSortMode.None);
            var renderers = terrains.Select(t => t.GetComponent<Renderer>()).Where(r => r != null).ToList();
            if (renderers.Count > 0) centerX = renderers.Average(r => r.bounds.center.x);

            float sx = (centerX >= from.x) ? 1f : -1f;
            return new Vector2(sx, 1f).normalized;
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string what, bool fatal = true)
        {
            float start = Time.realtimeSinceStartup;
            while (!finished)
            {
                bool ok;
                try { ok = condition(); }
                catch (Exception e) { ok = false; Problem($"Excepción esperando {what}: {e.Message}"); }
                if (ok) yield break;

                if (Time.realtimeSinceStartup - start > timeout)
                {
                    if (fatal) Finish($"No ocurrió a tiempo ({timeout}s): {what}");
                    else Log($"Aviso: no ocurrió a tiempo ({timeout}s): {what}");
                    yield break;
                }
                yield return null;
            }
        }

        private IEnumerator Capture(string name)
        {
            if (finished) yield break;
            yield return new WaitForEndOfFrame();

            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                string path = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Log($"📸 {name}.png  ·  {Snapshot()}");
            }
            catch (Exception e)
            {
                Problem($"No se pudo capturar {name}: {e.Message}");
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
        }

        /// <summary>Resumen del estado de la partida en este instante (va al informe).</summary>
        private static string Snapshot()
        {
            var sb = new StringBuilder();
            sb.Append($"escena={SceneManager.GetActiveScene().name}");

            var tm = NetworkTurnManager.Instance;
            if (tm != null)
            {
                string turn = tm.CurrentTurnClientId.Value == ulong.MaxValue ? "nadie" : tm.CurrentTurnClientId.Value.ToString();
                sb.Append($" · turno={turn} ({tm.TurnTimeRemaining.Value:0.0}s) · viento={tm.Wind.Value:+0.0;-0.0;0}");
                if (tm.GameEnded.Value) sb.Append(" · PARTIDA TERMINADA");
            }

            var rats = FindObjectsByType<NetworkHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var h in rats)
            {
                Vector3 p = h.transform.position;
                sb.Append($" · {h.name}: {h.CurrentHealth.Value}hp{(h.IsDead.Value ? " MUERTA" : "")} en ({p.x:0.0},{p.y:0.0})");
            }
            return sb.ToString();
        }

        // =========================================================
        // LOG E INFORME
        // =========================================================
        private void OnLog(string message, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    errorCount++;
                    string firstFrame = (stackTrace ?? "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
                    Problem($"[{type}] {message}{(string.IsNullOrEmpty(firstFrame) ? "" : "  ←  " + firstFrame)}");
                    break;
                case LogType.Warning:
                    warningCount++;
                    if (warnings.Count < 10 && !warnings.Contains(message)) warnings.Add(message);
                    break;
            }
        }

        private void Log(string msg)
        {
            timeline.AppendLine($"[{Time.realtimeSinceStartup,6:0.00}s] {msg}");
            Debug.Log($"[AutoPlayTest] {msg}");
        }

        private void Problem(string msg)
        {
            // Evitamos que un error que se repite cada frame llene el informe
            if (problems.Count < 200 && !problems.Contains(msg)) problems.Add(msg);
        }

        private void Finish(string reason)
        {
            if (finished) return;
            finished = true;
            failReason = reason;
            StopAllCoroutines();

            bool ok = reason == null && errorCount == 0;
            var sb = new StringBuilder();
            sb.AppendLine("==== RATMS · INFORME DE PRUEBA AUTOMÁTICA ====");
            sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Resultado: {(ok ? "OK" : "CON PROBLEMAS")}");
            if (reason != null) sb.AppendLine($"Motivo de parada: {reason}");
            sb.AppendLine($"Errores/excepciones en consola: {errorCount} · Warnings: {warningCount}");
            sb.AppendLine();
            sb.AppendLine("---- Línea de tiempo ----");
            sb.Append(timeline);
            sb.AppendLine();
            sb.AppendLine("---- Errores únicos ----");
            if (problems.Count == 0) sb.AppendLine("(ninguno)");
            foreach (var p in problems) sb.AppendLine("• " + p);
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---- Warnings (únicos, máx. 10) ----");
                foreach (var w in warnings) sb.AppendLine("· " + w);
            }

            try { File.WriteAllText(Path.Combine(outDir, "informe.txt"), sb.ToString(), Encoding.UTF8); }
            catch (Exception e) { Debug.LogError($"[AutoPlayTest] No se pudo escribir el informe: {e.Message}"); }

            Debug.Log($"[AutoPlayTest] Terminado. Resultado: {(ok ? "OK" : "CON PROBLEMAS")}");

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            Application.Quit(ok ? 0 : 1);
        }

        // =========================================================
        // LÍNEA DE COMANDOS
        // =========================================================
        private static bool HasArg(string name) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
