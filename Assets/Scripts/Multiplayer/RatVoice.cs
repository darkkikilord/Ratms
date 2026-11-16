using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.33 — VOZ PROPIA DE CADA RATA (estilo "galimatías" de dibujos animados).
    ///
    ///  Antes todas las ratas usaban el mismo clip (<c>Sfx.Id.Voice</c>) más
    ///  agudo o más grave. Ahora cada una tiene una VOZ sintetizada a partir de
    ///  su aspecto (<see cref="RatLook"/>, que es igual en todos los PCs):
    ///   • TONO según el cuerpo (gigante grave, enana aguda) ± un poco al azar.
    ///   • TAMAÑO DE GARGANTA (formantes): cambia el "color" de las vocales.
    ///   • ASPEREZA (ruido), VIBRATO y VELOCIDAD al hablar.
    ///   • ONDA: más "zumbido" (diente de sierra) o más "pito" (pulso).
    ///
    ///  Cómo se sintetiza una sílaba: una onda a la frecuencia de la voz pasa por
    ///  dos RESONADORES (los formantes F1 y F2 de la vocal: a, e, i, o, u) — es
    ///  lo que hace la garganta de verdad — y delante va un chasquido de ruido
    ///  corto (la "consonante"). Varias sílabas con una entonación = una frase.
    ///
    ///  Clips por rata y tipo de frase (<see cref="Kind"/>), generados la primera
    ///  vez que hacen falta (~1 ms) y guardados. Se oyen a la IZQUIERDA o a la
    ///  DERECHA según dónde esté la rata en la pantalla, y más flojo si no se ve.
    ///  Volumen: general × "Voces de las ratas" (<c>opt_voices</c>).
    /// </summary>
    public static class RatVoice
    {
        public enum Kind : byte
        {
            /// <summary>Galimatías: burla (P) o frase al empezar el turno.</summary>
            Talk,
            /// <summary>"¡Ay!" de un golpe flojo.</summary>
            Ouch,
            /// <summary>"¡Aaagh!" de un golpe fuerte o una amputación.</summary>
            Hurt,
            /// <summary>"¡Ja, ja, ja!"</summary>
            Laugh,
            /// <summary>Grito largo al reventar.</summary>
            Death,
            /// <summary>"¡Hup!" de la voltereta.</summary>
            Hup,
            /// <summary>"¡Eh!" al empezar su turno.</summary>
            Hey,
            /// <summary>Grito y gorgoteo al caer al agua.</summary>
            Splash,
        }

        private const int Rate = 22050;
        private const int Variants = 4;

        /// <summary>Volumen de las voces (Opciones), 0-1.</summary>
        public static float Volume
        {
            get => PlayerPrefs.GetFloat("opt_voices", 0.8f);
            set => PlayerPrefs.SetFloat("opt_voices", Mathf.Clamp01(value));
        }

        /// <summary>Para las pruebas: cuántas frases se han dicho y cuántos clips distintos hay.</summary>
        public static int Said;
        public static int ClipCount => clips.Count;

        private static readonly Dictionary<(ulong, Kind, int), AudioClip> clips = new();
        private static readonly List<AudioSource> sources = new();
        private static int next;

        // ------------------------------------------------------------
        //  PERFIL DE VOZ
        // ------------------------------------------------------------
        public struct Profile
        {
            public float f0;         // tono base (Hz)
            public float throat;     // escala de formantes (garganta pequeña > 1)
            public float rasp;       // ruido (0-0.4)
            public float vibDepth;   // vibrato (fracción del tono)
            public float vibRate;    // Hz
            public float syllable;   // segundos por sílaba
            public float buzz;       // 0 = pulso (pito), 1 = sierra (zumbido)
        }

        /// <summary>La voz de un aspecto. Determinista: el mismo código suena igual en todos los PCs.</summary>
        public static Profile ProfileOf(ulong lookCode)
        {
            var look = RatLook.Unpack(lookCode);
            var rng = new System.Random((int)(lookCode ^ (lookCode >> 32)) * 31 + 7);
            float R() => (float)rng.NextDouble();
            float f0 = look.body switch
            {
                RatLook.BodyType.Enana => 330f, RatLook.BodyType.Flaca => 265f, RatLook.BodyType.Cabezona => 240f,
                RatLook.BodyType.Erguida => 225f, RatLook.BodyType.Salchicha => 215f, RatLook.BodyType.Jorobada => 200f,
                RatLook.BodyType.Topo => 190f, RatLook.BodyType.Gorda => 165f, RatLook.BodyType.Musculosa => 145f,
                RatLook.BodyType.Gigante => 118f, _ => 210f,
            };
            float throat = Mathf.Lerp(0.82f, 1.25f, Mathf.InverseLerp(110f, 340f, f0)) * (0.92f + R() * 0.16f);
            return new Profile
            {
                f0 = f0 * (0.86f + R() * 0.28f),
                throat = throat,
                rasp = look.fur == RatLook.FurType.Sarnosa || look.fur == RatLook.FurType.Zombi || look.fur == RatLook.FurType.Canosa
                       ? 0.22f + R() * 0.18f : R() * 0.14f,
                vibDepth = 0.005f + R() * 0.03f,
                vibRate = 4.5f + R() * 3f,
                syllable = 0.065f + R() * 0.045f,
                buzz = R(),
            };
        }

        // ------------------------------------------------------------
        //  REPRODUCIR
        // ------------------------------------------------------------
        /// <summary>
        ///  La rata dice algo (solo en ESTE PC; quien lo llama ya se encarga de
        ///  que pase en todos, p. ej. desde un ClientRpc).
        /// </summary>
        public static void Say(Component rat, Kind kind, float volume = 1f)
        {
            if (rat == null || Application.isBatchMode) return;
            var mv = rat.GetComponent<NetworkPlayerMovement>();
            if (mv == null) return;
            ulong code = mv.LookCode.Value;
            if (code == 0) code = mv.NetworkObjectId * 2654435761UL + 1;
            var hp = rat.GetComponent<NetworkHealth>();
            Vector3 at = hp != null ? (Vector3)hp.BodyCenter : rat.transform.position;
            Play(code, kind, at, volume);
        }

        /// <summary>Reproduce la voz de un aspecto en un punto del mundo.</summary>
        public static void Play(ulong lookCode, Kind kind, Vector3 worldPos, float volume = 1f)
        {
            if (Application.isBatchMode) return;
            float v = volume * Volume * PlayerPrefs.GetFloat("opt_master", 0.8f);
            if (v <= 0.001f) return;
            int variant = Random.Range(0, Variants);
            var clip = Get(lookCode, kind, variant);
            var src = Source();

            // Izquierda/derecha según la pantalla; más flojo si no se ve.
            float pan = 0f, far = 1f;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(worldPos);
                pan = Mathf.Clamp((vp.x - 0.5f) * 1.4f, -0.85f, 0.85f);
                bool onScreen = vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                far = onScreen ? 1f : 0.55f;
            }
            src.panStereo = pan;
            src.pitch = 1f + Random.Range(-0.025f, 0.025f);
            src.PlayOneShot(clip, v * far);
            Said++;
        }

        private static AudioSource Source()
        {
            sources.RemoveAll(s => s == null);
            if (sources.Count == 0)
            {
                var go = new GameObject("VocesDeRatas");
                Object.DontDestroyOnLoad(go);
                for (int i = 0; i < 8; i++)
                {
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    sources.Add(s);
                }
            }
            var src = sources[next % sources.Count];
            next++;
            return src;
        }

        /// <summary>Clip de una voz (lo genera la primera vez).</summary>
        public static AudioClip Get(ulong lookCode, Kind kind, int variant)
        {
            var key = (lookCode, kind, variant);
            if (clips.TryGetValue(key, out var c) && c != null) return c;
            var p = ProfileOf(lookCode);
            float[] data = Synth(p, kind, new System.Random((int)lookCode * 17 + (int)kind * 101 + variant * 7919));
            c = AudioClip.Create($"voz_{lookCode:X}_{kind}_{variant}", data.Length, 1, Rate, false);
            c.SetData(data, 0);
            if (clips.Count > 600) clips.Clear();   // tope de memoria (40 ratas × 8 tipos × 4 variantes cabe de sobra)
            clips[key] = c;
            return c;
        }

        // ------------------------------------------------------------
        //  0.37 — LEER UN TEXTO (chat): la rata "pronuncia" lo que escribes
        // ------------------------------------------------------------
        /// <summary>
        ///  La rata dice <paramref name="text"/> en ratuno: cada VOCAL del texto
        ///  es una sílaba con esa vocal (a, e, i, o, u), la consonante de delante
        ///  pone el chasquido (p, t, k, b, d, g, c, q, r) o el soplido (s, z, f,
        ///  j, x, h, ch); m, n, l, y, v... entran suaves. Los espacios son pausas,
        ///  "?" sube el tono al final, "!" y las MAYÚSCULAS lo suben y lo hacen
        ///  más fuerte. "hola" suena a "jo-la" dicho por una rata.
        ///  <paramref name="rat"/> puede ser null (en la sala de espera): entonces
        ///  suena centrada con la voz de <paramref name="lookCode"/>.
        /// </summary>
        public static void SayText(Component rat, ulong lookCode, string text, float volume = 1f)
        {
            if (Application.isBatchMode || string.IsNullOrWhiteSpace(text)) return;
            float v = volume * Volume * PlayerPrefs.GetFloat("opt_master", 0.8f);
            if (v <= 0.001f) return;
            Vector3 at = Vector3.zero;
            bool positional = false;
            if (rat != null)
            {
                var mv = rat.GetComponent<NetworkPlayerMovement>();
                if (mv != null && mv.LookCode.Value != 0) lookCode = mv.LookCode.Value;
                var hp = rat.GetComponent<NetworkHealth>();
                at = hp != null ? (Vector3)hp.BodyCenter : rat.transform.position;
                positional = true;
            }
            if (lookCode == 0) lookCode = (ulong)text.GetHashCode() | 1UL;
            var p = ProfileOf(lookCode);
            var data = Render(p, TextSyllables(p, text), 0.012f, 1f, new System.Random(text.GetHashCode()));
            var clip = AudioClip.Create("voz_texto", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            var src = Source();
            float pan = 0f, far = 1f;
            var cam = Camera.main;
            if (positional && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(at);
                pan = Mathf.Clamp((vp.x - 0.5f) * 1.4f, -0.85f, 0.85f);
                far = vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f ? 1f : 0.55f;
            }
            src.panStereo = pan;
            src.pitch = 1f;
            src.PlayOneShot(clip, v * far);
            Said++;
            Object.Destroy(clip, data.Length / (float)Rate + 1f);   // clip de un solo uso
        }

        /// <summary>Para las pruebas: el sonido de un texto dicho con la voz de <paramref name="lookCode"/>.</summary>
        public static float[] RenderText(ulong lookCode, string text) =>
            Render(ProfileOf(lookCode), TextSyllables(ProfileOf(lookCode), text), 0.012f, 1f, new System.Random(text.GetHashCode()));

        /// <summary>Sílabas de un texto (para <see cref="SayText"/> y las pruebas).</summary>
        public static int SyllablesOf(string text) => TextSyllables(ProfileOf(1), text).Count;

        private static List<Syl> TextSyllables(Profile p, string text)
        {
            var syl = new List<Syl>();
            if (text.Length > 140) text = text.Substring(0, 140);
            bool question = text.TrimEnd().EndsWith("?");
            bool shout = text.Contains("!");
            int letters = 0, upper = 0;
            foreach (char ch in text) if (char.IsLetter(ch)) { letters++; if (char.IsUpper(ch)) upper++; }
            bool caps = letters >= 3 && upper > letters * 0.6f;
            float baseP = (shout ? 1.12f : 1f) * (caps ? 1.12f : 1f);
            float gain = caps || shout ? 1f : 0.85f;

            float cons = 0f;           // consonante pendiente para la próxima vocal
            float s = p.syllable * 0.85f;
            int vowels = 0;
            foreach (char raw in text) if (VowelOf(raw) >= 0) vowels++;
            int idx = 0;
            for (int i = 0; i < text.Length && syl.Count < 28; i++)
            {
                char ch = char.ToLowerInvariant(text[i]);
                int v = VowelOf(ch);
                if (v >= 0)
                {
                    float t = vowels > 1 ? idx / (float)(vowels - 1) : 0f;
                    idx++;
                    // Entonación: baja poco a poco; en preguntas, sube al final.
                    float contour = question ? 0.95f + Mathf.Max(0f, t - 0.6f) * 1.1f : 1.1f - t * 0.22f;
                    // Diptongos ("ue", "ia"...): la vocal se desliza hacia la siguiente.
                    int next = i + 1 < text.Length ? VowelOf(char.ToLowerInvariant(text[i + 1])) : -1;
                    bool accent = "áéíóú".IndexOf(ch) >= 0;
                    float wob = 0.94f + (float)((ch * 7 + i * 13) % 11) / 100f;
                    syl.Add(new Syl
                    {
                        v0 = v, v1 = next >= 0 ? next : v,
                        len = s * (accent ? 1.4f : 1f) * (next >= 0 ? 1.3f : 1f),
                        p0 = contour * baseP * wob * (accent ? 1.12f : 1f), p1 = contour * baseP * wob * 0.97f,
                        gain = gain, cons = cons,
                    });
                    cons = 0f;
                    if (next >= 0) i++;   // el diptongo ya se ha comido la siguiente vocal
                    continue;
                }
                if (char.IsLetter(ch))
                {
                    cons = "ptkbdgcqr".IndexOf(ch) >= 0 ? 1f : "szfjxhç".IndexOf(ch) >= 0 ? 2f : Mathf.Max(cons, 0f);
                    continue;
                }
                // Espacio y signos: pausa tras la última sílaba
                if (syl.Count > 0)
                {
                    var last = syl[syl.Count - 1];
                    float add = ch == ' ' ? 0.05f : ".,;:!?…".IndexOf(ch) >= 0 ? 0.14f : 0f;
                    last.pause = Mathf.Max(last.pause, add);
                    syl[syl.Count - 1] = last;
                }
            }
            if (syl.Count == 0)   // "jajaja"-sin-vocales, "xd", emoticonos...: un gruñido corto
                syl.Add(new Syl { v0 = U, v1 = O, len = 0.12f, p0 = 1f, p1 = 0.9f, gain = 0.8f, cons = 1 });
            return syl;
        }

        /// <summary>a/e/i/o/u (con tildes y la y final) → índice de vocal; si no, -1.</summary>
        private static int VowelOf(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'a': case 'á': case 'à': return A;
                case 'e': case 'é': case 'è': return E;
                case 'i': case 'í': case 'ï': return I;
                case 'o': case 'ó': case 'ò': return O;
                case 'u': case 'ú': case 'ü': return U;
                default: return -1;
            }
        }

        // ------------------------------------------------------------
        //  SÍNTESIS
        // ------------------------------------------------------------
        // Formantes (Hz) de cada vocal: F1, F2.
        private static readonly Vector2[] Vowels =
        {
            new Vector2(800f, 1200f),   // a
            new Vector2(500f, 1850f),   // e
            new Vector2(320f, 2300f),   // i
            new Vector2(520f, 900f),    // o
            new Vector2(330f, 780f),    // u
        };
        private const int A = 0, E = 1, I = 2, O = 3, U = 4;

        /// <summary>Una sílaba: vocal (o deslizamiento de una vocal a otra), tono de principio a fin, consonante delante.</summary>
        private struct Syl
        {
            public int v0, v1;
            public float len, p0, p1, gain, cons;   // cons: 0 = sin consonante, 1 = chasquido, 2 = "h" (aire)
            public float pause;                      // 0.37: silencio extra DESPUÉS (entre palabras)
        }

        private static float[] Synth(Profile p, Kind kind, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var syl = new List<Syl>();
            float s = p.syllable;
            switch (kind)
            {
                case Kind.Talk:
                {
                    int n = 3 + rng.Next(5);
                    bool question = R() < 0.35f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Mathf.Max(1, n - 1);
                        float contour = question ? 0.95f + t * 0.35f : 1.12f - t * 0.25f;
                        float wob = 0.9f + R() * 0.25f;
                        syl.Add(new Syl { v0 = rng.Next(5), v1 = rng.Next(5), len = s * (0.8f + R() * 0.7f), p0 = contour * wob, p1 = contour * wob * (0.95f + R() * 0.1f), gain = 0.8f + R() * 0.2f, cons = R() < 0.8f ? 1 : 0 });
                    }
                    break;
                }
                case Kind.Ouch:
                    syl.Add(new Syl { v0 = A, v1 = I, len = 0.2f + R() * 0.08f, p0 = 1.55f, p1 = 1.15f, gain = 1f, cons = 0 });
                    break;
                case Kind.Hurt:
                    syl.Add(new Syl { v0 = A, v1 = A, len = 0.42f + R() * 0.15f, p0 = 1.4f, p1 = 0.9f, gain = 1f, cons = 2 });
                    syl.Add(new Syl { v0 = O, v1 = U, len = 0.12f, p0 = 0.9f, p1 = 0.7f, gain = 0.6f, cons = 0 });
                    break;
                case Kind.Laugh:
                {
                    int n = 4 + rng.Next(3);
                    for (int i = 0; i < n; i++)
                    {
                        float k = 1.35f - i * 0.07f;
                        syl.Add(new Syl { v0 = R() < 0.7f ? A : E, v1 = A, len = s * 0.9f, p0 = k, p1 = k * 0.95f, gain = 1f - i * 0.08f, cons = 2 });
                    }
                    break;
                }
                case Kind.Death:
                    syl.Add(new Syl { v0 = A, v1 = I, len = 0.7f + R() * 0.25f, p0 = 1.7f, p1 = 0.75f, gain = 1f, cons = 0 });
                    break;
                case Kind.Hup:
                    syl.Add(new Syl { v0 = U, v1 = U, len = 0.11f, p0 = 1.15f, p1 = 1.45f, gain = 0.9f, cons = 2 });
                    break;
                case Kind.Hey:
                    syl.Add(new Syl { v0 = E, v1 = E, len = 0.14f, p0 = 1.2f, p1 = 1.35f, gain = 0.9f, cons = 2 });
                    syl.Add(new Syl { v0 = O, v1 = O, len = 0.12f, p0 = 1.1f, p1 = 0.95f, gain = 0.7f, cons = 1 });
                    break;
                case Kind.Splash:
                    syl.Add(new Syl { v0 = A, v1 = A, len = 0.45f, p0 = 1.6f, p1 = 1.3f, gain = 1f, cons = 0 });
                    for (int i = 0; i < 4; i++)
                        syl.Add(new Syl { v0 = U, v1 = O, len = 0.06f, p0 = 0.8f + R() * 0.3f, p1 = 0.7f, gain = 0.45f, cons = 1 });
                    break;
            }

            // Duración total (+ pequeños silencios entre sílabas de la charla).
            float gap = kind == Kind.Talk ? 0.025f : kind == Kind.Laugh ? 0.03f : 0.008f;
            return Render(p, syl, gap, kind == Kind.Death ? 3f : 1f, rng);
        }

        /// <summary>Convierte sílabas en sonido (fuente → formantes), normalizado.</summary>
        private static float[] Render(Profile p, List<Syl> syl, float gap, float vibMul, System.Random rng)
        {
            float total = 0.02f;
            foreach (var sy in syl) total += sy.len + gap + sy.pause;
            var buf = new float[Mathf.CeilToInt(total * Rate)];

            float phase = 0f, vibPh = (float)rng.NextDouble() * 6.28f;
            // Estado de los dos resonadores (formantes) — continuo entre sílabas.
            float y1a = 0, y2a = 0, y1b = 0, y2b = 0;
            int pos = (int)(0.01f * Rate);
            uint noise = (uint)rng.Next() | 1u;
            float Noise() { noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5; return (noise / (float)uint.MaxValue) * 2f - 1f; }

            foreach (var sy in syl)
            {
                int n = Mathf.Max(1, (int)(sy.len * Rate));
                int consN = sy.cons > 0 ? (int)((sy.cons == 2 ? 0.035f : 0.018f) * Rate) : 0;
                for (int i = 0; i < n && pos < buf.Length; i++, pos++)
                {
                    float t = i / (float)n;
                    // Envolvente: ataque rápido, caída suave.
                    float env = Mathf.Min(1f, t * 14f) * Mathf.Min(1f, (1f - t) * 5f) * sy.gain;
                    // Tono con entonación + vibrato (más vibrato en los gritos largos).
                    float vib = 1f + p.vibDepth * vibMul * Mathf.Sin(vibPh);
                    vibPh += 6.2832f * p.vibRate / Rate;
                    float f = p.f0 * Mathf.Lerp(sy.p0, sy.p1, t) * vib;
                    phase += f / Rate; if (phase >= 1f) phase -= 1f;
                    // Fuente: mezcla de diente de sierra y pulso estrecho, más aire.
                    float saw = 2f * phase - 1f;
                    float pulse = phase < 0.18f ? 1f : -0.22f;
                    float src = Mathf.Lerp(pulse, saw, p.buzz) + Noise() * p.rasp;
                    // Consonante: ráfaga de ruido al principio de la sílaba.
                    if (i < consN)
                    {
                        float ck = 1f - i / (float)consN;
                        src = Mathf.Lerp(src, Noise() * (sy.cons == 2 ? 0.7f : 1.2f), ck);
                    }
                    // Formantes de la vocal (desliza de v0 a v1).
                    Vector2 fm = Vector2.Lerp(Vowels[sy.v0], Vowels[sy.v1], t) * p.throat;
                    float a = Resonate(src, fm.x, 90f, ref y1a, ref y2a);
                    float b = Resonate(src, fm.y, 140f, ref y1b, ref y2b);
                    buf[pos] = (a * 1.0f + b * 0.55f + src * 0.04f) * env;
                }
                pos += (int)((gap + sy.pause) * Rate);
            }

            // Normaliza a un pico de ~0.55 (todas las voces igual de fuertes).
            float peak = 0.0001f;
            for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            float g = 0.55f / peak;
            for (int i = 0; i < buf.Length; i++) buf[i] *= g;
            return buf;
        }

        /// <summary>Resonador de 2 polos (un formante) en <paramref name="freq"/> Hz con ancho <paramref name="bw"/>.</summary>
        private static float Resonate(float x, float freq, float bw, ref float y1, ref float y2)
        {
            freq = Mathf.Min(freq, Rate * 0.45f);
            float r = Mathf.Exp(-Mathf.PI * bw / Rate);
            float b1 = 2f * r * Mathf.Cos(2f * Mathf.PI * freq / Rate);
            float b2 = r * r;
            float y = (1f - r) * x + b1 * y1 - b2 * y2;
            y2 = y1; y1 = y;
            return y;
        }
    }
}
