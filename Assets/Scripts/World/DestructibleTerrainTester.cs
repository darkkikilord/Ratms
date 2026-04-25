using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  Utilidad para PROBAR el sistema de terreno destructible sin tener que
    ///  lanzar una partida completa. Añádelo al mismo GameObject que el
    ///  <see cref="DestructibleTerrain"/> mientras desarrollamos y úsalo así:
    ///
    ///     · Click IZQUIERDO del ratón en el juego → carvea en ese punto.
    ///     · Mantén SHIFT + click → carvea con el radio grande.
    ///     · Tecla R → regenera el terreno de prueba (si el terrain tiene
    ///                 generateTestTerrainOnStart activado).
    ///
    ///  IMPORTANTE: es solo para desarrollo. Quítalo (o pon <see cref="enabled"/>
    ///  a false) en builds finales. En la Sesión 3 serán las EXPLOSIONES reales
    ///  las que llamen a CarveHole, no este script.
    /// </summary>
    [RequireComponent(typeof(DestructibleTerrain))]
    public class DestructibleTerrainTester : MonoBehaviour
    {
        [Header("Prueba con ratón")]
        [Tooltip("Radio pequeño — click normal.")]
        [SerializeField] private float smallRadius = 1.2f;

        [Tooltip("Radio grande — SHIFT+click. Simula una granada gorda.")]
        [SerializeField] private float bigRadius = 3f;

        [Header("Atajos")]
        [Tooltip("Tecla para regenerar el terreno de prueba desde cero (solo si generateTestTerrainOnStart=ON).")]
        [SerializeField] private KeyCode regenerateKey = KeyCode.R;

        private DestructibleTerrain terrain;
        private TerrainGenerator generator;    // opcional — si existe, R pide un mapa nuevo al procedural
        private Camera cam;

        private void Awake()
        {
            terrain = GetComponent<DestructibleTerrain>();
            generator = GetComponent<TerrainGenerator>();
            cam = Camera.main;
        }

        private void Update()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            // Click → carvea
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 screen = Input.mousePosition;
                screen.z = -cam.transform.position.z;       // la cámara suele estar en z=-10
                Vector3 world = cam.ScreenToWorldPoint(screen);
                float r = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        ? bigRadius : smallRadius;
                terrain.CarveHole(world, r);
                Debug.Log($"[TerrainTester] Carveado en {world} con radio {r}");
            }

            // R → regenera el terreno completo (útil para "deshacer" mientras pruebas)
            if (Input.GetKeyDown(regenerateKey))
            {
                if (generator != null)
                {
                    // Si hay generador procedural, cada R te saca un mapa NUEVO
                    // con distinta semilla (si randomSeedEachGenerate=ON).
                    generator.Generate();
                    Debug.Log("[TerrainTester] Nuevo mapa procedural generado");
                }
                else
                {
                    // Fallback: terreno de prueba con senos.
                    terrain.RegenerateTestTerrain();
                    Debug.Log("[TerrainTester] Terreno de prueba regenerado");
                }
            }
        }
    }
}
