using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  MÚSICA generada por código (cero archivos): una MARCHA DE TRINCHERA
    ///  cómica con bajo "oom-pah", acordeón de pulso, tambor militar y
    ///  redobles. Dos temas:
    ///   • Menú: marcha a 112 ppm en re menor.
    ///   • Batalla: más rápida, oscura y con más tambor.
    ///  Cambia sola según la escena (con fundido). Volumen = General × Música
    ///  de Opciones (PlayerPrefs opt_master / opt_music).
    /// </summary>
    public class Music : MonoBehaviour
    {
        private const int Rate = 22050;
        private static Music instance;
        private AudioSource a, b;          // dos fuentes para el fundido
        private AudioClip menuClip, battleClip;
        private AudioClip wanted;
        private float fade = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || instance != null) return;
            var go = new GameObject("[Música]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Music>();
        }

        private void Awake()
        {
            a = gameObject.AddComponent<AudioSource>();
            b = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { a, b }) { s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; }
            menuClip = Compose(false);
            battleClip = Compose(true);
            SceneManager.activeSceneChanged += (_, __) => PickTrack();
            PickTrack();
        }

        private void PickTrack()
        {
            wanted = SceneManager.GetActiveScene().name == "MainMenu" ? menuClip : battleClip;
            if (a.clip == wanted && a.isPlaying) return;
            // La fuente "a" es siempre la que suena; "b" se queda la vieja y se apaga.
            (a, b) = (b, a);
            a.clip = wanted;
            a.volume = 0f;
            a.Play();
            fade = 0f;
        }

        private void Update()
        {
            float target = PlayerPrefs.GetFloat("opt_master", 0.8f) * PlayerPrefs.GetFloat("opt_music", 0.8f) * 0.32f;
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / 1.5f);
            a.volume = target * fade;
            b.volume = target * (1f - fade);
            if (fade >= 1f && b.isPlaying) b.Stop();
        }

        // ============================================================
        //  COMPOSICIÓN
        // ============================================================
        private static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static AudioClip Compose(bool battle)
        {
            float bpm = battle ? 138f : 112f;
            float beat = 60f / bpm, eighth = beat / 2f;
            const int bars = 8;
            int n = Mathf.CeilToInt(bars * 4 * beat * Rate);
            var d = new float[n];
            var rng = new System.Random(battle ? 7 : 3);

            // Acordes por compás: Dm A Dm A | Gm Dm A Dm   (batalla: Dm Bb A Dm ×2)
            int[][] chords = battle
                ? new[] { new[] { 50, 53, 57 }, new[] { 46, 50, 53 }, new[] { 45, 49, 52 }, new[] { 50, 53, 57 },
                          new[] { 50, 53, 57 }, new[] { 46, 50, 53 }, new[] { 45, 49, 52 }, new[] { 45, 49, 52 } }
                : new[] { new[] { 57, 62, 65 }, new[] { 57, 61, 64 }, new[] { 57, 62, 65 }, new[] { 57, 61, 64 },
                          new[] { 55, 58, 62 }, new[] { 57, 62, 65 }, new[] { 57, 61, 64 }, new[] { 57, 62, 65 } };
            int[] roots = battle ? new[] { 38, 34, 33, 38, 38, 34, 33, 33 } : new[] { 38, 33, 38, 33, 43, 38, 33, 38 };

            int[] melody = battle
                ? new[]
                {
                    62,-1,-1,62, 65,-1,64,62,  58,-1,-1,58, 62,-1,60,58,  57,-1,61,-1, 64,-1,61,57,  62,-1,-1,-1, 69,-1,-1,-1,
                    74,-1,72,70, 69,-1,65,62,  70,-1,69,67, 65,-1,62,58,  61,-1,64,-1, 67,-1,64,61,  62,-1,57,-1, 50,-1,-1,-1,
                }
                : new[]
                {
                    62,-1,62,65, 69,-1,67,65,  64,-1,61,64, 69,-1,-1,-1,  62,-1,62,65, 69,72,70,69,  67,65,64,61, 62,-1,-1,-1,
                    67,-1,67,70, 74,-1,72,70,  69,-1,65,69, 74,-1,-1,-1,  73,-1,72,70, 69,67,64,61,  62,-1,69,-1, 62,-1,-1,-1,
                };

            for (int bar = 0; bar < bars; bar++)
            {
                float t0 = bar * 4 * beat;
                int root = roots[bar];
                // OOM-PAH: bajo en 1 y 3, acorde en 2 y 4
                Tone(d, t0, beat * 0.8f, Freq(root), 0.30f, wave: 2);
                Tone(d, t0 + 2 * beat, beat * 0.8f, Freq(root + 7 - (battle ? 0 : 12)), 0.26f, wave: 2);
                foreach (int c in chords[bar])
                {
                    Tone(d, t0 + beat, beat * 0.45f, Freq(c), 0.07f, wave: 0);
                    Tone(d, t0 + 3 * beat, beat * 0.45f, Freq(c), 0.07f, wave: 0);
                }
                // Tambor militar: bombo en 1 y 3, caja en 2 y 4, redoble al final de cada 4 compases
                Kick(d, t0, 0.5f); Kick(d, t0 + 2 * beat, 0.42f);
                Snare(d, t0 + beat, 0.28f, rng); Snare(d, t0 + 3 * beat, 0.28f, rng);
                if (battle) { Snare(d, t0 + 1.5f * beat, 0.12f, rng); Snare(d, t0 + 3.5f * beat, 0.12f, rng); }
                if (bar % 4 == 3)
                    for (int k = 0; k < 8; k++) Snare(d, t0 + 3 * beat + k * beat / 8f, 0.1f + k * 0.02f, rng);
                // Melodía (acordeón de pulso, un pelín desafinado: suena viejo)
                for (int e = 0; e < 8; e++)
                {
                    int note = melody[(bar * 8 + e) % melody.Length];
                    if (note < 0) continue;
                    int len = 1;
                    while (e + len < 8 && melody[(bar * 8 + e + len) % melody.Length] < 0 && len < 4) len++;
                    float f = Freq(note + (battle ? -12 : 0)) * (1f + 0.003f * (float)(rng.NextDouble() - 0.5));
                    Tone(d, t0 + e * eighth, eighth * len * 0.9f, f, battle ? 0.12f : 0.14f, wave: 1, vibrato: true);
                }
            }

            // Saturación suave y fundido en las juntas para que el bucle no haga clic
            for (int i = 0; i < n; i++) d[i] = (float)System.Math.Tanh(d[i] * 1.2f) * 0.9f;
            int edge = Rate / 100;
            for (int i = 0; i < edge; i++) { float k = i / (float)edge; d[i] *= k; d[n - 1 - i] *= k; }

            var clip = AudioClip.Create(battle ? "musica_batalla" : "musica_menu", n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>wave: 0 = pulso (acorde), 1 = acordeón (pulso + quinta suave), 2 = bajo redondo.</summary>
        private static void Tone(float[] d, float start, float dur, float freq, float vol, int wave, bool vibrato = false)
        {
            int s0 = (int)(start * Rate), len = (int)((dur + 0.08f) * Rate);
            double ph = 0, ph2 = 0;
            float lp = 0f;
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.012f) * (t < dur ? 1f - 0.35f * t / Mathf.Max(0.01f, dur) : Mathf.Exp(-(t - dur) * 30f) * 0.65f);
                float f = freq * (vibrato ? 1f + 0.006f * Mathf.Sin(t * 33f) : 1f);
                ph += f / Rate; ph2 += f * 1.5f / Rate;
                float frac = (float)(ph - System.Math.Floor(ph));
                float v;
                switch (wave)
                {
                    case 0: v = frac < 0.3f ? 1f : -1f; break;
                    case 1: v = (frac < 0.45f ? 1f : -1f) * 0.7f + (((ph2 - System.Math.Floor(ph2)) < 0.5) ? 0.3f : -0.3f); break;
                    default: v = 1f - 4f * Mathf.Abs(frac - 0.5f); break;   // triángulo
                }
                lp += (v - lp) * (wave == 2 ? 0.25f : 0.35f);   // suaviza los bordes (menos chillón)
                d[s0 + i] += lp * env * vol;
            }
        }

        private static void Kick(float[] d, float start, float vol)
        {
            int s0 = (int)(start * Rate), len = (int)(0.22f * Rate);
            double ph = 0;
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += Mathf.Lerp(110f, 45f, t / 0.22f) / Rate;
                d[s0 + i] += Mathf.Sin((float)(ph * 2.0 * System.Math.PI)) * Mathf.Exp(-t * 16f) * vol;
            }
        }

        private static void Snare(float[] d, float start, float vol, System.Random rng)
        {
            int s0 = (int)(start * Rate), len = (int)(0.16f * Rate);
            float lp = 0f;
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float nz = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (nz - lp) * 0.6f;
                d[s0 + i] += (nz - lp * 0.5f) * Mathf.Exp(-t * 26f) * vol;
            }
        }
    }
}
