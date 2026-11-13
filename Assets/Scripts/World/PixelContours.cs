using System.Collections.Generic;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>Cómo construye <see cref="DestructibleTerrain"/> su collider.</summary>
    public enum TerrainColliderMode
    {
        /// <summary>Contornos exactos píxel a píxel (EdgeCollider2D por bucle). Recomendado.</summary>
        PixelContours = 0,
        /// <summary>Sistema antiguo: línea superior suavizada + cuevas cerradas (PolygonCollider2D).</summary>
        LegacySkyline = 1,
    }

    /// <summary>
    ///  Saca los CONTORNOS EXACTOS de la parte sólida de una imagen de píxeles
    ///  (algoritmo "marching squares"). Devuelve una lista de bucles cerrados:
    ///  el borde exterior del terreno, el de cada cueva, cráter, isla, etc.
    ///
    ///  Por qué existe: el collider antiguo ("skyline") solo seguía la línea
    ///  más alta de cada columna y la suavizaba. Eso hacía que:
    ///   · en los bultos el suelo físico quedase POR DEBAJO de la hierba (la
    ///     rata se hundía) y en los valles por encima (flotaba);
    ///   · cualquier hueco bajo un saliente (p. ej. un cráter abierto de lado)
    ///     contase como TIERRA MACIZA para la física aunque se viese vacío.
    ///  Con contornos exactos, lo que ves es lo que pisas.
    ///
    ///  Cómo funciona, en corto:
    ///   1) Cada píxel es sólido o aire (<see cref="DestructibleTerrain.IsSolidPixel"/>).
    ///   2) Miramos cada cuadradito de 2×2 píxeles. Según cuáles sean sólidos
    ///      (16 casos), el borde pasa por el medio de ciertos lados del
    ///      cuadradito → trozos de línea con dirección (sólido a la izquierda).
    ///   3) Encadenamos los trozos: cada punto tiene exactamente un trozo que
    ///      sale y otro que entra → salen bucles cerrados.
    ///   4) Simplificamos cada bucle (Ramer–Douglas–Peucker) para que no tenga
    ///      miles de vértices, sin desviarnos más de ~1 píxel del dibujo.
    ///
    ///  Coste: recorre el mapa entero (~2 M de celdas en un mapa de 2048×1024).
    ///  Son unos milisegundos; solo se hace al generar el mapa y tras cada
    ///  explosión. Reutiliza buffers para no generar basura.
    /// </summary>
    public static class PixelContours
    {
        private static byte[] solid;
        private static int[] next;
        private static readonly List<int> starts = new();
        private static readonly List<Vector2> loopPts = new();

        /// <summary>
        ///  Contornos en UNIDADES LOCALES del terreno (píxel / ppu, origen en
        ///  la esquina inferior izquierda). Cada array viene CERRADO (el último
        ///  punto repite el primero), listo para un EdgeCollider2D.
        /// </summary>
        /// <param name="simplifyPx">Desviación máxima permitida al simplificar, en píxeles.</param>
        /// <param name="minLoopPoints">Bucles con menos puntos (motas sueltas de 1-3 px) se descartan.</param>
        public static List<Vector2[]> Build(Color32[] pixels, int w, int h, float ppu,
                                            float simplifyPx, int minLoopPoints)
        {
            var result = new List<Vector2[]>();
            if (pixels == null || w <= 0 || h <= 0) return result;

            // ---- 1) Rejilla sólido/aire ----
            int n = w * h;
            if (solid == null || solid.Length < n) solid = new byte[n];
            for (int i = 0; i < n; i++) solid[i] = DestructibleTerrain.IsSolidPixel(pixels[i]) ? (byte)1 : (byte)0;

            // ---- 2) Trozos de borde dirigidos ----
            // Las esquinas de cada celda son CENTROS de píxel. Las celdas van de
            // -1 a w-1 / h-1 para cerrar también contra el borde de la imagen
            // (fuera de la imagen = aire).
            int hCount = (w + 1) * (h + 2);          // aristas horizontales
            int vCount = (w + 2) * (h + 1);          // aristas verticales
            int total = hCount + vCount;
            if (next == null || next.Length < total)
            {
                next = new int[total];
                for (int i = 0; i < total; i++) next[i] = -1;
            }
            starts.Clear();

            for (int j = -1; j < h; j++)
            {
                for (int i = -1; i < w; i++)
                {
                    int bl = S(i, j, w, h), br = S(i + 1, j, w, h);
                    int tr = S(i + 1, j + 1, w, h), tl = S(i, j + 1, w, h);
                    int c = bl | (br << 1) | (tr << 2) | (tl << 3);
                    if (c == 0 || c == 15) continue;

                    int B = (j + 1) * (w + 1) + (i + 1);              // arista inferior
                    int T = (j + 2) * (w + 1) + (i + 1);              // arista superior
                    int L = hCount + (j + 1) * (w + 2) + (i + 1);     // arista izquierda
                    int R = hCount + (j + 1) * (w + 2) + (i + 2);     // arista derecha

                    // Dirección: el sólido queda SIEMPRE a la izquierda del avance.
                    switch (c)
                    {
                        case 1:  Link(B, L); break;
                        case 2:  Link(R, B); break;
                        case 3:  Link(R, L); break;
                        case 4:  Link(T, R); break;
                        case 5:  Link(B, L); Link(T, R); break;   // silla: dos esquinas sueltas
                        case 6:  Link(T, B); break;
                        case 7:  Link(T, L); break;
                        case 8:  Link(L, T); break;
                        case 9:  Link(B, T); break;
                        case 10: Link(R, B); Link(L, T); break;   // silla
                        case 11: Link(R, T); break;
                        case 12: Link(L, R); break;
                        case 13: Link(B, R); break;
                        case 14: Link(L, B); break;
                    }
                }
            }

            // ---- 3) Encadenar en bucles ----
            float eps = Mathf.Max(0.01f, simplifyPx);
            foreach (int start in starts)
            {
                if (next[start] < 0) continue;          // ya recorrido
                loopPts.Clear();
                int cur = start;
                int guard = total;
                while (cur >= 0 && guard-- > 0)
                {
                    loopPts.Add(Position(cur, w, hCount));
                    int nx = next[cur];
                    next[cur] = -1;                      // visitado (y de paso, buffer limpio)
                    cur = nx;
                    if (cur == start) break;
                }
                if (loopPts.Count < minLoopPoints) continue;

                // ---- 4) Simplificar y pasar a unidades ----
                var simplified = SimplifyClosed(loopPts, eps);
                if (simplified.Count < 3) continue;
                var arr = new Vector2[simplified.Count + 1];
                for (int k = 0; k < simplified.Count; k++) arr[k] = simplified[k] / ppu;
                arr[simplified.Count] = arr[0];         // cerrado
                result.Add(arr);
            }
            return result;
        }

        private static int S(int i, int j, int w, int h) =>
            (i < 0 || j < 0 || i >= w || j >= h) ? 0 : solid[j * w + i];

        private static void Link(int from, int to)
        {
            next[from] = to;
            starts.Add(from);
        }

        /// <summary>Punto medio de una arista, en coordenadas de PÍXEL (0,0 = esquina inf-izq de la imagen).</summary>
        private static Vector2 Position(int id, int w, int hCount)
        {
            if (id < hCount)
            {
                int hj = id / (w + 1), hi = id % (w + 1);
                // Entre los centros de los píxeles (i,j) e (i+1,j) → (i+1, j+0.5)
                return new Vector2(hi, hj - 0.5f);
            }
            int k = id - hCount;
            int vj = k / (w + 2), vi = k % (w + 2);
            // Entre los centros (i,j) e (i,j+1) → (i+0.5, j+1)
            return new Vector2(vi - 0.5f, vj);
        }

        // ============================================================
        //  Simplificación (Ramer–Douglas–Peucker) para bucles cerrados
        // ============================================================
        private static List<Vector2> SimplifyClosed(List<Vector2> pts, float eps)
        {
            int count = pts.Count;
            // Partimos el bucle en dos por el punto más lejano al primero y
            // simplificamos cada mitad como una línea abierta.
            int far = 0;
            float best = -1f;
            for (int i = 1; i < count; i++)
            {
                float d = (pts[i] - pts[0]).sqrMagnitude;
                if (d > best) { best = d; far = i; }
            }
            var keep = new bool[count];
            keep[0] = keep[far] = true;
            Rdp(pts, 0, far, eps, keep);
            RdpWrap(pts, far, count, eps, keep);

            var outList = new List<Vector2>(64);
            for (int i = 0; i < count; i++) if (keep[i]) outList.Add(pts[i]);
            return outList;
        }

        private static readonly Stack<(int a, int b)> rdpStack = new();

        private static void Rdp(List<Vector2> p, int first, int last, float eps, bool[] keep)
        {
            rdpStack.Clear();
            rdpStack.Push((first, last));
            while (rdpStack.Count > 0)
            {
                var (a, b) = rdpStack.Pop();
                if (b - a < 2) continue;
                float maxD = 0f; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = SegDist(p[i], p[a], p[b]);
                    if (d > maxD) { maxD = d; idx = i; }
                }
                if (idx >= 0 && maxD > eps)
                {
                    keep[idx] = true;
                    rdpStack.Push((a, idx));
                    rdpStack.Push((idx, b));
                }
            }
        }

        /// <summary>Segunda mitad del bucle: de 'far' hasta el final y vuelta al índice 0.</summary>
        private static void RdpWrap(List<Vector2> p, int far, int count, float eps, bool[] keep)
        {
            // Copiamos la mitad a una lista temporal [far .. count-1, 0] y mapeamos índices.
            var tmp = new List<Vector2>(count - far + 1);
            for (int i = far; i < count; i++) tmp.Add(p[i]);
            tmp.Add(p[0]);
            var k2 = new bool[tmp.Count];
            Rdp(tmp, 0, tmp.Count - 1, eps, k2);
            for (int i = 1; i < tmp.Count - 1; i++) if (k2[i]) keep[far + i] = true;
        }

        private static float SegDist(Vector2 pt, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return (pt - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(pt - a, ab) / len2);
            return (pt - (a + ab * t)).magnitude;
        }
    }
}
