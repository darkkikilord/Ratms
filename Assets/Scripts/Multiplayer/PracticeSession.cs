using System.Collections;
using System.Collections.Generic;
using Ratms.World;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  MODO PRÁCTICA — lo que hace el botón "Jugar" del menú.
    ///
    ///  Partida LOCAL tú solo (sin internet ni Relay): arranca un host en
    ///  127.0.0.1, carga el escenario y reparte <see cref="dummyCount"/>
    ///  RATAS DIANA por el mapa para practicar con todas las armas. Cuando
    ///  acabas con todas, salen otras nuevas.
    ///
    ///  Para salir: ESC dos veces (lo gestiona TurnHUD).
    ///
    ///  Antes el botón "Jugar" intentaba cargar la escena "Escenario 1",
    ///  que ya no existe (se renombró a "Escenario 1 obsoleto"), y no hacía nada.
    /// </summary>
    public class PracticeSession : MonoBehaviour
    {
        private const int DummyCount = 3;

        /// <summary>true mientras hay una práctica en marcha.</summary>
        public static bool Active { get; private set; }

        private string scene;
        private readonly List<NetworkHealth> dummies = new();

        public static void Begin(string gameScene)
        {
            if (Active) return;
            var go = new GameObject("[Práctica]");
            DontDestroyOnLoad(go);
            go.AddComponent<PracticeSession>().scene = gameScene;
        }

        private IEnumerator Start()
        {
            Active = true;
            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[Práctica] No hay NetworkManager en el menú."); End(); yield break; }

            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");   // local, sin Relay
            if (!nm.IsListening && !nm.StartHost()) { Debug.LogError("[Práctica] No se pudo arrancar el host local."); End(); yield break; }
            nm.SceneManager.LoadScene(scene, UnityEngine.SceneManagement.LoadSceneMode.Single);

            // Esperamos a que aparezca nuestra rata
            float t = 0f;
            while (t < 20f && LocalRat() == null) { t += Time.deltaTime; yield return null; }
            if (LocalRat() == null) { Debug.LogWarning("[Práctica] No apareció la rata."); End(); yield break; }
            yield return new WaitForSeconds(1f);

            while (nm != null && nm.IsListening)
            {
                SpawnDummies();
                // Cuando caen todas, nuevas dianas
                while (nm != null && nm.IsListening && dummies.Exists(d => d != null && !d.IsDead.Value))
                    yield return new WaitForSeconds(0.5f);
                if (nm == null || !nm.IsListening) break;
                var me = LocalRat();
                if (me != null) WorldDamagePopup.SpawnImpact(me.GetComponent<NetworkHealth>().BodyCenter + Vector2.up * 0.8f,
                                                             "¡Dianas fuera! Salen más...", new Color(1f, 0.9f, 0.3f));
                yield return new WaitForSeconds(3f);
            }
            End();
        }

        private void SpawnDummies()
        {
            dummies.Clear();
            var nm = NetworkManager.Singleton;
            var gen = FindFirstObjectByType<TerrainGenerator>();
            GameObject prefab = null;
            foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
                if (p.Prefab != null && p.Prefab.GetComponent<NetworkPlayerMovement>() != null) { prefab = p.Prefab; break; }
            if (prefab == null || gen == null) return;

            var used = new List<Vector3>();
            var me = LocalRat();
            if (me != null) used.Add(me.transform.position);
            for (int i = 0; i < DummyCount; i++)
            {
                Vector3? pos = gen.GetRandomValidSpawnWorld(used, 8f);
                if (!pos.HasValue) break;
                used.Add(pos.Value);
                NetworkPlayerMovement.SpawnNextAsTestDummy = true;
                var go = Instantiate(prefab, pos.Value + Vector3.up * 0.15f, Quaternion.identity);
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                NetworkPlayerMovement.SpawnNextAsTestDummy = false;
                dummies.Add(go.GetComponent<NetworkHealth>());
            }
        }

        private static NetworkPlayerMovement LocalRat()
        {
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                if (m.IsSpawned && m.IsOwner && !m.IsTestDummy) return m;
            return null;
        }

        private void End()
        {
            Active = false;
            Destroy(gameObject);
        }

        private void OnDestroy() => Active = false;
    }
}
