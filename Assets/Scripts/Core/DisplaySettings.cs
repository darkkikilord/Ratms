using System.Collections.Generic;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  PANTALLA Y RESOLUCIÓN (0.21).
    ///
    ///  El problema que había: el interruptor "Pantalla completa" hacía
    ///  <c>Screen.fullScreen = true</c>, que pone pantalla completa PERO con la
    ///  resolución que tuviera la ventana (p. ej. 1920×1080). En un monitor 2K
    ///  esa imagen se estiraba a 2560×1440 y se veía borrosa.
    ///  Ahora la pantalla completa va SIEMPRE a la resolución elegida (por
    ///  defecto, la nativa del monitor) y en Opciones se puede elegir otra.
    ///
    ///  PlayerPrefs: <c>opt_win</c> (0 ventana, 1 completa), <c>opt_res_w</c>/<c>opt_res_h</c>
    ///  (0 = nativa). Se aplica al arrancar el juego.
    /// </summary>
    public static class DisplaySettings
    {
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt("opt_win", Screen.fullScreen ? 1 : 0) == 1;
            private set => PlayerPrefs.SetInt("opt_win", value ? 1 : 0);
        }

        /// <summary>Resolución nativa del monitor (la de verdad, no la de la ventana).</summary>
        public static Vector2Int Native => new(Display.main.systemWidth, Display.main.systemHeight);

        /// <summary>Resoluciones disponibles (de mayor a menor, sin repetir ni las muy pequeñas).</summary>
        public static List<Vector2Int> Available()
        {
            var list = new List<Vector2Int>();
            var n = Native;
            float nativeAspect = (float)n.x / Mathf.Max(1, n.y);
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.y < 720 || list.Contains(v)) continue;
                // 0.30.1: solo las de la MISMA forma que el monitor (si no, en
                // pantalla completa salían franjas negras y el menú se deformaba).
                if (Mathf.Abs((float)v.x / v.y - nativeAspect) > 0.03f) continue;
                list.Add(v);
            }
            if (!list.Contains(n)) list.Add(n);
            list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
            return list;
        }

        /// <summary>Resolución elegida (o la nativa si no se ha elegido ninguna).</summary>
        public static Vector2Int Chosen
        {
            get
            {
                int w = PlayerPrefs.GetInt("opt_res_w", 0), h = PlayerPrefs.GetInt("opt_res_h", 0);
                return w > 0 && h > 0 ? new Vector2Int(w, h) : Native;
            }
        }

        public static void Set(bool fullscreen, Vector2Int res)
        {
            Fullscreen = fullscreen;
            bool native = res == Native;
            PlayerPrefs.SetInt("opt_res_w", native ? 0 : res.x);
            PlayerPrefs.SetInt("opt_res_h", native ? 0 : res.y);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            var r = Chosen;
            if (Fullscreen)
                Screen.SetResolution(r.x, r.y, FullScreenMode.FullScreenWindow);
            else
            {
                // En ventana, que quepa en el monitor con su marco.
                var n = Native;
                int w = Mathf.Min(r.x, n.x - 80), h = Mathf.Min(r.y, n.y - 120);
                if (r == n) { w = Mathf.RoundToInt(n.x * 0.8f); h = Mathf.RoundToInt(n.y * 0.8f); }
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            Debug.Log($"[Pantalla] {(Fullscreen ? "Completa" : "Ventana")} a {Screen.width}x{Screen.height} → pedida {r.x}x{r.y}");
        }

        /// <summary>Al arrancar: si hay preferencias guardadas, se aplican (no en el editor ni en las pruebas).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnBoot()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-ratmsAutoTest" || a.StartsWith("-screen-")) return;   // la prueba elige su ventana
            // 0.30.1: una resolución guardada con otra forma que el monitor (de
            // versiones viejas o de otro monitor) → volver a la nativa.
            {
                var c = Chosen; var n = Native;
                if (Mathf.Abs((float)c.x / Mathf.Max(1, c.y) - (float)n.x / Mathf.Max(1, n.y)) > 0.03f || c.x > n.x || c.y > n.y)
                {
                    PlayerPrefs.SetInt("opt_res_w", 0);
                    PlayerPrefs.SetInt("opt_res_h", 0);
                }
            }
            if (!PlayerPrefs.HasKey("opt_win"))
            {
                // Primera vez: si arranca en pantalla completa, que sea a la resolución nativa.
                if (Screen.fullScreen && (Screen.width != Native.x || Screen.height != Native.y))
                    Screen.SetResolution(Native.x, Native.y, FullScreenMode.FullScreenWindow);
                return;
            }
            Apply();
        }
    }
}
