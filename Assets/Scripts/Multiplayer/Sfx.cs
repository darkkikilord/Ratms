using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  EFECTOS DE SONIDO generados por código (síntesis sencilla: ruido
    ///  filtrado, senos con barrido de tono, envolventes). Cero archivos de
    ///  audio. Cada clip se crea una vez y se reutiliza.
    ///
    ///  Volumen = "General" × "Efectos" del menú de Opciones (PlayerPrefs
    ///  opt_master / opt_sfx). Sonido 2D: se oye igual esté donde esté la
    ///  cámara (el mapa es pequeño).
    ///
    ///  Si algún día queréis sonidos "de verdad", basta con asignar los
    ///  AudioClip en los campos explosionSfx, etc. de cada arma: se oirán
    ///  además de estos (o quitad la llamada a Sfx).
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public enum Id { Explosion, BigExplosion, Shot, Whack, Swish, Launch, Throw, Splash, Teleport, Rope, Jump, TurnStart, Tick, Victory, Squeak, Laugh, Splat, Ouch }

        private const int Rate = 44100;
        private static Sfx instance;
        private static readonly Dictionary<Id, AudioClip> clips = new();
        private readonly List<AudioSource> sources = new();
        private int next;

        private static Sfx I
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[Sfx]");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<Sfx>();
                    for (int i = 0; i < 10; i++)
                    {
                        var s = go.AddComponent<AudioSource>();
                        s.playOnAwake = false;
                        s.spatialBlend = 0f;
                        instance.sources.Add(s);
                    }
                }
                return instance;
            }
        }

        /// <summary>Reproduce un efecto. <paramref name="volume"/> relativo (1 = normal).</summary>
        public static void Play(Id id, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (Application.isBatchMode) return;
            var fx = I;
            var clip = Get(id);
            if (clip == null) return;
            float v = volume * PlayerPrefs.GetFloat("opt_master", 0.8f) * PlayerPrefs.GetFloat("opt_sfx", 0.8f);
            if (v <= 0.001f) return;
            var src = fx.sources[fx.next];
            fx.next = (fx.next + 1) % fx.sources.Count;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(clip, v);
        }

        /// <summary>Genera todos los clips por adelantado (evita un tirón la primera vez). Devuelve cuántos.</summary>
        public static int Warmup()
        {
            int n = 0;
            foreach (Id id in System.Enum.GetValues(typeof(Id))) if (Get(id) != null) n++;
            return n;
        }

        private static AudioClip Get(Id id)
        {
            if (clips.TryGetValue(id, out var c)) return c;
            float[] d = id switch
            {
                Id.Explosion => Boom(0.9f, 60f, 0.75f),
                Id.BigExplosion => Boom(1.4f, 45f, 0.95f),
                Id.Shot => Mix(Noise(0.22f, 18f, 0.9f, lowpass: 0.5f), Sine(0.08f, 140f, 60f, 0.6f)),
                Id.Whack => Mix(Noise(0.12f, 30f, 0.6f, lowpass: 0.25f), Sine(0.12f, 220f, 110f, 0.8f)),
                Id.Swish => Sweep(0.18f, 0.08f, 0.5f),
                Id.Launch => Sweep(0.4f, 0.03f, 0.6f),
                Id.Throw => Sweep(0.15f, 0.12f, 0.4f),
                Id.Splash => Splash(),
                Id.Teleport => Mix(Sine(0.4f, 400f, 1600f, 0.45f, tremolo: 30f), Sine(0.4f, 600f, 2400f, 0.2f)),
                Id.Rope => Mix(Sweep(0.12f, 0.2f, 0.45f), Sine(0.06f, 900f, 500f, 0.3f)),
                Id.Jump => Sine(0.11f, 320f, 640f, 0.25f),
                Id.TurnStart => Mix(Sine(0.3f, 880f, 880f, 0.35f), Delay(Sine(0.3f, 1320f, 1320f, 0.3f), 0.08f)),
                Id.Tick => Sine(0.04f, 1200f, 1100f, 0.35f),
                Id.Victory => Arpeggio(new[] { 523f, 659f, 784f, 1047f }, 0.13f, 0.4f),
                Id.Squeak => Sine(0.3f, 1100f, 380f, 0.4f, tremolo: 18f),
                // Risa de rata: "ji-ji-ji-ji" agudo y descendente
                Id.Laugh => Chain(new[] { 1500f, 1380f, 1260f, 1150f, 1050f }, 0.075f, 0.035f, 0.35f),
                // Chof húmedo: ruido grave y corto + golpe
                Id.Splat => Mix(Noise(0.28f, 14f, 0.8f, lowpass: 0.12f), Sine(0.12f, 120f, 50f, 0.6f)),
                // Quejido corto al recibir daño
                Id.Ouch => Sine(0.14f, 900f, 600f, 0.3f, tremolo: 25f),
                _ => null,
            };
            if (d == null) return null;
            c = AudioClip.Create("sfx_" + id, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            clips[id] = c;
            return c;
        }

        // ============================================================
        //  Síntesis
        // ============================================================
        private static System.Random rng = new System.Random(1234);
        private static float Rnd() => (float)(rng.NextDouble() * 2.0 - 1.0);

        /// <summary>Explosión: ruido grave filtrado + golpe sinusoidal, caída exponencial.</summary>
        private static float[] Boom(float seconds, float thumpHz, float gain)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 4.5f) * Mathf.Clamp01(t * 200f);
                float cutoff = Mathf.Lerp(0.35f, 0.04f, t / seconds);        // se va apagando (más grave)
                lp += (Rnd() - lp) * cutoff;
                float thump = Mathf.Sin(2f * Mathf.PI * thumpHz * t * (1f - t * 0.4f)) * Mathf.Exp(-t * 7f);
                d[i] = Mathf.Clamp((lp * 1.8f + thump * 0.9f) * env * gain, -1f, 1f);
            }
            return d;
        }

        private static float[] Noise(float seconds, float decay, float gain, float lowpass = 1f)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Rnd() - lp) * lowpass;
                d[i] = lp * Mathf.Exp(-t * decay) * gain;
            }
            return d;
        }

        /// <summary>Tono con barrido de frecuencia (de f0 a f1) y envolvente suave.</summary>
        private static float[] Sine(float seconds, float f0, float f1, float gain, float tremolo = 0f)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, k = t / seconds;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2.0 * Mathf.PI * f / Rate;
                float env = Mathf.Clamp01(t * 300f) * (1f - k) * (1f - k);
                float trem = tremolo > 0f ? 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * tremolo * t) : 1f;
                d[i] = (float)System.Math.Sin(phase) * env * trem * gain;
            }
            return d;
        }

        /// <summary>"Fiuu": ruido con filtro que se abre y se cierra (paso de aire).</summary>
        private static float[] Sweep(float seconds, float brightness, float gain)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                float bell = Mathf.Sin(k * Mathf.PI);
                float cutoff = brightness * (0.3f + bell);
                lp += (Rnd() - lp) * cutoff;
                lp2 += (lp - lp2) * cutoff;
                d[i] = (lp - lp2) * 3f * bell * gain;
            }
            return d;
        }

        private static float[] Splash()
        {
            var d = Noise(0.6f, 5f, 0.5f, lowpass: 0.3f);
            for (int b = 0; b < 7; b++)   // burbujitas
            {
                var bub = Sine(0.05f, 600f + b * 140f, 1200f + b * 160f, 0.25f);
                int off = (int)((0.08f + b * 0.06f) * Rate);
                for (int i = 0; i < bub.Length && off + i < d.Length; i++) d[off + i] += bub[i];
            }
            return d;
        }

        private static float[] Arpeggio(float[] notes, float step, float gain)
        {
            int n = (int)((step * notes.Length + 0.4f) * Rate);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                var s = Sine(k == notes.Length - 1 ? 0.5f : step * 1.6f, notes[k], notes[k], gain);
                int off = (int)(k * step * Rate);
                for (int i = 0; i < s.Length && off + i < n; i++) d[off + i] += s[i];
            }
            return d;
        }

        /// <summary>Sílabas cortas seguidas (para la risa).</summary>
        private static float[] Chain(float[] pitches, float syllable, float gap, float gain)
        {
            float step = syllable + gap;
            int n = (int)((step * pitches.Length + 0.05f) * Rate);
            var d = new float[n];
            for (int k = 0; k < pitches.Length; k++)
            {
                var s = Sine(syllable, pitches[k] * 1.15f, pitches[k] * 0.85f, gain, tremolo: 45f);
                int off = (int)(k * step * Rate);
                for (int i = 0; i < s.Length && off + i < n; i++) d[off + i] += s[i];
            }
            return d;
        }

        private static float[] Delay(float[] a, float seconds)
        {
            int off = (int)(seconds * Rate);
            var d = new float[a.Length + off];
            System.Array.Copy(a, 0, d, off, a.Length);
            return d;
        }

        private static float[] Mix(float[] a, float[] b)
        {
            var d = new float[Mathf.Max(a.Length, b.Length)];
            for (int i = 0; i < d.Length; i++)
                d[i] = Mathf.Clamp((i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f), -1f, 1f);
            return d;
        }
    }
}
