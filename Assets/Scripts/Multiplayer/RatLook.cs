namespace Ratms.Networking
{
    /// <summary>
    ///  ASPECTO DE UNA RATA, por piezas. Todas siguen el mismo estilo
    ///  (pixel-art de trinchera, contorno oscuro, luz de arriba-izquierda),
    ///  pero cada una tiene su ANATOMÍA y su equipo de guerra:
    ///
    ///   · Cuerpo: gorda, flaca, jorobada, musculosa o rata topo (sin pelo).
    ///   · Pelaje, cola (normal, larga, muñón vendado, sin cola), brazo
    ///     amputado, pata de palo, orejas, ojo (parche, loco, monóculo),
    ///     casco (Brodie, Adrian, Pickelhaube, gorra, pañuelo, máscara de gas),
    ///     boca (cigarro, puro...), dientes y marcas.
    ///
    ///  Se empaqueta en un <c>uint</c> (<see cref="Pack"/>) que viaja por red
    ///  en <see cref="NetworkPlayerMovement.LookCode"/>: así TODOS los
    ///  clientes dibujan la misma rata. En el futuro el editor de cosméticos
    ///  solo tendrá que rellenar estos campos a mano en vez de al azar.
    /// </summary>
    public struct RatLook
    {
        public enum BodyType : byte { Gorda, Flaca, Jorobada, Musculosa, Topo }
        public enum FurType : byte { Parda, Gris, Negra, Pelirroja, Albina, Sarnosa }
        public enum TailType : byte { Normal, Larga, Munon, SinCola }
        public enum EarType : byte { Normal, Rota, Grande, Arrancada }
        public enum EyeType : byte { Normal, Parche, Loco, Monoculo }
        public enum HatType : byte { Brodie, Adrian, Pickelhaube, Gorra, Panuelo, MascaraGas }
        public enum MouthType : byte { Cigarro, Puro, Nada, Mellada }
        public enum MarkType : byte { Cicatriz, Puntos, Venda, Ninguna }

        public BodyType body;
        public FurType fur;
        public TailType tail;
        public EarType ear;
        public EyeType eye;
        public HatType hat;
        public MouthType mouth;
        public MarkType mark;
        public bool oneArm;      // le falta el brazo de delante (muñón vendado)
        public bool pegLeg;      // pata de palo
        public bool goldTooth;
        public byte variant;     // posición de calvas, manchas de sangre...

        // ------------------------------------------------------------
        //  Empaquetado (bits): body 3 · fur 3 · tail 2 · ear 2 · eye 2 ·
        //  hat 3 · mouth 2 · mark 2 · brazo 1 · pata 1 · oro 1 · variante 8
        //  = 30 bits. El bit 31 a 1 marca "código válido" (0 = sin aspecto).
        // ------------------------------------------------------------
        public uint Pack()
        {
            uint c = 0; int s = 0;
            void Put(uint v, int bits) { c |= (v & ((1u << bits) - 1)) << s; s += bits; }
            Put((uint)body, 3); Put((uint)fur, 3); Put((uint)tail, 2); Put((uint)ear, 2); Put((uint)eye, 2);
            Put((uint)hat, 3); Put((uint)mouth, 2); Put((uint)mark, 2);
            Put(oneArm ? 1u : 0u, 1); Put(pegLeg ? 1u : 0u, 1); Put(goldTooth ? 1u : 0u, 1); Put(variant, 8);
            return c | 0x80000000u;
        }

        public static RatLook Unpack(uint c)
        {
            int s = 0;
            uint Get(int bits) { uint v = (c >> s) & ((1u << bits) - 1); s += bits; return v; }
            var l = new RatLook
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
            return l;
        }

        /// <summary>Rata al azar. <paramref name="forcedBody"/> ≥ 0 fija la anatomía (para variar dentro de un equipo).</summary>
        public static RatLook Random(System.Random r, int forcedBody = -1)
        {
            var l = new RatLook
            {
                body = forcedBody >= 0 ? (BodyType)(forcedBody % 5) : (BodyType)r.Next(5),
                fur = (FurType)Pick(r, 22, 20, 14, 14, 8, 22),
                tail = (TailType)Pick(r, 45, 15, 22, 18),
                ear = (EarType)Pick(r, 40, 30, 18, 12),
                eye = (EyeType)Pick(r, 50, 20, 18, 12),
                hat = (HatType)Pick(r, 26, 16, 14, 16, 14, 14),
                mouth = (MouthType)Pick(r, 35, 18, 27, 20),
                mark = (MarkType)Pick(r, 30, 22, 20, 28),
                oneArm = r.NextDouble() < 0.18,
                pegLeg = r.NextDouble() < 0.14,
                goldTooth = r.NextDouble() < 0.2,
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

        /// <summary>Descripción corta (para el informe de pruebas y, más adelante, el editor).</summary>
        public override string ToString()
        {
            string s = $"{body}, {fur}";
            if (tail == TailType.SinCola) s += ", sin cola"; else if (tail == TailType.Munon) s += ", muñón de cola"; else if (tail == TailType.Larga) s += ", cola larga";
            if (oneArm) s += ", manca";
            if (pegLeg) s += ", pata de palo";
            if (ear != EarType.Normal) s += $", oreja {ear}";
            if (eye != EyeType.Normal) s += $", ojo {eye}";
            s += $", {hat}";
            if (mouth != MouthType.Nada) s += $", {mouth}";
            if (goldTooth) s += ", diente de oro";
            return s;
        }
    }
}
