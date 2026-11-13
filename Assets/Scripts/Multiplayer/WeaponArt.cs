using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ARMAS en pixel-art de trinchera, dibujadas por código con el MISMO
    ///  estilo que las ratas (32 px por unidad, contorno oscuro, luz de
    ///  arriba-izquierda). Cada dibujo es una lista de filas de caracteres
    ///  (fila 0 = arriba) y una paleta común.
    ///
    ///  Todas miran a la DERECHA; el pivote es el punto por donde la rata las
    ///  agarra (así rotan desde la mano).
    /// </summary>
    public static class WeaponArt
    {
        private static readonly Dictionary<string, Sprite> cache = new();

        /// <summary>Bazuca de trinchera: tubo verde oliva con anillas de latón, mira y empuñadura.</summary>
        public static Sprite Bazooka() => Make("bazooka", new Vector2(8.5f, 2.5f), 32f,
            "..........o.............",
            ".oooooooooYooooooooooo..",
            "oHGGGGgGGGGGGGgGGGGGGHo.",
            "oGGGGGbGGGGGGGbGGGGGGGGo",
            "oggggggggggggggggggggggo",
            ".oooooowwoooooooooooooo.",
            "......owo...............",
            "......oo................");

        /// <summary>Punto de la boca del cañón en píxeles del dibujo (para alinear el firePoint).</summary>
        public const float BazookaMuzzlePx = 24f - 8.5f;

        /// <summary>Granada de palo (la "pasapurés"): cabeza de lata y mango de madera.</summary>
        public static Sprite StickGrenade() => Make("stickgrenade", new Vector2(8f, 2.5f), 32f,
            "..........ooooo",
            "ooooooooooHGGGo",
            "oWWWWWWWWobGGGo",
            "owwwwwwwwobgggo",
            "ooooooooooggggo",
            "..........ooooo");

        /// <summary>Escopeta recortada de dos cañones con culata de madera.</summary>
        public static Sprite Shotgun() => Make("shotgun", new Vector2(5.5f, 2.5f), 32f,
            ".oooo..............",
            "oWWWWoooooooooooooo",
            "oWwwwoMMMMMMMMMMMMo",
            "owwwwommmmmmmmmmmmo",
            ".ooowoooooooooooooo",
            "....oo.............");

        /// <summary>Bate con CLAVOS y sangre seca en la punta.</summary>
        public static Sprite Bat() => Make("bat", new Vector2(0.5f, 3f), 32f,
            "...............n...n....",
            "..........ooooonooonoo..",
            "ooooooooooWWWWWWWWWWRWo.",
            "oLLLLLLoowWWWWWWWWWRRWWo",
            "ooooooooowwwwwwwwwwwRwo.",
            "..........oooooonoooon..",
            "................n.......");

        /// <summary>Tres cartuchos de dinamita atados, con mecha.</summary>
        public static Sprite Dynamite() => Make("dynamite", new Vector2(5.5f, 1.5f), 32f,
            "......F....",
            ".....F.....",
            "....oFo....",
            ".ooooooooo.",
            "oRrRRrRRrRo",
            "oRrRRrRRrRo",
            "oYYYYYYYYYo",
            "oRrRRrRRrRo",
            "oRrRRrRRrRo",
            "oRrRRrRRrRo",
            "oYYYYYYYYYo",
            "oRrRRrRRrRo",
            "orrrrrrrrro",
            ".ooooooooo.");

        /// <summary>Altura de la punta de la mecha (px desde el pivote), para las chispas.</summary>
        public const float DynamiteFusePx = 12.5f;

        /// <summary>Proyectil de la bazuca: obús con punta roja y aletas.</summary>
        public static Sprite Missile() => Make("missile", new Vector2(7f, 3f), 32f,
            ".oo...........",
            "oSSoooooooooo.",
            ".oSGGGGGGbRRRo",
            ".oSgggggggbrrRo",
            ".oSgggggggbrro",
            "oSSoooooooooo.",
            ".oo...........");

        /// <summary>Icono del teletransporte: cacharro de latón con una válvula que chisporrotea.</summary>
        public static Sprite TeleportIcon() => Make("teleport", new Vector2(6f, 6f), 32f,
            "....oooo....",
            "...oCCCCo...",
            "..oCSSSSCo..",
            ".ooobbbbooo.",
            "obbbmmmmbbbo",
            "obMMMMMMMMbo",
            "obMSSMMSSMbo",
            "obmmmmmmmmbo",
            ".obbbbbbbbo.",
            "..oooooooo..",
            "...oR..Ro...",
            "....o..o....");

        /// <summary>Icono de la cuerda ninja: rollo de cuerda con gancho.</summary>
        public static Sprite RopeIcon() => Make("rope", new Vector2(6f, 6f), 32f,
            "........oMo.",
            ".......oMMo.",
            "..oooooMo.o.",
            ".oYYYYoMo...",
            "oYyyyyYo....",
            "oYyooyYo....",
            "oYyooyYo....",
            "oYyyyyYo....",
            ".oYYYYo.....",
            "..oooo......");

        /// <summary>Caja de armas: madera con flejes y una calavera pintada.</summary>
        public static Sprite WeaponCrate() => Make("crate", new Vector2(7f, 0.5f), 32f,
            "oooooooooooooo",
            "oMWWWWWWWWWWMo",
            "oWwYYwWWwYYwWo",
            "oWwYoYWWYoYwWo",
            "oWwYYYYYYYYwWo",
            "oMMMMYoYoYMMMo",
            "oWwwwYYYYYwwWo",
            "oWWwwWYYYWwwWo",
            "oWwWWWWWWWWwWo",
            "oMMMMMMMMMMMMo",
            "oWwwwwwwwwwwWo",
            "oooooooooooooo");

        /// <summary>Botiquín de campaña: caja blanca con cruz roja.</summary>
        public static Sprite HealthCrate() => Make("health", new Vector2(7f, 0.5f), 32f,
            "oooooooooooooo",
            "oCCCCCCCCCCCCo",
            "oCCCCCRRCCCCCo",
            "oCCCCCRRCCCCCo",
            "oCCCRRRRRRCCCo",
            "oCCCRRRRRRCCCo",
            "oCCCCCRRCCCCCo",
            "oCCCCCRRCCCCCo",
            "occccccccccccco",
            "oooooooooooooo");

        /// <summary>Paracaídas de lona remendada (cuelga de la caja mientras cae).</summary>
        public static Sprite Parachute() => Make("chute", new Vector2(10f, 0f), 32f,
            ".....oooooooooo.....",
            "...ooCCYYCCYYCCoo...",
            "..oCCCYYCCYYCCCCYo..",
            ".oYYCCCCYYCCCCYYCCo.",
            "oCCYYCCCCYYCCYYCCYYo",
            "ooooooooooooooooooo.",
            ".o....o......o....o.",
            "..o....o....o....o..",
            "...o....o..o....o...",
            "....o....oo....o....",
            ".....o...oo...o.....",
            "......o..oo..o......",
            ".......o.oo.o.......",
            "........oooo........");

        /// <summary>Bidón de gasolina oxidado con el símbolo de peligro.</summary>
        public static Sprite Barrel() => Make("barrel", new Vector2(6f, 0.5f), 32f,
            ".oooooooooo.",
            "oKkKKKKKKkKo",
            "oKkKKKKKKkKo",
            "omMMMMMMMMmo",
            "oKkKYYYYKkKo",
            "oKkKYoYYKkKo",
            "oKkYYoYYYkKo",
            "oKkKYYYYKkKo",
            "omMMMMMMMMmo",
            "oKkKKKKrKkKo",
            "oKkKKKrrKkKo",
            "omMMMMMMMMmo",
            "oKkKKKKKKkKo",
            ".oooooooooo.");

        // ============================================================
        private static Sprite Make(string key, Vector2 pivotPx, float ppu, params string[] rows)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            int h = rows.Length, w = 0;
            foreach (var r in rows) w = Mathf.Max(w, r.Length);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(h - 1 - y) * w + x] = Col(x < rows[y].Length ? rows[y][x] : '.');
            tex.SetPixels32(px);
            tex.Apply();
            // pivotPx: x desde la izquierda, y desde ABAJO
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(pivotPx.x / w, pivotPx.y / h), ppu);
            cache[key] = s;
            return s;
        }

        private static Color32 Col(char c) => c switch
        {
            'o' => new Color32(22, 18, 18, 255),      // contorno
            'G' => new Color32(98, 104, 62, 255),     // verde oliva
            'H' => new Color32(136, 142, 90, 255),    // oliva claro
            'g' => new Color32(66, 70, 42, 255),      // oliva oscuro
            'b' => new Color32(206, 166, 64, 255),    // latón
            'Y' => new Color32(222, 196, 120, 255),   // papel / cuerda
            'W' => new Color32(176, 122, 70, 255),    // madera
            'w' => new Color32(126, 84, 46, 255),     // madera oscura
            'L' => new Color32(60, 40, 30, 255),      // cinta del mango
            'M' => new Color32(128, 134, 144, 255),   // metal claro
            'm' => new Color32(72, 76, 86, 255),      // metal oscuro
            'n' => new Color32(170, 170, 176, 255),   // clavos
            'R' => new Color32(196, 40, 34, 255),     // rojo (dinamita, sangre, punta)
            'r' => new Color32(138, 24, 22, 255),     // rojo oscuro
            'F' => new Color32(90, 62, 34, 255),      // mecha
            'S' => new Color32(110, 112, 118, 255),   // aletas
            'C' => new Color32(226, 220, 204, 255),   // lona / blanco sucio
            'y' => new Color32(170, 140, 80, 255),    // cuerda oscura
            'c' => new Color32(180, 172, 156, 255),
            'K' => new Color32(168, 64, 36, 255),     // bidón oxidado
            'k' => new Color32(112, 40, 24, 255),
            _ => new Color32(0, 0, 0, 0),
        };
    }
}
