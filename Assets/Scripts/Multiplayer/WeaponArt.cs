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
        //  ARMAS NUEVAS (0.18)
        // ============================================================
        /// <summary>Lanzallamas de trinchera: depósito de gasolina a la espalda, manguera y lanza con piloto encendido.</summary>
        public static Sprite Flamethrower() => Make("flamer", new Vector2(6.5f, 2.5f), 32f,
            ".oooo....................",
            "oKKkKo..............ooo..",
            "oKKkKoooooooooooooooMMMoQ",
            "oKKkKommmmmmmmmmmmmmmmmoO",
            "oKKkKoowwoooooooooooooo..",
            "okkkko.owo...............",
            ".oooo..oo................");

        /// <summary>Metralleta de trinchera: culata de madera, cañón con camisa agujereada y cargador lateral.</summary>
        public static Sprite MachineGun() => Make("mgun", new Vector2(8.5f, 2.5f), 32f,
            "........oooo..............",
            "oooo...oommooooooooooooo..",
            "oWWWoooMMMMMMoMoMoMoMoMMoo",
            "owwwwWWmmmmmmomomomomommmo",
            ".ooooowooommmooooooooooo..",
            "......owo.omo.............",
            "......oo..omo.............",
            "..........oo..............");

        /// <summary>Bomba de racimo: lata gorda con franja roja y remaches; revienta en bombetas.</summary>
        public static Sprite ClusterBomb() => Make("cluster", new Vector2(5f, 4.5f), 32f,
            "...oooo...",
            "..oMMMMo..",
            ".oMSSMMMo.",
            "oMSMMMMmmo",
            "oRRRRRRRro",
            "oMMbMMbMmo",
            "omMMMMMmmo",
            ".ommmmmmo.",
            "..oooooo..");

        /// <summary>Bombeta (lo que suelta la bomba de racimo).</summary>
        public static Sprite Bomblet() => Make("bomblet", new Vector2(2.5f, 2.5f), 32f,
            ".ooo.",
            "oMSmo",
            "oRRro",
            "ommmo",
            ".ooo.");

        /// <summary>Biplano de reconocimiento que cruza el cielo en el ataque aéreo (mira a la DERECHA).</summary>
        public static Sprite Plane() => Make("plane", new Vector2(20f, 7f), 32f,
            "..........oooooooooooooooooo..........",
            ".........oHHHHHHHHHHHHHHHHHHo.........",
            "oo........oooooooooooooooooo..........",
            "oGo...........o......o...........ooo..",
            "oGGo..........o......o.........ooMMo..",
            "oGGGoooooooooooooooooooooooooooomMMo.o",
            "oGGGGGGGGGGGGGGGGGGGGGGGGGGGRRGGoMMoMo",
            "oggggggggggggggggggggggggggggggoomMmoM",
            ".oooooggggggggggggggggggggggggo.omMo.o",
            "......ooooooooooooooooooooooooo..ooo..",
            ".........oHHHHHHHHHHHHHHHHHHo.........",
            "..........oooooooooooooooooo..........",
            "...................oo.oo..............",
            "..................oMMoMMo.............",
            "...................oo.oo..............");

        /// <summary>Bomba de aviación (cae girada hacia donde va): cuerpo gris, aletas y punta.</summary>
        public static Sprite AirBomb() => Make("airbomb", new Vector2(6f, 3f), 32f,
            "oo..........",
            "oSoooooooo..",
            ".oSMMMMMMMoo",
            ".oSmmmmmmmmMo",
            ".oSmmmmmmmoo",
            "oSoooooooo..",
            "oo..........");

        /// <summary>Bomba de NAPALM: igual pero con bidón rojo y un trapo ardiendo.</summary>
        public static Sprite NapalmBomb() => Make("napalmbomb", new Vector2(6f, 3f), 32f,
            "oo.......Q..",
            "oSoooooooOo.",
            ".oSKKKKKKKoo",
            ".oSkkYYkkkkKo",
            ".oSkkkkkkkoo",
            "oSoooooooo..",
            "oo..........");

        /// <summary>Icono del napalm: bidón con llamas encima.</summary>
        public static Sprite NapalmIcon() => Make("napalmicon", new Vector2(6f, 0.5f), 32f,
            "...Q...Q....",
            "..QOQ.QOQ.Q.",
            "..OOOQOOOQO.",
            ".ooooooooooo",
            ".oKkKYYYKkKo",
            ".oKkYoYYKkKo",
            ".omMMMMMMMmo",
            ".oKkKKKKKkKo",
            ".oKkKKKKKkKo",
            ".ooooooooooo");

        /// <summary>Llama (fotograma 0-2) para el fuego del suelo: lengua naranja con corazón amarillo.</summary>
        public static Sprite Flame(int frame) => (frame % 3) switch
        {
            0 => Make("flame0", new Vector2(3f, 0f), 32f,
                "...O...",
                "..OO...",
                "..OQO..",
                ".OQQO..",
                ".OQQQO.",
                "OOQQQOO",
                ".OOOOO."),
            1 => Make("flame1", new Vector2(3f, 0f), 32f,
                "....O..",
                "...OO..",
                "..OQO..",
                "..OQQO.",
                ".OQQQO.",
                "OOQQQOO",
                ".OOOOO."),
            _ => Make("flame2", new Vector2(3f, 0f), 32f,
                "..O....",
                "..OO.O.",
                ".OQOOO.",
                ".OQQO..",
                ".OQQQO.",
                "OOQQQOO",
                ".OOOOO."),
        };

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
            'O' => new Color32(255, 132, 30, 255),    // llama naranja
            'Q' => new Color32(255, 222, 96, 255),    // llama amarilla (corazón)
            _ => new Color32(0, 0, 0, 0),
        };
    }
}
