using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  0.32 — GUARDIÁN DEL TERRENO: que nada atraviese el mapa.
    ///
    ///  Cómo es nuestro terreno: una TEXTURA de píxeles (sólido / aire) y, para
    ///  la física, solo sus BORDES (<see cref="PixelContours"/> → EdgeCollider2D,
    ///  una línea por cada contorno). Una línea no tiene "dentro": si algo acaba
    ///  metido en la tierra (dinamita soltada en una cuesta, un agujero que se
    ///  abre justo debajo, un empujón muy fuerte...), Box2D no lo empuja fuera y
    ///  cae a través del mapa hasta el agua. Era el bug de la dinamita.
    ///
    ///  Este componente mira cada paso de física si el CENTRO del objeto está
    ///  dentro de un píxel sólido; si lo está, lo sube al primer hueco libre
    ///  (<see cref="DestructibleTerrain.EscapeUpDistance"/>) y le quita la
    ///  velocidad hacia abajo. Además barre el trayecto entre dos pasos: si el
    ///  objeto ha CRUZADO tierra (de aire a aire pasando por sólido, típico de
    ///  algo muy rápido) lo devuelve al último punto libre.
    ///
    ///  Solo actúa donde el Rigidbody es DINÁMICO, es decir, en el PC que manda
    ///  en su física (servidor para proyectiles, dueño para ratas); en los demás
    ///  es cinemático y la posición llega por red.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TerrainGuard : MonoBehaviour
    {
        /// <summary>Rescates hechos en esta partida (para las pruebas).</summary>
        public static int Rescues;

        private Rigidbody2D rb;
        private Collider2D col;
        private Vector2 lastFree;
        private bool hasLast;
        private float enabledAt;

        /// <summary>Añade el guardián si aún no lo tiene.</summary>
        public static TerrainGuard Attach(GameObject go)
        {
            var g = go.GetComponent<TerrainGuard>();
            return g != null ? g : go.AddComponent<TerrainGuard>();
        }

        private void OnEnable()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<Collider2D>();
            hasLast = false;
            enabledAt = Time.time;
        }

        private Vector2 Center => col != null ? (Vector2)col.bounds.center : rb.position;

        private void FixedUpdate()
        {
            if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic) { hasLast = false; return; }
            // Recién creado, la física aún puede tener la posición del prefab.
            if (Time.time - enabledAt < 0.1f) return;

            Vector2 c = Center;
            if (DestructibleTerrain.IsSolidAtWorld(c))
            {
                // 1) ¿Lo ha cruzado en este paso? Mejor volver al último sitio libre
                //    (si no, una granada que atraviesa una pared fina saldría por ARRIBA).
                if (hasLast && (c - lastFree).sqrMagnitude < 9f && !DestructibleTerrain.IsSolidAtWorld(lastFree))
                {
                    Shift(lastFree - c);
                    Vector2 v = rb.linearVelocity;
                    rb.linearVelocity = new Vector2(v.x * -0.3f, Mathf.Max(0f, v.y) * 0.3f);
                    Rescued("vuelve al último hueco libre");
                    return;
                }
                // 2) Si no, al primer hueco hacia ARRIBA (como las ratas).
                float? up = DestructibleTerrain.EscapeUpDistance(c, 4f) ?? DestructibleTerrain.EscapeUpDistance(c, 14f);
                if (up.HasValue)
                {
                    float ext = col != null ? col.bounds.extents.y : 0.1f;
                    Shift(Vector2.up * (up.Value + ext + 0.02f));
                    Vector2 v = rb.linearVelocity;
                    rb.linearVelocity = new Vector2(v.x * 0.5f, Mathf.Max(0f, v.y));
                    Rescued($"sube {up.Value + ext:F2} u");
                }
                return;
            }
            lastFree = c;
            hasLast = true;
        }

        private void Shift(Vector2 d)
        {
            // Mismo sitio para el cuerpo y el transform (si solo sumásemos a los
            // dos, con la sincronización de Unity el salto se aplicaba doble).
            Vector2 np = rb.position + d;
            rb.position = np;
            transform.position = new Vector3(np.x, np.y, transform.position.z);
        }

        private void Rescued(string how)
        {
            Rescues++;
            Debug.Log($"[TerrainGuard] {name} estaba dentro del terreno → {how}");
        }
    }
}
