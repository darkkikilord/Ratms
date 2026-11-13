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
            // Con dos ventanas, la que no tiene el foco no debe pausarse.
            Application.runInBackground = true;

            outDir = GetArg("-autoTestOut") ??
                     Path.Combine(Application.persistentDataPath, "AutoTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outDir);

            gameScene = GetArg("-autoTestScene") ?? gameScene;
            string biomeArg = GetArg("-autoTestBiome");
            if (biomeArg != null && Enum.TryParse(biomeArg, true, out BiomeType b))
                forcedBiome = b;

            Log($"Prueba automática iniciada. Salida: {outDir}");
            Log($"Escena: {gameScene} · Bioma: {(forcedBiome?.ToString() ?? "por defecto")} · Resolución: {Screen.width}x{Screen.height}");
            Log($"Sonidos generados por código: {Sfx.Warmup()}");
        }

        private void Start() => StartCoroutine(Run());

        private void Update()
        {
            // Vigilante: si algo se queda colgado (la rata no aparece, etc.),
            // no nos quedamos abiertos para siempre.
            if (!finished && Time.realtimeSinceStartup > maxSeconds)
                Finish($"TIMEOUT: la prueba superó {maxSeconds}s");

            WatchForFallThrough();
        }

        private readonly HashSet<int> reportedFalls = new();

        /// <summary>
        ///  Vigilante: si una rata viva aparece POR DEBAJO del fondo del
        ///  terreno, es que lo ha atravesado (un fallo de colisión).
        /// </summary>
        private void WatchForFallThrough()
        {
            if (finished || Time.frameCount % 10 != 0) return;
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            if (terrain == null) return;
            float bottom = terrain.transform.position.y;
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
            {
                if (h.IsDead.Value || reportedFalls.Contains(h.GetInstanceID())) continue;
                Vector2 b = h.BodyCenter;
                // Solo cuenta si está DENTRO del ancho del mapa (salir volando
                // por un lado y caer al agua es legal).
                float left = terrain.transform.position.x, right = left + terrain.WorldSize.x;
                if (b.y < bottom - 0.5f && b.x > left + 0.5f && b.x < right - 0.5f)
                {
                    reportedFalls.Add(h.GetInstanceID());
                    Problem($"{h.name} ha ATRAVESADO el terreno: está en ({b.x:0.0},{b.y:0.0}), por debajo del fondo ({bottom:0.0})");
                }
            }
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        // =========================================================
        // GUION DE LA PRUEBA
        // =========================================================
        private IEnumerator Run()
        {
            // Partida de DOS jugadores reales (dos copias del juego en el mismo PC).
            string role = GetArg("-autoTestRole");
            if (role == "host") { yield return RunHostMulti(); yield break; }
            if (role == "client") { yield return RunClientMulti(); yield break; }
            if (role == "practice") { yield return RunPractice(); yield break; }

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
                        // Solo informativo: en vuelo real el misil puede rozar algo.
                        // La prueba fiable del viento es WindTest (misil en el cielo).
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
            yield return ShotgunTest(nm, rat);
            yield return TeleportTest(nm, rat);
            yield return RopeTest(nm, rat);
            yield return WaterTest(rat);
            yield return CountdownTest(nm);

            Finish(null);
        }

        // =========================================================
        // MULTIJUGADOR REAL: HOST + CLIENTE (dos ejecutables)
        // =========================================================
        private IEnumerator RunHostMulti()
        {
            Log("Papel: HOST (partida de 2 jugadores)");
            yield return new WaitForSeconds(2f);
            NetworkManager nm = null;
            yield return WaitFor(() => (nm = NetworkManager.Singleton) != null, 10f, "NetworkManager");
            if (finished) yield break;
            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            if (menu != null) menu.HideAllPanels();
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");
            if (!nm.StartHost()) { Finish("No se pudo arrancar el host"); yield break; }
            Log("Host arrancado, esperando al cliente...");
            yield return WaitFor(() => nm.ConnectedClientsIds.Count >= 2, 60f, "que se conecte el cliente");
            if (finished) yield break;
            Log($"Cliente conectado ({nm.ConnectedClientsIds.Count} jugadores). Cargando escena...");
            yield return new WaitForSeconds(1f);
            nm.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);

            yield return WaitFor(() => FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Count(m => m.IsSpawned) >= 2,
                                 30f, "que aparezcan las dos ratas");
            if (finished) yield break;
            StartCoroutine(TurnWatcher());
            yield return new WaitForSeconds(1.5f);
            var mine = FindLocalRat();
            var other = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).FirstOrDefault(m => !m.IsOwner);
            if (mine != null && other != null)
            {
                float sep = Vector2.Distance(mine.transform.position, other.transform.position);
                Log($"Separación entre ratas al aparecer: {sep:0.0} u {(sep >= 10f ? "✓" : "⚠ muy juntas")}");
            }
            yield return Capture("M01_host_partida");

            int myTurns = 0;
            while (!finished)
            {
                yield return WaitFor(() => IsMyTurn(nm) || GameOver(), 90f, "mi turno (host)", fatal: false);
                if (GameOver() || finished) break;
                mine = FindLocalRat();
                other = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).FirstOrDefault(m => !m.IsOwner);
                if (mine == null) break;
                var weapons = mine.GetComponentInChildren<WeaponsMenuUI>();

                if (myTurns == 0 && other != null)
                {
                    // Bazooka hacia el cliente
                    yield return FaceTowards(mine, other.transform.position.x);
                    weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Bazooka;
                    mine.TestSetAim(45f);
                    yield return new WaitForSeconds(0.3f);
                    mine.TestPressFire();
                    yield return new WaitForSeconds(1.0f);
                    mine.TestReleaseFire();
                    Log("Host: bazooka disparado hacia el cliente");
                    yield return new WaitForSeconds(0.5f);
                    yield return Capture("M02_host_bazooka");
                }
                else if (myTurns == 1 && other != null)
                {
                    // Escopeta hacia el cliente (si está a tiro)
                    yield return FaceTowards(mine, other.transform.position.x);
                    weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Shotgun;
                    AimAt(mine, other.GetComponent<NetworkHealth>().BodyCenter);
                    yield return new WaitForSeconds(0.3f);
                    mine.TestPressFire();
                    yield return new WaitForSeconds(0.5f);
                    mine.TestPressFire();
                    Log("Host: dos tiros de escopeta");
                }
                else Log("Host: paso el turno sin hacer nada");
                myTurns++;
                yield return WaitFor(() => !IsMyTurn(nm) || GameOver(), 45f, "que acabe mi turno (host)", fatal: false);
            }

            yield return WaitFor(GameOver, 20f, "fin de la partida", fatal: false);
            var tm = NetworkTurnManager.Instance;
            bool won = tm != null && tm.GameEnded.Value && tm.WinnerClientId.Value == nm.LocalClientId;
            Log($"Fin de partida: {(tm != null && tm.GameEnded.Value ? "terminada" : "⚠ NO terminó")} · ganador = {(tm != null ? tm.WinnerClientId.Value.ToString() : "?")} · " +
                $"{(won ? "gana el HOST al irse el cliente ✓" : "⚠ el host no gana")}");
            if (!won) Problem("Multijugador: el host no ganó al desconectarse el cliente");
            yield return new WaitForSeconds(1f);
            yield return Capture("M09_host_victoria");
            Finish(null);
        }

        private IEnumerator RunClientMulti()
        {
            Log("Papel: CLIENTE (partida de 2 jugadores)");
            yield return new WaitForSeconds(3f);
            NetworkManager nm = null;
            yield return WaitFor(() => (nm = NetworkManager.Singleton) != null, 10f, "NetworkManager");
            if (finished) yield break;
            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            if (menu != null) menu.HideAllPanels();
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777);

            bool connected = false;
            for (int attempt = 0; attempt < 10 && !connected && !finished; attempt++)
            {
                nm.StartClient();
                float t = 0f;
                while (t < 5f && !nm.IsConnectedClient) { t += Time.deltaTime; yield return null; }
                connected = nm.IsConnectedClient;
                if (!connected) { nm.Shutdown(); yield return new WaitForSeconds(1f); }
            }
            if (!connected) { Finish("El cliente no pudo conectarse al host"); yield break; }
            Log("Cliente conectado al host ✓");

            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && FindLocalRat() != null, 40f, "entrar en la partida");
            if (finished) yield break;
            StartCoroutine(TurnWatcher());
            StartCoroutine(ClientWatcher());
            yield return new WaitForSeconds(1.5f);
            yield return Capture("C01_cliente_partida");

            int myTurns = 0;
            while (!finished)
            {
                yield return WaitFor(() => IsMyTurn(nm) || GameOver(), 90f, "mi turno (cliente)", fatal: false);
                if (GameOver() || finished) break;
                var mine = FindLocalRat();
                if (mine == null) { Log("Cliente: mi rata ha muerto"); break; }

                if (myTurns == 0)
                {
                    var weapons = mine.GetComponentInChildren<WeaponsMenuUI>();
                    weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Dynamite;
                    yield return new WaitForSeconds(0.4f);
                    mine.TestPressFire();
                    yield return new WaitForSeconds(0.6f);
                    var dyn = FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).FirstOrDefault(g => g.IsDynamite.Value);
                    bool visual = dyn != null && dyn.transform.Find("DynamiteVisual") != null;
                    Log($"Cliente: suelto dinamita → la veo {(dyn == null ? "⚠ NO existe" : visual ? "COMO DINAMITA ✓" : "⚠ sin dibujo de dinamita")}");
                    if (!visual) Problem("Multijugador: el cliente no ve la dinamita como dinamita");
                    yield return Capture("C02_cliente_dinamita");
                    // Huir
                    float side = mine.FacingRight ? -1f : 1f;
                    mine.TestInputX = side;
                    for (float t = 0f; t < 2.5f; t += 0.5f) { mine.TestPressJump(); mine.TestJumpHeld = true; yield return new WaitForSeconds(0.5f); }
                    mine.TestInputX = 0f; mine.TestJumpHeld = false;
                }
                else
                {
                    Log("Cliente: me desconecto A PROPÓSITO (el host debería ganar)");
                    yield return Capture("C03_cliente_antes_de_irse");
                    nm.Shutdown();
                    yield return new WaitForSeconds(0.5f);
                    Finish(null);
                    yield break;
                }
                myTurns++;
                yield return WaitFor(() => !IsMyTurn(nm) || GameOver(), 45f, "que acabe mi turno (cliente)", fatal: false);
            }
            Finish(null);
        }

        /// <summary>
        ///  Botón "Jugar" → modo Práctica: ¿arranca la partida local con 3
        ///  dianas? Y luego ESC×2: ¿vuelve al menú?
        /// </summary>
        private IEnumerator RunPractice()
        {
            Log("Papel: PRÁCTICA (botón Jugar)");
            yield return new WaitForSeconds(2.5f);
            var mm = FindFirstObjectByType<Ratms.UI.MainMenuController>();
            if (mm == null) { Finish("No encuentro MainMenuController"); yield break; }
            mm.OnPlay();
            Log("Pulsado 'Jugar'");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && FindLocalRat() != null, 30f, "entrar en la práctica");
            if (finished) yield break;
            yield return WaitFor(() => FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Count(m => m.IsTestDummy) >= 3,
                                 10f, "que aparezcan las 3 dianas", fatal: false);
            int n = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Count(m => m.IsTestDummy);
            Log($"Práctica: {n} dianas en el mapa {(n == 3 ? "✓" : "⚠")}");
            if (n != 3) Problem($"Práctica: se esperaban 3 dianas y hay {n}");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("P01_practica");

            TurnHUD.ExitToMenu();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 10f, "volver al menú", fatal: false);
            bool back = SceneManager.GetActiveScene().name == "MainMenu";
            Log($"Salir al menú (ESC×2) → {(back ? "de vuelta en el menú ✓" : "⚠ no volvió")}");
            if (!back) Problem("Práctica: no se pudo volver al menú");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("P02_menu_otra_vez");
            Finish(null);
        }

        /// <summary>Apunta cada cambio de turno con su viento (para comparar host y cliente).</summary>
        private IEnumerator TurnWatcher()
        {
            ulong last = ulong.MaxValue - 1;
            int n = 0;
            while (!finished)
            {
                var tm = NetworkTurnManager.Instance;
                if (tm != null && tm.CurrentTurnClientId.Value != last && tm.CurrentTurnClientId.Value != ulong.MaxValue)
                {
                    last = tm.CurrentTurnClientId.Value;
                    n++;
                    Log($"[turno {n}] le toca al cliente {last} · viento {tm.Wind.Value:+0.0;-0.0;0}");
                }
                yield return null;
            }
        }

        /// <summary>Cliente: ¿ve lo que hace el host?</summary>
        private IEnumerator ClientWatcher()
        {
            bool sawMissile = false;
            while (!finished && !sawMissile)
            {
                if (FindFirstObjectByType<NetworkMissile>() != null) { sawMissile = true; Log("Cliente: veo el misil del host volando ✓"); }
                yield return null;
            }
        }

        private static bool IsMyTurn(NetworkManager nm) =>
            NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId);

        private static bool GameOver() => NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.GameEnded.Value;

        private static IEnumerator FaceTowards(NetworkPlayerMovement rat, float targetX)
        {
            bool wantRight = targetX > rat.transform.position.x;
            if (rat.FacingRight == wantRight) yield break;
            rat.TestInputX = wantRight ? 1f : -1f;
            yield return new WaitForSeconds(0.08f);
            rat.TestInputX = 0f;
            yield return new WaitForSeconds(0.15f);
            rat.TestInputX = null;
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
            yield return CloseupWithColliders(rat, "09b_dinamita_en_mano", size: 1.4f, overlay: false);
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
            // Apuntamos AL CUERPO de la diana (puede haber resbalado más abajo).
            Vector2 toDummy = dHealth.BodyCenter - rat.GetComponent<NetworkHealth>().BodyCenter;
            rat.TestSetAim(Mathf.Atan2(toDummy.y, Mathf.Abs(toDummy.x)) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("14_bate_listo");
            yield return CloseupWithColliders(rat, "14b_bate_mira", size: 1.4f, overlay: false);

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

        /// <summary>Escopeta: dos disparos a la diana y el tercero bloqueado.</summary>
        private IEnumerator ShotgunTest(NetworkManager nm, NetworkPlayerMovement rat)
        {
            if (finished || rat == null || !rat.gameObject.activeInHierarchy) yield break;
            Log("---- Escopeta ----");
            yield return WaitFreshTurn(nm, rat);
            if (finished) yield break;
            // Buscamos un sitio con LÍNEA DE TIRO LIBRE hasta la diana (un bulto
            // del terreno en medio para el perdigonazo, como en el juego real).
            NetworkPlayerMovement dummy = null;
            NetworkHealth dh = null;
            foreach (var zone in new[] { (0.7f, 0.85f), (0.4f, 0.6f), (0.15f, 0.3f), (0.55f, 0.75f) })
            {
                yield return PlaceOnFlatSpot(rat, zone.Item1, zone.Item2, "escopeta");
                float side = rat.FacingRight ? 1f : -1f;
                dummy = SpawnDummy((Vector2)rat.transform.position + new Vector2(side * 2.5f, 0.3f));
                if (dummy == null) yield break;
                dh = dummy.GetComponent<NetworkHealth>();
                yield return new WaitForSeconds(0.9f);
                Vector2 from = rat.GetComponent<NetworkHealth>().BodyCenter;
                bool clear = true;
                foreach (var hh in Physics2D.LinecastAll(from, dh.BodyCenter))
                    if (hh.collider != null && !hh.collider.isTrigger && hh.collider.GetComponentInParent<NetworkHealth>() == null) { clear = false; break; }
                if (clear) break;
                Log("Escopeta: terreno en medio, pruebo otro sitio");
                dummy.GetComponent<NetworkObject>().Despawn(true);
                dummy = null;
            }
            if (dummy == null) { Log("Escopeta: sin línea de tiro libre en ningún sitio, se omite."); yield break; }

            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Shotgun;
            AimAt(rat, dh.BodyCenter);
            yield return new WaitForSeconds(0.3f);
            yield return CloseupWithColliders(rat, "18_escopeta_mira", size: 2f, overlay: false);

            int h0 = dh.CurrentHealth.Value;
            Vector2 rb0 = rat.GetComponent<NetworkHealth>().BodyCenter, db0 = dh.BodyCenter;
            Log($"Escopeta: rata en ({rb0.x:0.00},{rb0.y:0.00}) mirando {(rat.FacingRight ? "dcha" : "izq")}, arma ({rat.WeaponForward.x:0.00},{rat.WeaponForward.y:0.00}) · diana en ({db0.x:0.00},{db0.y:0.00})");
            rat.TestPressFire();
            yield return new WaitForSeconds(0.45f);
            int h1 = dh.CurrentHealth.Value;
            AimAt(rat, dh.BodyCenter);
            yield return new WaitForSeconds(0.15f);
            rat.TestPressFire();
            yield return new WaitForSeconds(0.45f);
            int h2 = dh.CurrentHealth.Value;
            rat.TestPressFire();   // tercero: no debe salir
            yield return new WaitForSeconds(0.4f);
            int h3 = dh.CurrentHealth.Value;
            Log($"Escopeta → diana {h0}→{h1}→{h2} (2 disparos) · tercer intento: {(h3 == h2 ? "bloqueado ✓" : "⚠ disparó otra vez")}" +
                (h1 < h0 && h2 < h1 ? "  ✓ los dos dieron" : h2 < h0 ? "  (alguno falló)" : "  ⚠ no dio ninguno"));
            if (h2 >= h0) Problem("Escopeta: la diana no recibió daño");
            if (h3 != h2) Problem("Escopeta: se pudo disparar más de 2 veces en un turno");
        }

        /// <summary>Teletransporte: primero a un sitio imposible (debe negarse), luego a uno válido.</summary>
        private IEnumerator TeleportTest(NetworkManager nm, NetworkPlayerMovement rat)
        {
            if (finished || rat == null || !rat.gameObject.activeInHierarchy) yield break;
            Log("---- Teletransporte ----");
            yield return WaitFreshTurn(nm, rat);
            if (finished) yield break;
            yield return PlaceOnFlatSpot(rat, 0.15f, 0.28f, "teletransporte");
            var tp = rat.GetComponent<NetworkTeleport>();
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = gen != null ? gen.GetComponent<DestructibleTerrain>() : null;
            if (tp == null || gen == null || terrain == null) { Problem("Teletransporte: falta componente"); yield break; }

            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Teleport;
            var health = rat.GetComponent<NetworkHealth>();
            Vector2 start = health.BodyCenter;

            // 1) Dentro de la tierra → no debe moverse. Buscamos bajo la rata un
            //    punto rodeado de tierra MACIZA de verdad (no un hueco de un cráter).
            Vector2 solidTarget = start + Vector2.down * 3f;
            for (float y = start.y - 1f; y > terrain.transform.position.y + 3f; y -= 0.25f)
            {
                var p = new Vector2(start.x, y);
                if (DestructibleTerrain.IsSolidAtWorld(p) && DestructibleTerrain.IsSolidAtWorld(p + Vector2.up * 0.3f) &&
                    DestructibleTerrain.IsSolidAtWorld(p + Vector2.down * 0.3f)) { solidTarget = p; break; }
            }
            tp.TestTarget = solidTarget;
            yield return null;
            rat.TestPressFire();
            yield return new WaitForSeconds(0.4f);
            bool stayed = Vector2.Distance(start, health.BodyCenter) < 0.3f && !rat.FiredThisTurn;
            Log($"Teletransporte dentro de la tierra → {(stayed ? "rechazado ✓" : "⚠ se movió")}");
            if (!stayed) Problem("Teletransporte: dejó aparecer dentro de la tierra");

            // 2) A otra zona (a ~10 u) → debe aparecer allí, sobre el suelo REAL actual
            float dirX = start.x < terrain.transform.position.x + terrain.WorldSize.x * 0.5f ? 1f : -1f;
            Vector2 target = default;
            bool found = false;
            foreach (float dist in new[] { 10f, 8f, 12f, 6f, 14f })
            {
                float x0 = start.x + dirX * dist;
                float? sy = CurrentSurfaceY(gen, x0);
                if (!sy.HasValue) continue;
                target = new Vector2(x0, sy.Value + 0.45f);
                if (tp.IsValidTarget(target, out _)) { found = true; break; }
            }
            if (!found) { Log("Teletransporte: no encontré destino válido, se omite."); yield break; }
            tp.TestTarget = target;
            yield return new WaitForSeconds(0.2f);
            yield return Capture("20_teletransporte_antes");
            rat.TestPressFire();
            yield return new WaitForSeconds(0.6f);
            float off = Vector2.Distance(target, health.BodyCenter);
            Log($"Teletransporte a ({target.x:0.0},{target.y:0.0}) → quedó a {off:0.00} u del destino · arma gastada: {rat.FiredThisTurn}" +
                (off < 0.8f ? "  ✓" : "  ⚠ no llegó"));
            if (off >= 0.8f) Problem($"Teletransporte: la rata no apareció en el destino (a {off:0.00} u)");
            yield return Capture("21_teletransporte_despues");
            tp.TestTarget = null;
        }

        /// <summary>Cuerda ninja: buscar dónde engancharse, balancearse, trepar y soltarse.</summary>
        private IEnumerator RopeTest(NetworkManager nm, NetworkPlayerMovement rat)
        {
            if (finished || rat == null || !rat.gameObject.activeInHierarchy) yield break;
            Log("---- Cuerda ninja ----");
            yield return WaitFreshTurn(nm, rat);
            if (finished) yield break;
            var rope = rat.GetComponent<NetworkNinjaRope>();
            if (rope == null) { Problem("Cuerda: falta componente"); yield break; }
            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Rope;
            var health = rat.GetComponent<NetworkHealth>();

            // Buscamos un sitio desde el que haya terreno ALTO a tiro (45°-85°):
            // con un enganche bajo la rata se queda apoyada y no puede columpiarse.
            float bestAngle = float.NaN, bestSide = 1f;
            foreach (var zone in new[] { (0.35f, 0.65f), (0.1f, 0.3f), (0.7f, 0.9f), (0.3f, 0.7f) })
            {
                yield return PlaceOnFlatSpot(rat, zone.Item1, zone.Item2, "cuerda");
                foreach (float sd in new[] { 1f, -1f })
                    for (float a = 85f; a >= 45f && float.IsNaN(bestAngle); a -= 5f)
                    {
                        Vector2 d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad) * sd, Mathf.Sin(a * Mathf.Deg2Rad));
                        foreach (var hh in Physics2D.RaycastAll(health.BodyCenter, d, rope.MaxLength * 0.95f))
                            if (hh.collider != null && hh.collider.GetComponentInParent<NetworkHealth>() == null && !hh.collider.isTrigger &&
                                hh.distance > 2f)
                            { bestAngle = a; bestSide = sd; break; }
                    }
                if (!float.IsNaN(bestAngle)) break;
            }
            if (float.IsNaN(bestAngle))
            {
                // Mapa sin techos a tiro: fabricamos una CUEVA con una explosión
                // enterrada y metemos la rata dentro, colgándose del techo.
                var gen = FindFirstObjectByType<TerrainGenerator>();
                var terr = gen != null ? gen.GetComponent<DestructibleTerrain>() : null;
                if (gen == null || terr == null || NetworkDestructibleTerrain.Instance == null) { Log("Cuerda: no se puede montar la cueva, se omite."); yield break; }
                float cx = terr.transform.position.x + terr.WorldSize.x * 0.5f;
                float? top = CurrentSurfaceY(gen, cx);
                if (top == null) yield break;
                Vector2 caveCenter = new Vector2(cx, top.Value - 3.2f);
                NetworkDestructibleTerrain.Instance.ServerCarveAt(caveCenter, 2.6f);
                yield return new WaitForSeconds(0.4f);
                var rrb = rat.GetComponent<Rigidbody2D>();
                Vector2 feetToPivot = (Vector2)rat.transform.position - health.BodyCenter;
                rrb.position = caveCenter + Vector2.down * 0.6f + feetToPivot;
                rrb.linearVelocity = Vector2.zero;
                yield return new WaitForFixedUpdate();
                rat.TestResetFallTracking();
                yield return new WaitForSeconds(0.8f);
                bestAngle = 80f; bestSide = rat.FacingRight ? 1f : -1f;
                Log("Cuerda: no había techos a tiro → cueva fabricada para la prueba");
            }

            // Mirar hacia ese lado y apuntar
            if (rat.FacingRight != (bestSide > 0f))
            {
                rat.TestInputX = bestSide;
                yield return new WaitForSeconds(0.08f);
                rat.TestInputX = 0f;
                yield return new WaitForSeconds(0.2f);
            }
            rat.TestSetAim(bestAngle);
            yield return new WaitForSeconds(0.25f);
            yield return CloseupWithColliders(rat, "22_cuerda_apuntando", size: 3f, overlay: false);
            rat.TestPressFire();
            yield return new WaitForSeconds(0.2f);
            if (!rope.IsAttached) { Problem($"Cuerda: no enganchó apuntando a {bestAngle}°"); rat.TestInputX = null; yield break; }

            Vector2 anchor = rope.Anchor;
            float len0 = rope.Length;
            // Primero TREPAR (para despegarse del suelo), como haría un jugador
            rat.TestRopeClimb = 1f;
            yield return new WaitForSeconds(0.8f);
            rat.TestRopeClimb = null;
            float len1 = rope.Length;
            // Balanceo: A/D alternando
            float minX = float.MaxValue, maxX = float.MinValue, t = 0f;
            while (t < 2.4f && !finished && rope.IsAttached)
            {
                t += Time.deltaTime;
                rat.TestInputX = Mathf.Sin(t * Mathf.PI / 0.6f) >= 0f ? 1f : -1f;
                float x = health.BodyCenter.x;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                yield return null;
            }
            rat.TestInputX = 0f;
            yield return Capture("23_cuerda_balanceo");
            // Soltar
            rat.TestPressFire();
            yield return new WaitForSeconds(0.2f);
            bool released = !rope.IsAttached;
            yield return new WaitForSeconds(1.5f);
            rat.TestInputX = null;
            Log($"Cuerda → enganchó a {bestAngle}° ({Vector2.Distance(health.BodyCenter, anchor):0.0} u) · balanceo {maxX - minX:0.00} u de lado a lado · " +
                $"trepar {len0:0.00}→{len1:0.00} u · soltar: {(released ? "✓" : "⚠")} · viva: {!health.IsDead.Value}" +
                (maxX - minX > 0.8f && len1 < len0 && released ? "  ✓" : "  ⚠"));
            if (maxX - minX <= 0.8f) Problem("Cuerda: casi no se balancea");
            if (!released) Problem("Cuerda: no se soltó");
        }

        private static void AimAt(NetworkPlayerMovement rat, Vector2 target)
        {
            Vector2 d = target - rat.GetComponent<NetworkHealth>().BodyCenter;
            rat.TestSetAim(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg);
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
            // Esta diana nace bajo el mapa A PROPÓSITO: que el vigilante no la cuente.
            reportedFalls.Add(dummy.GetComponent<NetworkHealth>().GetInstanceID());
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

            // 5) Primer plano de los pies + líneas de colliders
            yield return RefreshTurn();
            yield return PlaceOnFlatSpot(rat, 0.4f, 0.6f, "primer plano");
            yield return CloseupWithColliders(rat, "02e_pies");
            yield return CloseupWithColliders(rat, "02h_decoracion", size: 3f, overlay: false);

            // 6) Cráter: ¿se puede salir de un agujero de explosión?
            yield return CraterEscapeTest(rat, rb, 0.30f, 0.45f, depthBelowSurface: 0.2f, radius: 2.5f, label: "cráter de bazooka");
            yield return CraterEscapeTest(rat, rb, 0.48f, 0.62f, depthBelowSurface: 1.2f, radius: 2.4f, label: "explosión enterrada");

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

        // =========================================================
        // COLLIDERS: primer plano y cráteres
        // =========================================================
        /// <summary>
        ///  Acerca mucho la cámara a la rata y saca dos fotos: una normal y
        ///  otra con las LÍNEAS DE LOS COLLIDERS dibujadas encima (terreno en
        ///  magenta, rata en cian). Así se ve si los pies pisan la hierba de
        ///  verdad o el collider del suelo va por otro sitio.
        /// </summary>
        private IEnumerator CloseupWithColliders(NetworkPlayerMovement rat, string name, float size = 1.1f, bool overlay = true)
        {
            var cam = Camera.main;
            var follow = CameraFollow.Instance;
            if (cam == null || rat == null) yield break;

            // Paramos la cámara normal (sigue al pivote de la rata, que está
            // bajo el suelo) y la centramos nosotros en el CUERPO.
            bool followWasOn = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            float prevSize = cam.orthographicSize;
            Vector3 prevPos = cam.transform.position;
            Vector2 body = rat.GetComponent<NetworkHealth>().BodyCenter;
            cam.transform.position = new Vector3(body.x, body.y + size * 0.15f, prevPos.z);
            cam.orthographicSize = size;
            yield return null;
            yield return new WaitForSeconds(0.2f);
            yield return Capture(name + "_cerca");

            if (overlay)
            {
                var ov = DrawColliderOverlay(body, size * 2.5f);
                yield return null;
                yield return Capture(name + "_colliders");
                Destroy(ov);
            }

            cam.orthographicSize = prevSize;
            cam.transform.position = prevPos;
            if (follow != null) follow.enabled = followWasOn;
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>Dibuja con LineRenderers el contorno de todos los colliders cerca de un punto.</summary>
        private static GameObject DrawColliderOverlay(Vector2 around, float radius)
        {
            var root = new GameObject("[ColliderOverlay]");
            var mat = new Material(Shader.Find("Sprites/Default"));
            var seen = new HashSet<Collider2D>();
            foreach (var c in Physics2D.OverlapCircleAll(around, radius, ~0))
            {
                if (c == null || !seen.Add(c)) continue;
                bool isRat = c.GetComponentInParent<NetworkHealth>() != null;
                Color color = isRat ? Color.cyan : Color.magenta;
                foreach (var loop in ColliderLoops(c))
                {
                    var go = new GameObject("loop");
                    go.transform.SetParent(root.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.material = mat;
                    lr.startColor = lr.endColor = color;
                    lr.startWidth = lr.endWidth = 0.012f;
                    lr.sortingOrder = 5000;
                    lr.useWorldSpace = true;
                    lr.loop = loop.closed;
                    lr.positionCount = loop.points.Length;
                    for (int i = 0; i < loop.points.Length; i++)
                        lr.SetPosition(i, new Vector3(loop.points[i].x, loop.points[i].y, -1f));
                }
            }
            return root;
        }

        private static IEnumerable<(Vector2[] points, bool closed)> ColliderLoops(Collider2D c)
        {
            switch (c)
            {
                case PolygonCollider2D p:
                    for (int i = 0; i < p.pathCount; i++)
                        yield return (p.GetPath(i).Select(v => (Vector2)p.transform.TransformPoint(v + p.offset)).ToArray(), true);
                    break;
                case EdgeCollider2D e:
                    yield return (e.points.Select(v => (Vector2)e.transform.TransformPoint(v + e.offset)).ToArray(), false);
                    break;
                default:
                {
                    var bb = c.bounds;
                    yield return (new[] { new Vector2(bb.min.x, bb.min.y), new Vector2(bb.max.x, bb.min.y),
                                          new Vector2(bb.max.x, bb.max.y), new Vector2(bb.min.x, bb.max.y) }, true);
                    break;
                }
            }
        }

        /// <summary>
        ///  Abre un agujero (como una explosión) en una zona llana, mete la
        ///  rata en el fondo e intenta SALIR corriendo y saltando hacia un
        ///  lado durante 5 s. Mide si lo consigue y cuánto tarda.
        /// </summary>
        private IEnumerator CraterEscapeTest(NetworkPlayerMovement rat, Rigidbody2D rb, float fromFrac, float toFrac,
                                             float depthBelowSurface, float radius, string label)
        {
            if (finished || rat == null || NetworkDestructibleTerrain.Instance == null) yield break;
            var gen = FindFirstObjectByType<TerrainGenerator>();
            if (gen == null) yield break;

            yield return RefreshTurn();
            yield return PlaceOnFlatSpot(rat, fromFrac, toFrac, label);
            Vector2 body = rat.GetComponent<NetworkHealth>().BodyCenter;
            var surf = gen.GetSurfaceWorldPos(body.x);
            float surfaceY = surf.HasValue ? surf.Value.y : body.y;
            Vector2 blast = new Vector2(body.x, surfaceY - depthBelowSurface);

            // Apartamos la rata (al CIELO, lejos de cualquier tierra), abrimos
            // el agujero y la soltamos en el fondo.
            rb.position = new Vector2(body.x, rb.position.y + 12f);
            rb.linearVelocity = Vector2.zero;
            NetworkDestructibleTerrain.Instance.ServerCarveAt(blast, radius);
            yield return new WaitForSeconds(0.4f);
            Vector2 pivotOffset = (Vector2)rat.transform.position - rat.GetComponent<NetworkHealth>().BodyCenter;
            rb.position = new Vector2(blast.x, blast.y - radius * 0.55f) + pivotOffset;
            rb.linearVelocity = Vector2.zero;
            yield return new WaitForFixedUpdate();
            rat.TestResetFallTracking();
            yield return new WaitForSeconds(0.8f);
            yield return CloseupWithColliders(rat, $"02g_{(label.Contains("enterrada") ? "enterrada" : "crater")}", size: 2.2f);

            Vector2 start = rat.GetComponent<NetworkHealth>().BodyCenter;
            float t = 0f, nextJump = 0f;
            bool escaped = false;
            // Como un jugador: prueba a salir por la derecha y, si no puede,
            // por la izquierda (un lado puede dar a una pared o a una cueva).
            rat.TestInputX = 1f;
            while (t < 5f && !finished)
            {
                t += Time.deltaTime;
                if (t > 2.5f && rat.TestInputX > 0f) rat.TestInputX = -1f;
                if (t >= nextJump) { rat.TestPressJump(); rat.TestJumpHeld = true; nextJump = t + 0.55f; }
                if (t >= nextJump - 0.25f) rat.TestJumpHeld = false;
                Vector2 now = rat.GetComponent<NetworkHealth>().BodyCenter;
                // Fuera = de vuelta a la altura del suelo y más allá del borde
                // (el embudo hace la boca más ancha que el radio).
                var surfHere = gen.GetSurfaceWorldPos(now.x);
                float groundHere = surfHere.HasValue ? surfHere.Value.y : surfaceY;
                if (Mathf.Abs(now.x - blast.x) > radius * 1.25f && now.y > groundHere - 0.25f) { escaped = true; break; }
                yield return null;
            }
            rat.TestInputX = 0f;
            rat.TestJumpHeld = false;
            Vector2 end = rat.GetComponent<NetworkHealth>().BodyCenter;
            Log($"Salir del {label} (radio {radius}, centro {depthBelowSurface} u bajo el suelo) → " +
                (escaped ? $"✓ fuera en {t:0.0}s" : $"⚠ NO sale en 5s (se quedó en {end.x - blast.x:+0.00;-0.00},{end.y - surfaceY:+0.00;-0.00} respecto al borde)"));
            yield return null;
            rat.TestInputX = null;
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
                float? c = CurrentSurfaceY(gen, x);
                if (c == null) continue;
                float score = 0f;
                bool valid = true;
                for (float dx = -2f; dx <= 2f; dx += 0.5f)
                {
                    float? s = CurrentSurfaceY(gen, x + dx);
                    if (s == null) { valid = false; break; }
                    score += Mathf.Abs(s.Value - c.Value);
                }
                if (valid && score < bestScore) { bestScore = score; bestX = x; }
            }
            if (float.IsNaN(bestX)) yield break;

            float surfY = CurrentSurfaceY(gen, bestX).Value;
            var rb = rat.GetComponent<Rigidbody2D>();
            // Colocamos los PIES (fondo del collider) 0.1 u por encima del suelo real.
            var col = rat.GetComponent<Collider2D>();
            var box = rat.GetComponent<BoxCollider2D>();
            float feetOffset = (col.bounds.min.y - (box != null ? box.edgeRadius : 0f)) - rat.transform.position.y;
            rb.position = new Vector2(bestX, surfY + 0.1f - feetOffset);
            rb.linearVelocity = Vector2.zero;
            yield return new WaitForFixedUpdate();
            rat.TestResetFallTracking();
            yield return new WaitForSeconds(0.3f);
            yield return Settle(rb);
            Log($"({label}: rata colocada en zona llana x={bestX:0.0}, irregularidad {bestScore:0.00})");
        }

        /// <summary>
        ///  Altura REAL del suelo ahora mismo en la X dada (mirando los píxeles,
        ///  así cuenta los cráteres que ya se han hecho). Empieza 1.5 u por
        ///  encima de la superficie original (por debajo de las islas) y baja.
        /// </summary>
        private static float? CurrentSurfaceY(TerrainGenerator gen, float x)
        {
            var orig = gen.GetSurfaceWorldPos(x);
            if (orig == null) return null;
            float step = 1f / 32f;
            // Solo vale un suelo con AIRE encima: si empezamos dentro de una
            // isla flotante, primero hay que salir de ella.
            bool seenAir = false;
            for (float y = orig.Value.y + 1.5f; y > orig.Value.y - 8f; y -= step)
            {
                bool solid = DestructibleTerrain.IsSolidAtWorld(new Vector2(x, y));
                if (!solid) seenAir = true;
                else if (seenAir) return y;
            }
            return null;
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

        /// <summary>
        ///  Espera a tener el turno y lo RELLENA a tope (somos el servidor).
        ///  Para bloques largos de movimiento que si no se quedan sin tiempo.
        /// </summary>
        private IEnumerator RefreshTurn()
        {
            var nm = NetworkManager.Singleton;
            yield return WaitMyTurn(nm);
            var tm = NetworkTurnManager.Instance;
            if (tm != null && tm.IsTurnOf(nm.LocalClientId))
                tm.TurnTimeRemaining.Value = tm.TurnDuration;
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
