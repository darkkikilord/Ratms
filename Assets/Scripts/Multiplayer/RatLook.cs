namespace Ratms.Networking
{
    /// <summary>
    ///  ASPECTO DE UNA RATA, por piezas. Todas siguen el mismo estilo
    ///  (pixel-art de trinchera, contorno oscuro, luz de arriba-izquierda),
    ///  pero cada una tiene su ANATOMÍA y su equipo de guerra.
    ///
    ///  0.24: muchas más piezas (10 cuerpos, 14 pelajes, 16 sombreros,
    ///  accesorios...) → cientos de millones de combinaciones. Se empaqueta en
    ///  un <c>ulong</c> (<see cref="Pack"/>) que viaja por red en
    ///  <see cref="NetworkPlayerMovement.LookCode"/>. Los códigos viejos de 32
    ///  bits (equipos guardados antes de 0.24) se siguen entendiendo.
    ///
    ///  Cada CUERPO tiene además sus ventajas (<see cref="Stats"/>): vida,
    ///  velocidad, salto, golpe y daño por caída.
    /// </summary>
    public struct RatLook
    {
        public enum BodyType : byte { Gorda, Flaca, Jorobada, Musculosa, Topo, Gigante, Enana, Salchicha, Cabezona, Erguida }
        public enum FurType : byte { Parda, Gris, Negra, Pelirroja, Albina, Sarnosa, Atigrada, Manchada, Zombi, Chamuscada, Dorada, Azulada, Rosa, Canosa }
        public enum TailType : byte { Normal, Larga, Munon, SinCola, Rizada, Bifurcada }
        public enum EarType : byte { Normal, Rota, Grande, Arrancada, Puntiaguda, Pendiente }
        public enum EyeType : byte { Normal, Parche, Loco, Monoculo, Gafas, Demonio, Cejijunta }
        public enum HatType : byte { Brodie, Adrian, Pickelhaube, Gorra, Panuelo, MascaraGas, Boina, Chistera, Bicornio, Minero, Corona, Vaquero, Fez, Vikingo, Lana, Ninguno }
        public enum MouthType : byte { Cigarro, Puro, Nada, Mellada, Pipa, Mostacho, Lengua, Espiga }
        public enum MarkType : byte { Cicatriz, Puntos, Venda, Ninguna, PinturaGuerra, Quemadura }
        public enum AccessoryType : byte { Ninguno, Medalla, Cartuchera, Mochila, Capa, Collar, Pajarita, Cantimplora }

        public const int Bodies = 10, Furs = 14, Tails = 6, Ears = 6, Eyes = 7, Hats = 16, Mouths = 8, Marks = 6, Accessories = 8;

        public BodyType body;
        public FurType fur;
        public TailType tail;
        public EarType ear;
        public EyeType eye;
        public HatType hat;
        public MouthType mouth;
        public MarkType mark;
        public AccessoryType acc;
        public bool oneArm;      // le falta el brazo de delante (muñón vendado)
        public bool pegLeg;      // pata de palo
        public bool goldTooth;
        public byte variant;     // posición de calvas, manchas de sangre...

        // ------------------------------------------------------------
        //  Empaquetado (bits): body 4 · fur 4 · tail 3 · ear 3 · eye 3 ·
        //  hat 5 · mouth 3 · mark 3 · acc 3 · brazo 1 · pata 1 · oro 1 ·
        //  variante 8 = 42 bits. El bit 63 a 1 marca "código nuevo" (0 = sin aspecto).
        // ------------------------------------------------------------
        private const ulong NewFlag = 1UL << 63;

        public ulong Pack()
        {
            ulong c = 0; int s = 0;
            void Put(uint v, int bits) { c |= ((ulong)v & ((1UL << bits) - 1)) << s; s += bits; }
            Put((uint)body, 4); Put((uint)fur, 4); Put((uint)tail, 3); Put((uint)ear, 3); Put((uint)eye, 3);
            Put((uint)hat, 5); Put((uint)mouth, 3); Put((uint)mark, 3); Put((uint)acc, 3);
            Put(oneArm ? 1u : 0u, 1); Put(pegLeg ? 1u : 0u, 1); Put(goldTooth ? 1u : 0u, 1); Put(variant, 8);
            return c | NewFlag;
        }

        public static RatLook Unpack(ulong c)
        {
            if ((c & NewFlag) == 0) return UnpackV1((uint)c);
            int s = 0;
            uint Get(int bits) { uint v = (uint)((c >> s) & ((1UL << bits) - 1)); s += bits; return v; }
            return new RatLook
            {
                body = (BodyType)(Get(4) % Bodies),
                fur = (FurType)(Get(4) % Furs),
                tail = (TailType)(Get(3) % Tails),
                ear = (EarType)(Get(3) % Ears),
                eye = (EyeType)(Get(3) % Eyes),
                hat = (HatType)(Get(5) % Hats),
                mouth = (MouthType)(Get(3) % Mouths),
                mark = (MarkType)(Get(3) % Marks),
                acc = (AccessoryType)(Get(3) % Accessories),
                oneArm = Get(1) == 1,
                pegLeg = Get(1) == 1,
                goldTooth = Get(1) == 1,
                variant = (byte)Get(8),
            };
        }

        /// <summary>Formato de antes de 0.24 (32 bits, bit 31 = válido).</summary>
        private static RatLook UnpackV1(uint c)
        {
            int s = 0;
            uint Get(int bits) { uint v = (c >> s) & ((1u << bits) - 1); s += bits; return v; }
            return new RatLook
            {
                body = (BodyType)(Get(3) % 5),
                fur = (FurType)(Get(3) % 6),
                tail = (TailType)Get(2),
                ear = (EarType)Get(2),
                eye = (EyeType)Get(2),
                hat = (HatType)(Get(3) % 6),
                mouth = (MouthType)Get(2),
                mark = (MarkType)Get(2),
                oneArm = Get(1) == 1,
                pegLeg = Get(1) == 1,
                goldTooth = Get(1) == 1,
                variant = (byte)Get(8),
            };
        }

        /// <summary>Rata al azar. <paramref name="forcedBody"/> ≥ 0 fija la anatomía (para variar dentro de un equipo).</summary>
        public static RatLook Random(System.Random r, int forcedBody = -1)
        {
            var l = new RatLook
            {
                body = forcedBody >= 0 ? (BodyType)(forcedBody % Bodies) : (BodyType)r.Next(Bodies),
                //                       Par Gri Neg Pel Alb Sar Ati Man Zom Cha Dor Azu Ros Can
                fur = (FurType)Pick(r, 16, 15, 11, 11, 6, 13, 9, 8, 5, 6, 3, 4, 3, 7),
                tail = (TailType)Pick(r, 36, 14, 18, 14, 11, 7),
                ear = (EarType)Pick(r, 34, 24, 15, 10, 9, 8),
                eye = (EyeType)Pick(r, 40, 15, 12, 9, 10, 6, 8),
                //                       Bro Adr Pic Gor Pañ Más Boi Chi Bic Min Cor Vaq Fez Vik Lan Nin
                hat = (HatType)Pick(r, 14, 9, 8, 9, 8, 8, 7, 5, 5, 6, 2, 5, 4, 4, 6, 5),
                mouth = (MouthType)Pick(r, 22, 12, 22, 14, 8, 10, 6, 6),
                mark = (MarkType)Pick(r, 24, 16, 14, 26, 12, 8),
                acc = (AccessoryType)Pick(r, 34, 10, 12, 8, 10, 8, 8, 10),
                oneArm = r.NextDouble() < 0.16,
                pegLeg = r.NextDouble() < 0.12,
                goldTooth = r.NextDouble() < 0.18,
                variant = (byte)r.Next(256),
            };
            return l;
        }

        private static int Pick(System.Random r, params int[] weights)
        {
            int total = 0;
            foreach (var w in weights) total += w;
            int x = r.Next(total);
            for (int i = 0; i < weights.Length; i++) { if (x < weights[i]) return i; x -= weights[i]; }
            return 0;
        }

        // ============================================================
        //  VENTAJAS DEL CUERPO (0.24)
        // ============================================================
        public struct BodyStats
        {
            public int hp;          // vida extra (o menos)
            public float speed;     // ×velocidad al andar
            public float jump;      // ×fuerza del salto
            public float melee;     // ×daño cuerpo a cuerpo (bate, bayoneta, mordisco)
            public float fall;      // ×daño por caída
            public float knock;     // ×empujón que RECIBE (las pesadas vuelan menos)
            public string summary;  // para "Mi equipo"
            // 0.30: TAMAÑO. hitW/hitH = escala del colisionador (ancho/alto):
            // la gigante es un blanco enorme y la enana uno chiquitito. visual =
            // escala del dibujo (para que se note a simple vista).
            public float hitW, hitH, visual;
            /// <summary>Escala media del blanco: las explosiones "llegan" más lejos a las grandes.</summary>
            public float HitScale => (hitW + hitH) * 0.5f;
        }

        public BodyStats Stats => StatsOf(body);

        public static BodyStats StatsOf(BodyType b) => b switch
        {
            BodyType.Gorda => new BodyStats { hitW = 1.15f, hitH = 1.0f, visual = 1.0f, hp = 15, speed = 0.92f, jump = 0.95f, melee = 1f, fall = 1f, knock = 0.85f, summary = "+15 vida · lenta · cuesta empujarla" },
            BodyType.Flaca => new BodyStats { hitW = 0.85f, hitH = 1.05f, visual = 1.0f, hp = -10, speed = 1.12f, jump = 1.12f, melee = 0.9f, fall = 0.9f, knock = 1.1f, summary = "-10 vida · rápida y saltarina" },
            BodyType.Jorobada => new BodyStats { hitW = 1.05f, hitH = 0.95f, visual = 1.0f, hp = 5, speed = 0.95f, jump = 0.95f, melee = 1f, fall = 0.6f, knock = 0.95f, summary = "+5 vida · casi no se hace daño al caer" },
            BodyType.Musculosa => new BodyStats { hitW = 1.1f, hitH = 1.05f, visual = 1.0f, hp = 5, speed = 1f, jump = 1f, melee = 1.5f, fall = 1f, knock = 0.9f, summary = "+5 vida · golpes cuerpo a cuerpo ×1,5" },
            BodyType.Topo => new BodyStats { hitW = 1.0f, hitH = 0.8f, visual = 1.0f, hp = 0, speed = 1.05f, jump = 0.9f, melee = 1.2f, fall = 0.8f, knock = 1f, summary = "mordisco fuerte · pequeña y difícil de acertar" },
            BodyType.Gigante => new BodyStats { hitW = 1.3f, hitH = 1.3f, visual = 1.18f, hp = 30, speed = 0.85f, jump = 0.88f, melee = 1.3f, fall = 1.25f, knock = 0.7f, summary = "+30 vida · muy lenta · enorme blanco" },
            BodyType.Enana => new BodyStats { hitW = 0.75f, hitH = 0.75f, visual = 0.85f, hp = -20, speed = 1.18f, jump = 1.08f, melee = 0.8f, fall = 0.5f, knock = 1.25f, summary = "-20 vida · rapidísima · cae de pie" },
            BodyType.Salchicha => new BodyStats { hitW = 1.25f, hitH = 0.85f, visual = 1.0f, hp = 5, speed = 1.15f, jump = 0.85f, melee = 1f, fall = 1f, knock = 0.9f, summary = "+5 vida · corre mucho, salta poco" },
            BodyType.Cabezona => new BodyStats { hitW = 1.05f, hitH = 1.08f, visual = 1.0f, hp = 10, speed = 1f, jump = 1f, melee = 1.25f, fall = 1f, knock = 1f, summary = "+10 vida · golpes ×1,25" },
            BodyType.Erguida => new BodyStats { hitW = 0.9f, hitH = 1.15f, visual = 1.0f, hp = -5, speed = 1f, jump = 1.22f, melee = 1f, fall = 0.85f, knock = 1.05f, summary = "-5 vida · salta muchísimo" },
            _ => new BodyStats { hitW = 1f, hitH = 1f, visual = 1f, hp = 0, speed = 1f, jump = 1f, melee = 1f, fall = 1f, knock = 1f, summary = "" },
        };

        // ============================================================
        //  NOMBRES (editor "Mi equipo")
        // ============================================================
        /// <summary>
        ///  Descripción para el jugador (editor "Mi equipo"), en su idioma:
        ///  "gorda, parda, cola larga, casco Brodie...".
        /// </summary>
        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string>
            {
                Ratms.UI.Loc.T(BodyName(body)), Ratms.UI.Loc.T(FurName(fur)),
            };
            if (tail != TailType.Normal) parts.Add(Ratms.UI.Loc.T(TailName(tail)));
            if (oneArm) parts.Add(Ratms.UI.Loc.T("manca"));
            if (pegLeg) parts.Add(Ratms.UI.Loc.T("pata de palo"));
            if (eye != EyeType.Normal) parts.Add(Ratms.UI.Loc.T(EyeName(eye)));
            parts.Add(Ratms.UI.Loc.T(HatName(hat)));
            if (mouth != MouthType.Nada) parts.Add(Ratms.UI.Loc.T(MouthName(mouth)));
            if (acc != AccessoryType.Ninguno) parts.Add(Ratms.UI.Loc.T(AccName(acc)));
            if (goldTooth) parts.Add(Ratms.UI.Loc.T("diente de oro"));
            return string.Join(", ", parts);
        }

        public static string BodyName(BodyType b) => b switch
        {
            BodyType.Gorda => "gorda", BodyType.Flaca => "flaca", BodyType.Jorobada => "jorobada",
            BodyType.Musculosa => "musculosa", BodyType.Topo => "topo", BodyType.Gigante => "gigante",
            BodyType.Enana => "enana", BodyType.Salchicha => "salchicha", BodyType.Cabezona => "cabezona", _ => "erguida",
        };
        public static string FurName(FurType f) => f switch
        {
            FurType.Parda => "parda", FurType.Gris => "gris", FurType.Negra => "negra",
            FurType.Pelirroja => "pelirroja", FurType.Albina => "albina", FurType.Sarnosa => "sarnosa",
            FurType.Atigrada => "atigrada", FurType.Manchada => "manchada", FurType.Zombi => "zombi",
            FurType.Chamuscada => "chamuscada", FurType.Dorada => "dorada", FurType.Azulada => "azulada",
            FurType.Rosa => "rosa", _ => "canosa",
        };
        public static string TailName(TailType t) => t switch
        {
            TailType.Larga => "cola larga", TailType.Munon => "muñón de cola", TailType.SinCola => "sin cola",
            TailType.Rizada => "cola rizada", TailType.Bifurcada => "cola bífida", _ => "cola normal",
        };
        public static string EarName(EarType e) => e switch
        {
            EarType.Rota => "oreja rota", EarType.Grande => "orejona", EarType.Arrancada => "sin oreja",
            EarType.Puntiaguda => "orejas de punta", EarType.Pendiente => "pendiente", _ => "orejas normales",
        };
        public static string EyeName(EyeType e) => e switch
        {
            EyeType.Parche => "parche", EyeType.Loco => "ojo loco", EyeType.Monoculo => "monóculo",
            EyeType.Gafas => "gafas de aviador", EyeType.Demonio => "ojos de demonio", EyeType.Cejijunta => "cejijunta",
            _ => "ojos normales",
        };
        public static string HatName(HatType h) => h switch
        {
            HatType.Brodie => "casco Brodie", HatType.Adrian => "casco Adrian", HatType.Pickelhaube => "pickelhaube",
            HatType.Gorra => "gorra", HatType.Panuelo => "pañuelo", HatType.MascaraGas => "máscara de gas",
            HatType.Boina => "boina", HatType.Chistera => "chistera", HatType.Bicornio => "bicornio",
            HatType.Minero => "casco de minero", HatType.Corona => "corona", HatType.Vaquero => "sombrero vaquero",
            HatType.Fez => "fez", HatType.Vikingo => "casco vikingo", HatType.Lana => "gorro de lana", _ => "sin sombrero",
        };
        public static string MouthName(MouthType m) => m switch
        {
            MouthType.Cigarro => "cigarro", MouthType.Puro => "puro", MouthType.Mellada => "mellada",
            MouthType.Pipa => "pipa", MouthType.Mostacho => "mostacho", MouthType.Lengua => "lengua fuera",
            MouthType.Espiga => "espiga", _ => "boca normal",
        };
        public static string MarkName(MarkType m) => m switch
        {
            MarkType.Cicatriz => "cicatriz", MarkType.Puntos => "puntos", MarkType.Venda => "venda",
            MarkType.PinturaGuerra => "pintura de guerra", MarkType.Quemadura => "quemadura", _ => "sin marcas",
        };
        public static string AccName(AccessoryType a) => a switch
        {
            AccessoryType.Medalla => "medalla", AccessoryType.Cartuchera => "cartuchera", AccessoryType.Mochila => "mochila",
            AccessoryType.Capa => "capa", AccessoryType.Collar => "collar de pinchos", AccessoryType.Pajarita => "pajarita",
            AccessoryType.Cantimplora => "cantimplora", _ => "sin accesorio",
        };

        /// <summary>Descripción corta (para el informe de pruebas).</summary>
        public override string ToString()
        {
            string s = $"{body}, {fur}, {tail}, {ear}, {eye}, {hat}, {mouth}, {mark}, {acc}";
            if (oneArm) s += ", manca";
            if (pegLeg) s += ", pata de palo";
            if (goldTooth) s += ", diente de oro";
            return s;
        }
    }
}
