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
        [SerializeField] private float maxSeconds = 120f;

        private string outDir;
        private string gameScene = "Escenario_TerrainTest";
        private BiomeType? forcedBiome;

        private readonly StringBuilder timeline = new();
        private readonly List<string> problems = new();
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
                Log($"Bazooka disparado: {fired} (dirección {aim})");

                yield return new WaitForSeconds(0.35f);
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

            Finish(null);
        }

        // =========================================================
        // HELPERS DE LA PRUEBA
        // =========================================================
        private static NetworkPlayerMovement FindLocalRat()
        {
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                if (m.IsSpawned && m.IsOwner) return m;
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
                sb.Append($" · turno={turn} ({tm.TurnTimeRemaining.Value:0.0}s)");
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
