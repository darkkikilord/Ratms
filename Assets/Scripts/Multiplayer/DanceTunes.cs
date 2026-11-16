using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.39 — CANCIONES DEL BAILE: cuando una rata baila (emote "Baile") suena
    ///  un trocito de canción (~4,5 s) de un estilo AL AZAR y la música del
    ///  juego baja mientras tanto (<see cref="Music.Duck"/>).
    ///
    ///  Estilos (todo sintetizado aquí, sin archivos):
    ///   0 ELECTRÓNICA  128 bpm · bombo a negras, contratiempos de charles,
    ///                  bajo rodando, acordes "supersaw" y arpegio.
    ///   1 POP          112 bpm · bombo/caja, acordes de piano I–V–vi–IV y melodía.
    ///   2 K-POP        124 bpm · bombo sincopado, palmas, melodía pentatónica
    ///                  brillante con campanitas y bajo saltando octavas.
    ///   3 RAP          90 bpm boom-bap · bombo y caja PESADOS, charles con swing,
    ///                  808 grave y piano oscuro.
    ///   4 REGGAETÓN    96 bpm · ritmo dembow (bum… ch-bum-ch), bajo y "pluck".
    ///
    ///  El servidor manda una SEMILLA con el emote: todos los PCs oyen el mismo
    ///  estilo y la misma melodía.
    /// </summary>
    public static class DanceTunes
    {
        public enum Style { Electronica, Pop, KPop, Rap, Reggaeton, Trap, Phonk, Eurobeat, Rumba }
        public const int Styles = 9;
        private const int Rate = 22050;
        public const float Seconds = 5.5f;

        /// <summary>Nombre del estilo para el bocadillo.</summary>
        public static string StyleName(Style s) => s switch
        {
            Style.Electronica => "(EDM)", Style.Pop => "(pop)", Style.KPop => "(k-pop)", Style.Rap => "(rap)", Style.Reggaeton => "(reggaetón)",
            Style.Trap => "(trap)", Style.Phonk => "(phonk)", Style.Eurobeat => "(eurobeat)", _ => "(rumba)",
        };

        private static readonly Dictionary<int, AudioClip> cache = new();
        private static AudioSource source;

        public static Style StyleOf(int seed) => (Style)(Mathf.Abs(seed) % Styles);

        /// <summary>Toca la canción de esta semilla (en este PC) y baja la música del juego.</summary>
        public static void Play(int seed, ulong singerLook = 0)
        {
            if (Application.isBatchMode) return;
            float v = PlayerPrefs.GetFloat("opt_master", 0.8f) * PlayerPrefs.GetFloat("opt_music", 0.8f);
            if (v <= 0.001f) return;
            int key = seed ^ (int)(singerLook * 31UL);
            if (!cache.TryGetValue(key, out var clip) || clip == null)
            {
                var data = Render(seed, singerLook);
                clip = AudioClip.Create("baile_" + seed, data.Length, 1, Rate, false);
                clip.SetData(data, 0);
                if (cache.Count > 30) cache.Clear();
                cache[key] = clip;
            }
            if (source == null)
            {
                var go = new GameObject("CancionDeBaile");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(v * 1.1f);
            source.Play();
            Music.Duck(clip.length + 0.2f, 0.12f);
            Played++;
        }

        /// <summary>Para las pruebas.</summary>
        public static int Played;

        // ------------------------------------------------------------
        //  SÍNTESIS
        // ------------------------------------------------------------
        private static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        public static float[] Render(int seed, ulong singerLook = 0)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var style = StyleOf(seed);
            float bpm = style switch
            {
                Style.Electronica => 128f, Style.Pop => 112f, Style.KPop => 124f, Style.Rap => 90f, Style.Reggaeton => 96f,
                Style.Trap => 140f, Style.Phonk => 130f, Style.Eurobeat => 155f, _ => 104f,
            };
            if (singerLook == 0) singerLook = (ulong)(seed * 2654435761L) | 1UL;
            float step = 60f / bpm / 4f;   // semicorchea
            int steps = Mathf.CeilToInt(Seconds / step);
            var buf = new float[Mathf.CeilToInt(Seconds * Rate)];
            int root = 45 + rng.Next(6);   // La-Re graves, cambia cada vez

            // Progresiones (grados en semitonos desde la raíz) — una cada media parte de 8 semicorcheas
            int[][] prog = style switch
            {
                Style.Pop => new[] { new[] { 0, 4, 7 }, new[] { 7, 11, 14 }, new[] { 9, 12, 16 }, new[] { 5, 9, 12 } },      // I V vi IV (mayor)
                Style.KPop => new[] { new[] { 5, 9, 12 }, new[] { 7, 11, 14 }, new[] { 4, 7, 11 }, new[] { 9, 12, 16 } },     // IV V iii vi
                Style.Reggaeton => new[] { new[] { 0, 3, 7 }, new[] { 8, 12, 15 }, new[] { 3, 7, 10 }, new[] { 10, 14, 17 } }, // i VI III VII
                Style.Rap => new[] { new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 8, 12, 15 }, new[] { 7, 10, 14 } },
                Style.Trap or Style.Phonk => new[] { new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 1, 5, 8 }, new[] { 0, 3, 7 } },   // frigio: oscuro
                Style.Eurobeat => new[] { new[] { 0, 3, 7 }, new[] { 8, 12, 15 }, new[] { 10, 14, 17 }, new[] { 7, 11, 14 } },
                Style.Rumba => new[] { new[] { 0, 3, 7 }, new[] { 10, 14, 17 }, new[] { 8, 12, 15 }, new[] { 7, 11, 14 } },          // cadencia andaluza
                _ => new[] { new[] { 0, 3, 7 }, new[] { 8, 12, 15 }, new[] { 3, 7, 10 }, new[] { 10, 14, 17 } },
            };
            int[] scale = style == Style.Pop || style == Style.KPop ? new[] { 0, 2, 4, 7, 9, 12, 14, 16 } : new[] { 0, 3, 5, 7, 10, 12, 15, 17 };
            // Melodía de 16 pasos al azar (se repite), con silencios
            var mel = new int[16];
            for (int i = 0; i < 16; i++) mel[i] = rng.NextDouble() < (style == Style.Rap ? 0.75 : 0.35) ? -1 : scale[rng.Next(scale.Length)];

            for (int s = 0; s < steps; s++)
            {
                int bar16 = s % 16;
                float t = s * step;
                int chordIdx = (s / 8) % prog.Length;
                var chord = prog[chordIdx];
                bool swing = style == Style.Rap && s % 2 == 1;
                float tt = t + (swing ? step * 0.25f : 0f);

                bool buildUp = style == Style.Electronica && s < 16;   // EDM: el primer compás es la SUBIDA
                switch (style)
                {
                    case Style.Electronica when buildUp:
                    {
                        // Redoble que acelera (corcheas → semicorcheas → fusas) y siseo que crece
                        float up = s / 16f;
                        if (s < 8 ? s % 2 == 0 : true) Snare(buf, t, 0.25f + 0.45f * up);
                        if (s >= 12) Snare(buf, t + step * 0.5f, 0.3f + 0.4f * up);
                        Riser(buf, t, step, up);
                        if (s % 4 == 0) Supersaw(buf, t, step * 3.5f, chord, root + 12, 0.04f + 0.1f * up);
                        if (s == 15) Vocal(buf, t + step * 0.6f, singerLook, "¡hey!", 0.9f);   // ¡y DROP!
                        break;
                    }
                    case Style.Electronica:
                        if (s == 16) { Kick(buf, t, 1.3f, 0.4f); Supersaw(buf, t, step * 4f, chord, root, 0.2f); }   // el golpe del drop
                        if (bar16 % 4 == 0) Kick(buf, t, 1f, 0.28f);
                        if (bar16 % 8 == 4) Clap(buf, t, 0.55f);
                        if (bar16 % 4 == 2) Hat(buf, t, 0.35f, 0.09f);
                        else Hat(buf, t, 0.12f, 0.025f);
                        if (bar16 % 4 != 0) Bass(buf, t, step * 0.9f, Hz(root + chord[0] - 12), 0.45f, 0.35f);   // rodando entre bombos
                        if (bar16 % 4 == 2) Supersaw(buf, t, step * 1.6f, chord, root + 12, 0.16f);
                        if (s % 2 == 0) Pluck(buf, t, step * 1.5f, Hz(root + 24 + chord[(s / 2) % 3]), 0.12f, square: false);
                        break;
                    case Style.Pop:
                        if (bar16 == 0 || bar16 == 8 || bar16 == 10) Kick(buf, t, 0.9f, 0.25f);
                        if (bar16 == 4 || bar16 == 12) Snare(buf, t, 0.6f);
                        if (s % 2 == 0) Hat(buf, t, 0.13f, 0.03f);
                        if (s % 8 == 0) Piano(buf, t, step * 7.5f, chord, root + 12, 0.22f);
                        if (s % 4 == 0) Bass(buf, t, step * 3.5f, Hz(root + chord[0] - 12), 0.45f, 0.2f);
                        if (mel[bar16] >= 0) Pluck(buf, t, step * 2f, Hz(root + 24 + mel[bar16]), 0.17f, square: true);
                        if (bar16 == 12) Vocal(buf, t, singerLook, "oh, oh, oh", 0.45f);
                        if (bar16 == 4 || bar16 == 12) Clap(buf, t, 0.3f);
                        break;
                    case Style.KPop:
                        if (bar16 == 0 || bar16 == 3 || bar16 == 6 || bar16 == 8 || bar16 == 11) Kick(buf, t, 0.95f, 0.24f);
                        if (bar16 == 4 || bar16 == 12) { Clap(buf, t, 0.6f); Snare(buf, t, 0.3f); }
                        Hat(buf, t, s % 2 == 0 ? 0.14f : 0.07f, 0.025f);
                        if (s % 2 == 0) Bass(buf, t, step * 1.6f, Hz(root + chord[0] - 12 + (s % 4 == 2 ? 12 : 0)), 0.42f, 0.4f);
                        if (mel[bar16] >= 0 || s % 4 == 0)
                        {
                            int n = mel[bar16] >= 0 ? mel[bar16] : scale[(s / 4) % scale.Length];
                            Pluck(buf, t, step * 1.4f, Hz(root + 24 + n), 0.15f, square: true);
                            Bell(buf, t, Hz(root + 36 + n), 0.05f);
                        }
                        if (s % 8 == 0) Supersaw(buf, t, step * 7f, chord, root + 12, 0.07f);
                        if (bar16 == 12) Vocal(buf, t, singerLook, "¡hey!", 0.75f);        // el "¡hey!" coreado
                        if (s == 18) Vocal(buf, t, singerLook, "la la la la", 0.45f);
                        break;
                    case Style.Rap:
                        if (bar16 == 0 || bar16 == 7 || bar16 == 10) Kick(buf, tt, 1.15f, 0.5f, sub: true);
                        if (bar16 == 4 || bar16 == 12) Snare(buf, t, 0.85f, heavy: true);
                        if (s % 2 == 0 || rng.NextDouble() < 0.25) Hat(buf, tt, 0.13f, 0.03f);
                        if (s % 8 == 0) Piano(buf, t, step * 6f, chord, root + 12, 0.2f, dark: true);
                        if (bar16 == 0 || bar16 == 10) Bass(buf, tt, step * 5f, Hz(root + chord[0] - 12), 0.6f, 0.05f);   // 808
                        if (mel[bar16] >= 0) Pluck(buf, t, step * 1.2f, Hz(root + 12 + mel[bar16]), 0.08f, square: false);
                        if (bar16 == 14) Vocal(buf, t, singerLook, s < 16 ? "¡yeah!" : "¡uh!", 0.55f);
                        Crackle(buf, t, step);   // vinilo
                        break;
                    case Style.Trap:
                        // Medio tiempo: caja en la parte 3; 808 que RESBALA entre notas; charles en ráfagas
                        if (bar16 == 0 || bar16 == 6 || bar16 == 11) Kick(buf, t, 1.1f, 0.45f, sub: true);
                        if (bar16 == 8) { Snare(buf, t, 0.8f, heavy: true); Clap(buf, t, 0.4f); }
                        if (bar16 == 0) Slide808(buf, t, step * 6f, Hz(root + chord[0] - 12), Hz(root + chord[0] - 12 + (rng.Next(2) == 0 ? 5 : 7)), 0.7f);
                        if (bar16 == 10) Slide808(buf, t, step * 5f, Hz(root - 12 + 3), Hz(root - 12), 0.6f);
                        if (rng.NextDouble() < 0.25) { for (int r = 0; r < 4; r++) Hat(buf, t + r * step / 4f, 0.12f, 0.015f); }   // ráfaga
                        else if (s % 2 == 0) Hat(buf, t, 0.13f, 0.025f);
                        if (s % 8 == 0) Bell(buf, t, Hz(root + 24 + chord[(s / 8) % 3]), 0.06f);
                        if (s == 8 || s == 40) Vocal(buf, t, singerLook, "¡yah!", 0.6f);
                        break;
                    case Style.Phonk:
                        // Cencerro con melodía, 808 saturado y charles "memphis"
                        if (bar16 % 4 == 0 || bar16 == 10) Kick(buf, t, 1.1f, 0.35f, sub: true);
                        if (bar16 == 4 || bar16 == 12) { Snare(buf, t, 0.6f); Clap(buf, t, 0.5f); }
                        Hat(buf, t, s % 2 == 0 ? 0.12f : 0.05f, 0.02f);
                        if (bar16 % 2 == 0 && mel[bar16] >= 0 || bar16 % 4 == 0)
                            Cowbell(buf, t, Hz(root + 24 + (mel[bar16] >= 0 ? mel[bar16] : chord[0])), 0.22f);
                        if (bar16 == 0 || bar16 == 8) Slide808(buf, t, step * 7f, Hz(root + chord[0] - 12), Hz(root + chord[0] - 12), 0.75f, distort: true);
                        break;
                    case Style.Eurobeat:
                        // A todo trapo: bombo a negras, bajo en contratiempo por octavas, arpegio de sierra
                        if (bar16 % 4 == 0) Kick(buf, t, 1f, 0.22f);
                        if (bar16 % 8 == 4) Snare(buf, t, 0.55f);
                        if (bar16 % 4 == 2) { Hat(buf, t, 0.25f, 0.06f); Bass(buf, t, step * 1.8f, Hz(root + chord[0] - 12 + (s % 8 == 2 ? 0 : 12)), 0.55f, 0.1f); }
                        Pluck(buf, t, step * 1.1f, Hz(root + 24 + chord[s % 3] + (s % 6 >= 3 ? 12 : 0)), 0.13f, square: false);
                        if (s % 16 == 0) Supersaw(buf, t, step * 8f, chord, root + 12, 0.08f);
                        if (s == 0 || s == 32) Vocal(buf, t, singerLook, "¡yeah!", 0.6f);
                        break;
                    case Style.Rumba:
                        // Palmas a contratiempo, cajón y rasgueo de guitarra (cadencia andaluza)
                        if (bar16 == 0 || bar16 == 6 || bar16 == 8) Kick(buf, t, 0.7f, 0.18f);
                        if (bar16 == 4 || bar16 == 12) Snare(buf, t, 0.35f);
                        if (bar16 % 4 == 2 || bar16 % 4 == 3) Clap(buf, t, 0.45f);
                        if (bar16 % 4 == 0 || bar16 % 4 == 3) Strum(buf, t, step * 2.5f, chord, root + 12, 0.2f, down: bar16 % 4 == 0);
                        if (s == 0) Vocal(buf, t + step, singerLook, "¡olé!", 0.75f);
                        if (s == 30) Vocal(buf, t, singerLook, "¡ay, ay, ay!", 0.6f);
                        break;
                    default: // Reggaetón (dembow): bombo en cada parte, caja en 3, 6, 11 y 14
                        if (s == 0) Vocal(buf, t, singerLook, "¡dale!", 0.7f);
                        if (bar16 % 4 == 0) Kick(buf, t, 1f, 0.26f);
                        if (bar16 == 3 || bar16 == 6 || bar16 == 11 || bar16 == 14) Snare(buf, t, 0.55f);
                        if (s % 2 == 0) Hat(buf, t, 0.1f, 0.03f);
                        if (bar16 % 4 == 0 || bar16 == 3 || bar16 == 11) Bass(buf, t, step * 1.8f, Hz(root + chord[0] - 12), 0.5f, 0.25f);
                        if (bar16 == 2 || bar16 == 5 || bar16 == 8 || bar16 == 10 || bar16 == 13)
                            Pluck(buf, t, step * 1.6f, Hz(root + 24 + chord[bar16 % 3]), 0.16f, square: false);
                        break;
                }
            }

            // Entrada/salida suaves, un poco de saturación y normalizado
            int fin = (int)(0.02f * Rate), fout = (int)(0.4f * Rate);
            float peak = 0.0001f;
            for (int i = 0; i < buf.Length; i++)
            {
                float g = Mathf.Min(1f, i / (float)fin) * Mathf.Min(1f, (buf.Length - i) / (float)fout);
                buf[i] = (float)System.Math.Tanh(buf[i] * 1.4f) * g;
                peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            }
            float k = 0.8f / peak;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
            return buf;
        }

        // ------------------------------------------------------------
        //  INSTRUMENTOS (suman en buf desde el instante t)
        // ------------------------------------------------------------
        private static uint noiseState = 22222;
        private static float Noise() { noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5; return (noiseState / (float)uint.MaxValue) * 2f - 1f; }

        private static void Kick(float[] b, float t, float gain, float decay, bool sub = false)
        {
            int s0 = (int)(t * Rate), n = (int)((decay + 0.05f) * Rate);
            float ph = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                float f = (sub ? 42f : 50f) + 110f * Mathf.Exp(-x * 28f);
                ph += f / Rate;
                float env = Mathf.Exp(-x / decay);
                float click = i < 60 ? Noise() * (1f - i / 60f) * 0.3f : 0f;
                b[s0 + i] += (Mathf.Sin(ph * 6.2832f) * env + click) * gain;
            }
        }

        private static void Snare(float[] b, float t, float gain, bool heavy = false)
        {
            int s0 = (int)(t * Rate), n = (int)((heavy ? 0.25f : 0.16f) * Rate);
            float lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                float env = Mathf.Exp(-x * (heavy ? 14f : 22f));
                lp += (Noise() - lp) * 0.55f;
                float tone = Mathf.Sin(6.2832f * (heavy ? 180f : 210f) * x) * Mathf.Exp(-x * 40f);
                b[s0 + i] += (lp * 0.8f + tone * 0.5f) * env * gain;
            }
        }

        private static void Clap(float[] b, float t, float gain)
        {
            for (int k = 0; k < 3; k++) Hat(b, t + k * 0.011f, gain * 0.7f, 0.05f, lowCut: false);
            Hat(b, t + 0.033f, gain, 0.12f, lowCut: false);
        }

        private static void Hat(float[] b, float t, float gain, float decay, bool lowCut = true)
        {
            int s0 = (int)(t * Rate), n = (int)((decay * 4f) * Rate);
            float prev = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                float nz = Noise();
                float hp = lowCut ? nz - prev : nz;   // quita graves: suena a metal
                prev = nz;
                b[s0 + i] += hp * Mathf.Exp(-x / decay) * gain * 0.32f;   // discretos: que mande el bombo
            }
        }

        private static void Bass(float[] b, float t, float len, float f, float gain, float pump)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                ph += f / Rate; ph -= Mathf.Floor(ph);
                float saw = 2f * ph - 1f;
                lp += (saw - lp) * 0.12f;
                float env = Mathf.Min(1f, x * 200f) * Mathf.Min(1f, (len - x) * 40f) * (1f - pump * Mathf.Exp(-x * 20f));
                b[s0 + i] += (lp * 0.7f + Mathf.Sin(ph * 6.2832f) * 0.5f) * env * gain;
            }
        }

        private static void Pluck(float[] b, float t, float len, float f, float gain, bool square)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                ph += f / Rate; ph -= Mathf.Floor(ph);
                float w = square ? (ph < 0.5f ? 1f : -1f) : 2f * ph - 1f;
                float cut = 0.05f + 0.6f * Mathf.Exp(-x * 18f);   // brillante al principio, se cierra
                lp += (w - lp) * cut;
                b[s0 + i] += lp * Mathf.Exp(-x * 7f) * Mathf.Min(1f, x * 400f) * gain;
            }
        }

        // ---- 0.40: instrumentos nuevos ----
        /// <summary>Siseo que crece (subida del EDM).</summary>
        private static void Riser(float[] b, float t, float len, float k)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            float lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float kk = k + (i / (float)n) / 16f;
                lp += (Noise() - lp) * (0.05f + 0.6f * kk);   // se abre el filtro
                b[s0 + i] += lp * kk * kk * 0.35f;
            }
        }

        /// <summary>808 con deslizamiento de tono (trap / phonk).</summary>
        private static void Slide808(float[] b, float t, float len, float f0, float f1, float gain, bool distort = false)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            float ph = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate, k = i / (float)n;
                float f = Mathf.Lerp(f0, f1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.5f) * 3f)));
                ph += f / Rate;
                float v = Mathf.Sin(ph * 6.2832f) * Mathf.Min(1f, x * 300f) * Mathf.Exp(-x * 1.4f);
                if (distort) v = (float)System.Math.Tanh(v * 3.5f) * 0.6f;
                b[s0 + i] += v * gain;
            }
        }

        /// <summary>Cencerro (phonk): dos ondas cuadradas desafinadas y metálicas, corto.</summary>
        private static void Cowbell(float[] b, float t, float f, float gain)
        {
            int s0 = (int)(t * Rate), n = (int)(0.22f * Rate);
            float p1 = 0f, p2 = 0f, lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                p1 += f / Rate; p1 -= Mathf.Floor(p1);
                p2 += f * 1.48f / Rate; p2 -= Mathf.Floor(p2);
                float sq = (p1 < 0.5f ? 1f : -1f) + (p2 < 0.5f ? 1f : -1f);
                lp += (sq - lp) * 0.4f;
                b[s0 + i] += (sq - lp) * Mathf.Exp(-x * 14f) * gain * 0.5f;
            }
        }

        /// <summary>Rasgueo de guitarra: las notas del acorde entran escalonadas (abajo o arriba).</summary>
        private static void Strum(float[] b, float t, float len, int[] chord, int baseMidi, float gain, bool down)
        {
            int[] notes = { chord[0], chord[1], chord[2], chord[0] + 12, chord[1] + 12 };
            for (int i = 0; i < notes.Length; i++)
            {
                int idx = down ? i : notes.Length - 1 - i;
                float f = Hz(baseMidi + notes[idx]);
                int s0 = (int)((t + i * 0.012f) * Rate), n = (int)(len * Rate);
                float ph = 0f, lp = 0f;
                for (int j = 0; j < n && s0 + j < b.Length; j++)
                {
                    float x = j / (float)Rate;
                    ph += f / Rate; ph -= Mathf.Floor(ph);
                    float w = 2f * ph - 1f;
                    lp += (w - lp) * (0.08f + 0.5f * Mathf.Exp(-x * 25f));
                    b[s0 + j] += lp * Mathf.Exp(-x * 4.5f) * gain * 0.35f;
                }
            }
        }

        /// <summary>Crujido de vinilo (rap).</summary>
        private static void Crackle(float[] b, float t, float len)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
                if (Noise() > 0.995f) b[s0 + i] += Noise() * 0.25f;
        }

        /// <summary>La RATA canta un coro ("¡hey!", "oh-oh"...) con su propia voz.</summary>
        private static void Vocal(float[] b, float t, ulong look, string text, float gain)
        {
            var v = RatVoice.RenderText(look, text);
            int s0 = (int)(t * Rate);
            for (int i = 0; i < v.Length && s0 + i < b.Length; i++) b[s0 + i] += v[i] * gain;
        }

        private static void Bell(float[] b, float t, float f, float gain)
        {
            int s0 = (int)(t * Rate), n = (int)(0.6f * Rate);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                float m = Mathf.Sin(6.2832f * f * 3.5f * x) * 2f * Mathf.Exp(-x * 8f);   // FM: campanita
                b[s0 + i] += Mathf.Sin(6.2832f * f * x + m) * Mathf.Exp(-x * 6f) * gain;
            }
        }

        private static void Supersaw(float[] b, float t, float len, int[] chord, int baseMidi, float gain)
        {
            foreach (int st in chord)
                foreach (float det in new[] { -0.012f, 0f, 0.013f })
                    SawNote(b, t, len, Hz(baseMidi + st) * (1f + det), gain * 0.33f);
        }

        private static void SawNote(float[] b, float t, float len, float f, float gain)
        {
            int s0 = (int)(t * Rate), n = (int)(len * Rate);
            float ph = (float)(f * 0.37 % 1.0), lp = 0f;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float x = i / (float)Rate;
                ph += f / Rate; ph -= Mathf.Floor(ph);
                lp += ((2f * ph - 1f) - lp) * 0.35f;
                float env = Mathf.Min(1f, x * 300f) * Mathf.Min(1f, (len - x) * 30f);
                b[s0 + i] += lp * env * gain;
            }
        }

        private static void Piano(float[] b, float t, float len, int[] chord, int baseMidi, float gain, bool dark = false)
        {
            foreach (int st in chord)
            {
                float f = Hz(baseMidi + st);
                int s0 = (int)(t * Rate), n = (int)(len * Rate);
                for (int i = 0; i < n && s0 + i < b.Length; i++)
                {
                    float x = i / (float)Rate;
                    float env = Mathf.Exp(-x * (dark ? 2.2f : 3f)) * Mathf.Min(1f, x * 500f);
                    float tone = Mathf.Sin(6.2832f * f * x) + 0.4f * Mathf.Sin(6.2832f * f * 2f * x) * Mathf.Exp(-x * 6f)
                                 + (dark ? 0f : 0.2f * Mathf.Sin(6.2832f * f * 3f * x) * Mathf.Exp(-x * 9f));
                    b[s0 + i] += tone * env * gain * 0.4f;
                }
            }
        }
    }
}
