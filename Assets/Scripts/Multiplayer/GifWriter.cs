using System.Collections.Generic;
using System.IO;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.34 — Escribe un GIF animado (para guardar las repeticiones y pasarlas
    ///  por WhatsApp/Discord). Sin librerías: el formato GIF es antiguo y simple.
    ///
    ///   • PALETA fija de 256 colores: 6 niveles de rojo × 7 de verde × 6 de azul
    ///     (252) + 4 grises. Con TRAMADO ordenado (matriz de Bayer 4×4) los
    ///     degradados del cielo no salen a franjas.
    ///   • Compresión LZW (la del formato), con códigos de 9 a 12 bits.
    ///   • Bucle infinito (extensión NETSCAPE).
    ///
    ///  Se usa desde un hilo aparte (solo matemáticas y un archivo): no toca
    ///  nada de Unity.
    /// </summary>
    public sealed class GifWriter : System.IDisposable
    {
        private readonly BinaryWriter w;
        private readonly int width, height;
        private static readonly byte[] Palette = BuildPalette();
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public GifWriter(string path, int width, int height)
        {
            this.width = width;
            this.height = height;
            w = new BinaryWriter(File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0xF7);   // paleta global de 256 colores
            w.Write((byte)0);      // fondo
            w.Write((byte)0);      // proporción de píxel
            w.Write(Palette);
            // Bucle infinito
            w.Write(new byte[] { 0x21, 0xFF, 0x0B });
            w.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        private static byte[] BuildPalette()
        {
            var p = new byte[256 * 3];
            int i = 0;
            for (int r = 0; r < 6; r++)
                for (int g = 0; g < 7; g++)
                    for (int b = 0; b < 6; b++)
                    {
                        p[i++] = (byte)(r * 255 / 5);
                        p[i++] = (byte)(g * 255 / 6);
                        p[i++] = (byte)(b * 255 / 5);
                    }
            for (int k = 0; k < 4; k++)
            {
                byte v = (byte)(40 + k * 50);
                p[i++] = v; p[i++] = v; p[i++] = v;
            }
            return p;
        }

        /// <summary>
        ///  Añade un fotograma. <paramref name="rgba"/>: width×height×4 bytes,
        ///  fila de ARRIBA primero. <paramref name="delayCs"/>: centésimas de segundo.
        /// </summary>
        public void AddFrame(byte[] rgba, int delayCs)
        {
            // Control gráfico (retardo)
            w.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 });
            w.Write((ushort)delayCs);
            w.Write(new byte[] { 0x00, 0x00 });
            // Descriptor de imagen (sin paleta local)
            w.Write((byte)0x2C);
            w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)width); w.Write((ushort)height);
            w.Write((byte)0);

            var idx = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    float d = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f - 0.5f;   // −0.5..0.5
                    int r = Q(rgba[o], 5, d), g = Q(rgba[o + 1], 6, d), b = Q(rgba[o + 2], 5, d);
                    idx[y * width + x] = (byte)(r * 42 + g * 6 + b);
                }
            Lzw(idx);
        }

        /// <summary>Cuantiza un canal 0-255 a 0..levels con tramado.</summary>
        private static int Q(byte c, int levels, float dither)
        {
            float v = c / 255f * levels + dither * 0.9f;
            int q = (int)(v + 0.5f);
            return q < 0 ? 0 : q > levels ? levels : q;
        }

        // ------------------------------------------------------------
        //  LZW (códigos de longitud variable, como manda el formato)
        // ------------------------------------------------------------
        private void Lzw(byte[] pixels)
        {
            const int minCode = 8, clear = 256, end = 257;
            w.Write((byte)minCode);
            var outBytes = new List<byte>(pixels.Length / 2);
            int bitBuf = 0, bitCount = 0;
            int codeSize = minCode + 1;
            void Emit(int code)
            {
                bitBuf |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8) { outBytes.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCount -= 8; }
            }

            var dict = new Dictionary<int, int>(4096);
            int next = end + 1;
            Emit(clear);
            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int k = pixels[i];
                int key = (prefix << 8) | k;
                if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix);
                if (next < 4096)
                {
                    dict[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    Emit(clear);
                    dict.Clear();
                    next = end + 1;
                    codeSize = minCode + 1;
                }
                prefix = k;
            }
            Emit(prefix);
            Emit(end);
            if (bitCount > 0) outBytes.Add((byte)(bitBuf & 0xFF));

            // En bloques de hasta 255 bytes
            for (int i = 0; i < outBytes.Count; i += 255)
            {
                int n = System.Math.Min(255, outBytes.Count - i);
                w.Write((byte)n);
                for (int j = 0; j < n; j++) w.Write(outBytes[i + j]);
            }
            w.Write((byte)0);
        }

        public void Dispose()
        {
            w.Write((byte)0x3B);   // fin del archivo
            w.Dispose();
        }
    }
}
