using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Sprites de armas e indicadores GENERADOS POR CÓDIGO (cero assets).
    ///  Se crean una sola vez y se comparten (caché estática).
    /// </summary>
    public static class WeaponSprites
    {
        private static Sprite dynamite, ring, dot;

        /// <summary>
        ///  Cartucho de dinamita pixel-art: rojo con dos bandas de papel,
        ///  mecha marrón y chispa amarilla. 8×22 px → 0.1 × 0.275 u.
        ///  Pivot abajo (en la base del cartucho).
        /// </summary>
        public static Sprite Dynamite()
        {
            if (dynamite != null) return dynamite;
            // Manojo de 3 cartuchos atados (WeaponArt), a 58 px/u para que no abulte demasiado.
            var art = WeaponArt.Dynamite();
            dynamite = Sprite.Create(art.texture, art.rect, new Vector2(art.pivot.x / art.rect.width, art.pivot.y / art.rect.height), 58f);
            if (dynamite != null) return dynamite;
            const int W = 8, H = 22;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            var clear = new Color32(0, 0, 0, 0);
            var red = new Color32(200, 40, 35, 255);
            var redDark = new Color32(140, 25, 25, 255);
            var band = new Color32(235, 210, 150, 255);
            var fuse = new Color32(90, 60, 30, 255);
            var spark = new Color32(255, 230, 80, 255);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color32 c = clear;
                    if (y < 16)
                    {
                        c = (x == 0 || x == W - 1) ? redDark : red;
                        if (y == 3 || y == 12) c = band;
                    }
                    else if (x == 4 && y < 20) c = fuse;
                    else if ((x == 4 || x == 3 || x == 5) && y >= 20) c = spark;
                    px[y * W + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            dynamite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.2f), 80f);
            return dynamite;
        }

        /// <summary>Anillo blanco con borde oscuro, 1 u de diámetro (escálalo al radio que quieras).</summary>
        public static Sprite Ring()
        {
            if (ring != null) return ring;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    // Banda clara entre r-5 y r-2, borde oscuro fuera y dentro.
                    float outer = Mathf.Clamp01(r - d);                 // 1 dentro del círculo
                    float band = Mathf.Clamp01((d - (r - 7f)));        // 1 fuera del hueco
                    float a = outer * band;
                    float light = Mathf.Clamp01(1f - Mathf.Abs(d - (r - 4f)) / 1.6f);
                    float shade = Mathf.Lerp(0.15f, 1f, light);
                    tex.SetPixel(x, y, new Color(shade, shade, shade, a * Mathf.Lerp(0.55f, 1f, light)));
                }
            tex.Apply();
            ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return ring;
        }

        /// <summary>Punto redondo blanco con borde oscuro, 1 u de diámetro.</summary>
        public static Sprite Dot()
        {
            if (dot != null) return dot;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    float inner = Mathf.Clamp01((r - 4.5f - d) / 1.5f);
                    float s = Mathf.Lerp(0.12f, 1f, inner);
                    tex.SetPixel(x, y, new Color(s, s, s, a));
                }
            tex.Apply();
            dot = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return dot;
        }
    }
}
