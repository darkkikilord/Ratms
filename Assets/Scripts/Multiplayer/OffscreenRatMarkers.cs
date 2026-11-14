using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  FLECHITAS en el borde de la pantalla hacia las ratas que no se ven
    ///  (0.20), con el color de su equipo, su nombre y su vida. Discretas:
    ///  pequeñas y semitransparentes; la de la rata del turno late un poco.
    ///  En cámara libre, además, una línea muy tenue recuerda los controles.
    ///  Lo añade <see cref="CameraFollow"/> a la cámara.
    /// </summary>
    public class OffscreenRatMarkers : MonoBehaviour
    {
        private Camera cam;
        private Texture2D arrow;
        private GUIStyle label, hint;
        private float builtScale = -1f;

        private void OnGUI()
        {
            GUI.depth = 10;   // por DEBAJO de menús y paneles (más profundidad = más al fondo)
            if (Event.current.type != EventType.Repaint) return;
            if (cam == null) cam = GetComponent<Camera>() != null ? GetComponent<Camera>() : Camera.main;
            if (cam == null) return;
            var tm = NetworkTurnManager.Instance;
            if (tm == null || tm.GameEnded.Value || Ratms.UI.PauseMenu.IsOpen) return;

            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            Ensure(scale);
            float margin = 34f * scale;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
            {
                if (m == null || !m.IsSpawned || m.IsTestDummy) continue;
                var h = m.GetComponent<NetworkHealth>();
                if (h == null || h.IsDead.Value) continue;
                Vector3 sp = cam.WorldToScreenPoint(h.BodyCenter);
                if (sp.z < 0f) continue;
                var gp = new Vector2(sp.x, Screen.height - sp.y);   // coordenadas de GUI (Y hacia abajo)
                if (gp.x > 0f && gp.x < Screen.width && gp.y > 0f && gp.y < Screen.height) continue;   // se ve: nada

                // Punto del borde en la dirección de la rata.
                Vector2 dir = (gp - center).normalized;
                float tx = dir.x != 0f ? ((dir.x > 0 ? Screen.width - margin : margin) - center.x) / dir.x : float.MaxValue;
                float ty = dir.y != 0f ? ((dir.y > 0 ? Screen.height - margin : margin) - center.y) / dir.y : float.MaxValue;
                Vector2 edge = center + dir * Mathf.Min(Mathf.Abs(tx), Mathf.Abs(ty));

                bool turn = tm.CurrentRatId.Value == m.NetworkObjectId;
                Color col = TurnHUD.TeamTextColor(m.TeamIndex.Value);
                col.a = turn ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f) : 0.7f;
                float size = (turn ? 26f : 20f) * scale;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var prevM = GUI.matrix;
                var prevC = GUI.color;
                GUIUtility.RotateAroundPivot(ang, edge);
                GUI.color = col;
                GUI.DrawTexture(new Rect(edge.x - size * 0.5f, edge.y - size * 0.5f, size, size), arrow);
                GUI.matrix = prevM;

                // Nombre y vida, hacia dentro de la pantalla.
                Vector2 tp = edge - dir * (size + 26f * scale);
                var r = new Rect(tp.x - 90f * scale, tp.y - 12f * scale, 180f * scale, 24f * scale);
                label.normal.textColor = new Color(0f, 0f, 0f, col.a * 0.8f);
                GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), $"{m.RatName.Value} {h.CurrentHealth.Value}", label);
                label.normal.textColor = col;
                GUI.Label(r, $"{m.RatName.Value} {h.CurrentHealth.Value}", label);
                GUI.color = prevC;
            }

            // Recordatorio muy tenue en cámara libre.
            var follow = CameraFollow.Instance;
            if (follow != null && follow.FreeMode)
            {
                string t = Ratms.UI.Loc.T(follow.Overview ? "Vista general  ·  V o C: volver a tu rata" : "Cámara libre  ·  C: volver a tu rata  ·  V: vista general");
                // Bajo el panel del turno (abajo están las barras de vida)
                GUI.Label(new Rect(0f, 190f * scale, Screen.width, 24f * scale), t, hint);
            }
        }

        private void Ensure(float scale)
        {
            if (arrow == null)
            {
                const int n = 32;
                arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        // Triángulo que apunta a la derecha (+X), con borde oscuro.
                        float fx = x / (float)(n - 1), fy = Mathf.Abs(y / (float)(n - 1) - 0.5f) * 2f;
                        bool inside = fy <= 1f - fx;
                        bool rim = inside && fy > 1f - fx - 0.16f;
                        arrow.SetPixel(x, y, inside ? (rim ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white) : Color.clear);
                    }
                arrow.Apply();
            }
            if (Mathf.Approximately(builtScale, scale) && label != null) return;
            builtScale = scale;
            label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15 * scale), fontStyle = FontStyle.Bold };
            if (Ratms.UI.TrenchGUI.Stencil != null) label.font = Ratms.UI.TrenchGUI.Stencil;
            hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(16 * scale) };
            if (Ratms.UI.TrenchGUI.Typewriter != null) hint.font = Ratms.UI.TrenchGUI.Typewriter;
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        }
    }
}
