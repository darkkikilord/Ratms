using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  MÚSICA generada por código (cero archivos). 0.20: antes eran dos bucles
    ///  de 8 compases (~14 s) que se repetían sin fin; ahora es una LISTA DE
    ///  CANCIONES de trinchera, cada una con estructura de canción de verdad:
    ///
    ///      Intro (2) · A (8) · A' (8, variada + contracanto) · B (8, puente) · A (8) · Final (2)
    ///
    ///  Estilos: MARCHA (oom-pah y caja militar), POLKA (rápida, bajo en
    ///  corcheas), VALS (3/4, bombo-chun-chun) y FÚNEBRE (lenta, timbales, para
    ///  la muerte súbita). Instrumentos: acordeón, pífano, metales y armónica.
    ///  La MELODÍA se compone con reglas sencillas: en los tiempos fuertes notas
    ///  del acorde, en los débiles notas de paso; un motivo de 2 compases que
    ///  se repite adaptado a los acordes y frases que cierran en la tónica.
    ///
    ///  Al acabar una canción entra otra (sin repetir la última) con fundido.
    ///  Hay tres listas: MENÚ, BATALLA y MUERTE SÚBITA (cambia sola al llegar).
    ///  Las canciones se componen en un HILO aparte para no dar tirones.
    ///  Volumen = General × Música de Opciones (opt_master / opt_music).
    /// </summary>
    public class Music : MonoBehaviour
    {
        private const int Rate = 22050;
        private static Music instance;

        // ------------------------------------------------------------
        //  CATÁLOGO
        // ------------------------------------------------------------
        private enum Style { March, Polka, Waltz, Dirge }
        private enum Lead { Accordion, Fife, Brass, Harmonica }
        private enum Mood { Menu, Battle, SuddenDeath }

        private class Song
        {
            public string name;       // en español (se traduce al mostrarlo)
            public Mood mood;
            public Style style;
            public Lead lead;
            public float bpm;
            public int root;          // nota MIDI de la tónica
            public bool minor;
            public int seed;
        }

        private static readonly Song[] Songs =
        {
            new() { name = "Marcha del cuartel",     mood = Mood.Menu,   style = Style.March, lead = Lead.Accordion, bpm = 112, root = 62, minor = true,  seed = 3 },
            new() { name = "Vals de la trinchera",   mood = Mood.Menu,   style = Style.Waltz, lead = Lead.Harmonica, bpm = 150, root = 57, minor = true,  seed = 11 },
            new() { name = "Polka del obús",         mood = Mood.Battle, style = Style.Polka, lead = Lead.Accordion, bpm = 160, root = 55, minor = true,  seed = 21 },
            new() { name = "¡A la carga!",           mood = Mood.Battle, style = Style.March, lead = Lead.Brass,     bpm = 132, root = 52, minor = true,  seed = 31 },
            new() { name = "El pífano de las ratas", mood = Mood.Battle, style = Style.March, lead = Lead.Fife,      bpm = 124, root = 62, minor = false, seed = 41 },
            new() { name = "Can-can del queso",      mood = Mood.Battle, style = Style.Polka, lead = Lead.Brass,     bpm = 170, root = 60, minor = false, seed = 51 },
            new() { name = "Vals macabro",           mood = Mood.Battle, style = Style.Waltz, lead = Lead.Accordion, bpm = 168, root = 52, minor = true,  seed = 61 },
            new() { name = "Agua al cuello",         mood = Mood.SuddenDeath, style = Style.Dirge, lead = Lead.Brass, bpm = 80, root = 48, minor = true, seed = 71 },
            new() { name = "Las últimas ratas",      mood = Mood.SuddenDeath, style = Style.March, lead = Lead.Fife,  bpm = 148, root = 53, minor = true, seed = 81 },
        };

        // ------------------------------------------------------------
        //  REPRODUCCIÓN
        // ------------------------------------------------------------
        private AudioSource a, b;          // "a" suena; "b" se apaga en el fundido
        private float fade = 1f;
        private const float FadeSeconds = 2.5f;
        private Mood mood = Mood.Menu;
        private int current = -1;
        private readonly Dictionary<int, AudioClip> cache = new();
        private readonly List<int> cacheOrder = new();
        private Task<float[]> composing;
        private int composingIndex = -1;
        private int pending = -1;          // canción que queremos poner en cuanto esté compuesta
        private readonly System.Random pick = new();

        // Rótulo con el nombre de la canción
        private string shownName;
        private float shownAt = -10f;
        private GUIStyle nameStyle;

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
            foreach (var s in new[] { a, b }) { s.loop = false; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; }
            SceneManager.activeSceneChanged += (_, __) => CheckMood(force: true);
            CheckMood(force: true);
        }

        private Mood WantedMood()
        {
            if (SceneManager.GetActiveScene().name == "MainMenu") return Mood.Menu;
            var tm = NetworkTurnManager.Instance;
            return tm != null && tm.SuddenDeath.Value ? Mood.SuddenDeath : Mood.Battle;
        }

        private void CheckMood(bool force)
        {
            var m = WantedMood();
            if (!force && m == mood) return;
            // Al cambiar de escena sin cambiar de ánimo (p. ej. revancha), que siga la canción.
            if (m == mood && current >= 0 && Songs[current].mood == m && a.isPlaying) return;
            mood = m;
            RequestNext();
        }

        /// <summary>Elige otra canción del ánimo actual (sin repetir la última) y la prepara.</summary>
        private void RequestNext()
        {
            var options = new List<int>();
            for (int i = 0; i < Songs.Length; i++)
                if (Songs[i].mood == mood && i != current) options.Add(i);
            if (options.Count == 0)
                for (int i = 0; i < Songs.Length; i++) if (Songs[i].mood == mood) options.Add(i);
            if (options.Count == 0) return;
            pending = options[pick.Next(options.Count)];
            EnsureComposing(pending);
        }

        private void EnsureComposing(int index)
        {
            if (cache.ContainsKey(index) || composingIndex == index) return;
            if (composing != null && !composing.IsCompleted) return;   // uno cada vez; ya llegará
            composingIndex = index;
            var song = Songs[index];
            composing = Task.Run(() => Compose(song));
        }

        private void Update()
        {
            // Terminó de componerse una canción → AudioClip (solo en el hilo principal)
            if (composing != null && composing.IsCompleted)
            {
                if (composing.Status == TaskStatus.RanToCompletion && composing.Result != null)
                {
                    var data = composing.Result;
                    var clip = AudioClip.Create("musica_" + composingIndex, data.Length, 1, Rate, false);
                    clip.SetData(data, 0);
                    Remember(composingIndex, clip);
                }
                else Debug.LogWarning($"[Música] No se pudo componer: {composing.Exception?.GetBaseException().Message}");
                composing = null;
                composingIndex = -1;
            }
            if (pending >= 0) EnsureComposing(pending);

            // ¿Toca cambiar de ánimo? (p. ej. empieza la muerte súbita)
            if (Time.frameCount % 30 == 0) CheckMood(force: false);

            // Poner la canción pendiente en cuanto esté lista
            if (pending >= 0 && cache.TryGetValue(pending, out var ready)) StartSong(pending, ready);

            // Antes de que acabe la actual, preparamos la siguiente
            if (pending < 0 && current >= 0 && a.clip != null && a.isPlaying &&
                a.clip.length - a.time < FadeSeconds + 0.1f)
                RequestNext();
            if (pending < 0 && current >= 0 && !a.isPlaying && fade >= 1f) RequestNext();

            float target = PlayerPrefs.GetFloat("opt_master", 0.8f) * PlayerPrefs.GetFloat("opt_music", 0.8f) * 0.32f;
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / FadeSeconds);
            a.volume = target * fade;
            b.volume = target * (1f - fade);
            if (fade >= 1f && b.isPlaying) b.Stop();
        }

        private void StartSong(int index, AudioClip clip)
        {
            pending = -1;
            current = index;
            (a, b) = (b, a);
            a.clip = clip;
            a.volume = 0f;
            a.Play();
            fade = 0f;
            shownName = Songs[index].name;
            shownAt = Time.unscaledTime;
        }

        private void Remember(int index, AudioClip clip)
        {
            cache[index] = clip;
            cacheOrder.Remove(index);
            cacheOrder.Add(index);
            // Como mucho 4 canciones en memoria (~5 MB cada una): se tira la más
            // antigua que no esté sonando.
            while (cacheOrder.Count > 4)
            {
                int victim = cacheOrder.Find(i => cache[i] != a.clip && cache[i] != b.clip);
                if (!cache.ContainsKey(victim) || cache[victim] == a.clip || cache[victim] == b.clip) break;
                cacheOrder.Remove(victim);
                Destroy(cache[victim]);
                cache.Remove(victim);
            }
        }

        /// <summary>Rótulo discreto "♪ Nombre" abajo a la derecha al empezar una canción.</summary>
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(shownName)) return;
            float t = Time.unscaledTime - shownAt;
            if (t > 5f) return;
            if (PlayerPrefs.GetFloat("opt_music", 0.8f) <= 0.01f) return;
            float alpha = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((5f - t) / 1f) * 0.55f;
            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
                if (Ratms.UI.TrenchGUI.Typewriter != null) nameStyle.font = Ratms.UI.TrenchGUI.Typewriter;
            }
            nameStyle.fontSize = Mathf.RoundToInt(16 * scale);
            nameStyle.normal.textColor = new Color(1f, 0.95f, 0.8f, alpha);
            string text = "♪ " + Ratms.UI.Loc.T(shownName);
            GUI.Label(new Rect(0f, Screen.height - 64f * scale, Screen.width - 18f * scale, 24f * scale), text, nameStyle);
        }

        // ============================================================
        //  COMPOSICIÓN (se ejecuta en un hilo aparte: nada de Unity aquí
        //  salvo matemáticas puras)
        // ============================================================
        private static float Freq(int midi) => 440f * (float)System.Math.Pow(2.0, (midi - 69) / 12.0);

        private static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };   // menor natural
        private static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };

        /// <summary>Nota de la escala por GRADO (0 = tónica; puede ser negativo o pasar de 7).</summary>
        private static int Degree(Song s, int deg)
        {
            var sc = s.minor ? MinorScale : MajorScale;
            int oct = (int)System.Math.Floor(deg / 7.0);
            int d = deg - oct * 7;
            return s.root + oct * 12 + sc[d];
        }

        /// <summary>Acorde (grados) sobre el grado <paramref name="deg"/>. En menor, el V es mayor (sensible).</summary>
        private static int[] Chord(Song s, int deg)
        {
            int[] c = { Degree(s, deg), Degree(s, deg + 2), Degree(s, deg + 4) };
            if (s.minor && ((deg % 7) + 7) % 7 == 4) c[1] += 1;   // V mayor: sube la tercera
            return c;
        }

        // Progresiones por sección (grados del acorde, uno por compás).
        private static readonly int[] ProgA_Minor = { 0, 6, 5, 4, 0, 3, 4, 0 };      // i VII VI V | i iv V i
        private static readonly int[] ProgB_Minor = { 2, 6, 0, 4, 5, 3, 4, 4 };      // III VII i V | VI iv V V
        private static readonly int[] ProgA_Major = { 0, 4, 5, 3, 0, 4, 1, 0 };      // I V vi IV | I V ii I
        private static readonly int[] ProgB_Major = { 3, 0, 4, 5, 3, 0, 1, 4 };      // IV I V vi | IV I ii V

        private static float[] Compose(Song s)
        {
            var rng = new System.Random(s.seed);
            int beatsPerBar = s.style == Style.Waltz ? 3 : 4;
            float beat = 60f / s.bpm;
            float bar = beat * beatsPerBar;

            // Secciones: (progresión, variante)  variante: 0 = A, 1 = A', 2 = B
            int[] progA = s.minor ? ProgA_Minor : ProgA_Major;
            int[] progB = s.minor ? ProgB_Minor : ProgB_Major;
            var sections = new List<(int[] prog, int kind)> { (progA, 0), (progA, 1), (progB, 2), (progA, 0) };
            int bars = 2 + sections.Count * 8 + 2;
            int n = (int)((bars * bar + 1.5f) * Rate);
            var d = new float[n];

            // Motivos (ritmo en corcheas + contorno), uno para A y otro para B
            var motifA = MakeMotif(rng, beatsPerBar, s.style);
            var motifB = MakeMotif(rng, beatsPerBar, s.style);

            // ---- INTRO: tambor solo + bajo en la tónica ----
            float t = 0f;
            for (int i = 0; i < 2; i++, t += bar)
            {
                Drums(d, s, t, beat, beatsPerBar, rng, intro: true, fill: i == 1);
                Bass(d, s, t, beat, beatsPerBar, Chord(s, 0));
            }

            // ---- SECCIONES ----
            int prevNote = Degree(s, 7);
            foreach (var (prog, kind) in sections)
            {
                var motif = kind == 2 ? motifB : motifA;
                for (int i = 0; i < 8; i++, t += bar)
                {
                    var chord = Chord(s, prog[i]);
                    bool fill = i == 7;
                    bool crash = i == 0;
                    Drums(d, s, t, beat, beatsPerBar, rng, intro: false, fill: fill, crash: crash);
                    Bass(d, s, t, beat, beatsPerBar, chord);
                    Comp(d, s, t, beat, beatsPerBar, chord);
                    bool cadence = i == 7 || i == 3;
                    prevNote = Melody(d, s, t, beat, beatsPerBar, chord, motif, prog[i], i, kind, cadence, prevNote, rng);
                }
            }

            // ---- FINAL: acorde largo de tónica + redoble ----
            var tonic = Chord(s, 0);
            Drums(d, s, t, beat, beatsPerBar, rng, intro: false, fill: true, crash: true);
            Tone(d, t, bar * 1.8f, Freq(tonic[0] - 24), 0.3f, 2);
            foreach (int c in tonic) Tone(d, t, bar * 1.8f, Freq(c - 12), 0.06f, 0);
            LeadNote(d, s, t, bar * 1.8f, Degree(s, 7), 0.13f);

            // Saturación suave (pega todo, suena a gramófono) y fundidos en los bordes
            for (int i = 0; i < n; i++) d[i] = (float)System.Math.Tanh(d[i] * 1.25f) * 0.9f;
            int edge = Rate / 50;
            for (int i = 0; i < edge; i++) { float k = i / (float)edge; d[i] *= k; d[n - 1 - i] *= k; }
            return d;
        }

        /// <summary>Un motivo de 2 compases: en qué corcheas suena nota y cuánto dura, con su "dirección".</summary>
        private class Motif { public bool[] on; public int[] len; public int[] step; }

        private static Motif MakeMotif(System.Random rng, int beatsPerBar, Style style)
        {
            int slots = beatsPerBar * 2 * 2;   // corcheas en 2 compases
            var m = new Motif { on = new bool[slots], len = new int[slots], step = new int[slots] };
            for (int i = 0; i < slots;)
            {
                bool strong = i % 2 == 0;
                // Más notas en polka, menos en fúnebre
                double density = style == Style.Polka ? 0.85 : style == Style.Dirge ? 0.45 : 0.7;
                if (strong || rng.NextDouble() < density * 0.6)
                {
                    m.on[i] = true;
                    int maxLen = style == Style.Dirge ? 4 : 2;
                    int len = rng.NextDouble() < (style == Style.Dirge ? 0.6 : 0.35) ? Mathf.Min(maxLen, slots - i) : 1;
                    m.len[i] = len;
                    m.step[i] = rng.Next(-2, 3);   // −2..2 grados respecto a la nota anterior
                    i += len;
                }
                else i++;
            }
            // El último tiempo, nota larga (respiración de la frase)
            m.on[slots - 2] = true; m.len[slots - 2] = 2;
            return m;
        }

        /// <summary>Melodía de un compás siguiendo el motivo; devuelve la última nota.</summary>
        private static int Melody(float[] d, Song s, float t0, float beat, int beatsPerBar, int[] chord, Motif motif,
                                  int chordDeg, int barInSection, int kind, bool cadence, int prev, System.Random rng)
        {
            float eighth = beat / 2f;
            int slotsPerBar = beatsPerBar * 2;
            int offset = (barInSection % 2) * slotsPerBar;   // primera o segunda mitad del motivo
            int octaveShift = kind == 1 ? 12 : 0;             // A' una octava más arriba
            float vol = s.lead == Lead.Brass ? 0.11f : s.lead == Lead.Fife ? 0.1f : 0.13f;

            for (int e = 0; e < slotsPerBar; e++)
            {
                int k = offset + e;
                if (!motif.on[k]) continue;
                int note;
                bool strong = e % 2 == 0;
                if (cadence && e >= slotsPerBar - 2)
                {
                    note = NearestOf(new[] { chord[0], chord[0] + 12 }, prev);   // la frase cierra en la fundamental
                }
                else if (strong)
                {
                    // Nota del acorde más cercana a la anterior + el paso del motivo
                    int target = prev + motif.step[k] * 2;
                    note = NearestOf(new[] { chord[0], chord[1], chord[2], chord[0] + 12, chord[1] + 12, chord[2] + 12, chord[0] - 12 }, target);
                }
                else
                {
                    // Nota de paso: un grado de escala arriba o abajo
                    note = StepScale(s, prev, motif.step[k] >= 0 ? 1 : -1);
                }
                // Mantener la melodía en un registro cantable
                while (note < s.root + 2) note += 12;
                while (note > s.root + 19) note -= 12;
                prev = note;
                float dur = eighth * motif.len[k] * 0.92f;
                LeadNote(d, s, t0 + e * eighth, dur, note + octaveShift, vol);

                // A' lleva un contracanto (una sexta por debajo) en las notas largas
                if (kind == 1 && motif.len[k] >= 2)
                    Tone(d, t0 + e * eighth, dur, Freq(note + octaveShift - 9), vol * 0.45f, 0);
            }
            return prev;
        }

        private static int NearestOf(int[] options, int target)
        {
            int best = options[0];
            foreach (int o in options) if (System.Math.Abs(o - target) < System.Math.Abs(best - target)) best = o;
            return best;
        }

        private static int StepScale(Song s, int note, int dir)
        {
            for (int k = 1; k <= 3; k++)
            {
                int c = note + dir * k;
                int rel = ((c - s.root) % 12 + 12) % 12;
                if (System.Array.IndexOf(s.minor ? MinorScale : MajorScale, rel) >= 0) return c;
            }
            return note + dir * 2;
        }

        private static void LeadNote(float[] d, Song s, float start, float dur, int midi, float vol)
        {
            float f = Freq(midi);
            switch (s.lead)
            {
                case Lead.Fife: Tone(d, start, dur, f * 2f, vol * 0.8f, 3, vibrato: true); break;
                case Lead.Brass: Tone(d, start, dur, f, vol, 4, vibrato: true); break;
                case Lead.Harmonica: Tone(d, start, dur, f, vol, 5, vibrato: true); break;
                default: Tone(d, start, dur, f * 1.0015f, vol, 1, vibrato: true); break;   // acordeón un pelín desafinado
            }
        }

        private static void Bass(float[] d, Song s, float t0, float beat, int beatsPerBar, int[] chord)
        {
            int root = chord[0] - 24, fifth = chord[0] - 24 + 7;
            switch (s.style)
            {
                case Style.Polka:   // bajo en cada tiempo alternando fundamental y quinta
                    for (int i = 0; i < beatsPerBar; i++)
                        Tone(d, t0 + i * beat, beat * 0.45f, Freq(i % 2 == 0 ? root : fifth - 12), 0.28f, 2);
                    break;
                case Style.Waltz:
                    Tone(d, t0, beat * 0.9f, Freq(root), 0.3f, 2);
                    break;
                case Style.Dirge:
                    Tone(d, t0, beat * beatsPerBar * 0.95f, Freq(root), 0.26f, 2);
                    Tone(d, t0, beat * beatsPerBar * 0.95f, Freq(root - 12), 0.12f, 2);
                    break;
                default:            // marcha: oom (1) ... pah (3)
                    Tone(d, t0, beat * 0.8f, Freq(root), 0.3f, 2);
                    Tone(d, t0 + 2 * beat, beat * 0.8f, Freq(fifth - 12), 0.26f, 2);
                    break;
            }
        }

        /// <summary>Acompañamiento: los "chun" del oom-pah, del vals o un colchón en la fúnebre.</summary>
        private static void Comp(float[] d, Song s, float t0, float beat, int beatsPerBar, int[] chord)
        {
            switch (s.style)
            {
                case Style.Waltz:
                    for (int i = 1; i < 3; i++) foreach (int c in chord) Tone(d, t0 + i * beat, beat * 0.4f, Freq(c - 12), 0.055f, 0);
                    break;
                case Style.Polka:
                    for (int i = 0; i < beatsPerBar; i++) foreach (int c in chord) Tone(d, t0 + i * beat + beat * 0.5f, beat * 0.25f, Freq(c - 12), 0.05f, 0);
                    break;
                case Style.Dirge:
                    foreach (int c in chord) Tone(d, t0, beat * beatsPerBar, Freq(c - 12), 0.035f, 1);
                    break;
                default:
                    foreach (int c in chord)
                    {
                        Tone(d, t0 + beat, beat * 0.45f, Freq(c - 12), 0.06f, 0);
                        Tone(d, t0 + 3 * beat, beat * 0.45f, Freq(c - 12), 0.06f, 0);
                    }
                    break;
            }
        }

        private static void Drums(float[] d, Song s, float t0, float beat, int beatsPerBar, System.Random rng,
                                  bool intro, bool fill, bool crash = false)
        {
            if (crash && !intro) Cymbal(d, t0, 0.12f, rng);
            switch (s.style)
            {
                case Style.Waltz:
                    Kick(d, t0, 0.45f);
                    Snare(d, t0 + beat, 0.1f, rng); Snare(d, t0 + 2 * beat, 0.1f, rng);
                    break;
                case Style.Polka:
                    for (int i = 0; i < beatsPerBar; i++)
                    {
                        Kick(d, t0 + i * beat, i % 2 == 0 ? 0.45f : 0.3f);
                        Snare(d, t0 + i * beat + beat * 0.5f, 0.16f, rng);
                    }
                    break;
                case Style.Dirge:
                    Timpani(d, t0, 0.5f);
                    if (fill) Timpani(d, t0 + 2.5f * beat, 0.35f);
                    break;
                default:
                    Kick(d, t0, 0.5f); Kick(d, t0 + 2 * beat, 0.42f);
                    Snare(d, t0 + beat, 0.26f, rng); Snare(d, t0 + 3 * beat, 0.26f, rng);
                    Snare(d, t0 + 3.5f * beat, 0.1f, rng);
                    break;
            }
            // Redoble de caja al final de frase
            if (fill && s.style != Style.Dirge)
            {
                float start = t0 + (beatsPerBar - 1) * beat;
                for (int k = 0; k < 8; k++) Snare(d, start + k * beat / 8f, 0.08f + k * 0.022f, rng);
            }
        }

        // ------------------------------------------------------------
        //  SINTETIZADORES
        // ------------------------------------------------------------
        /// <summary>
        ///  wave: 0 = pulso (acordes), 1 = acordeón (pulso + quinta), 2 = bajo
        ///  (triángulo), 3 = pífano (seno + soplo), 4 = metal (diente de sierra que
        ///  se abre), 5 = armónica (cuadrada con trémolo).
        /// </summary>
        private static void Tone(float[] d, float start, float dur, float freq, float vol, int wave, bool vibrato = false)
        {
            int s0 = (int)(start * Rate), len = (int)((dur + 0.1f) * Rate);
            if (s0 < 0) return;
            double ph = 0, ph2 = 0;
            float lp = 0f;
            uint noise = (uint)(s0 * 2654435761u + 1u);
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float attack = wave == 4 ? 0.035f : wave == 3 ? 0.02f : 0.012f;
                float env = System.Math.Min(1f, t / attack) *
                            (t < dur ? 1f - 0.3f * t / System.Math.Max(0.01f, dur) : (float)System.Math.Exp(-(t - dur) * 28f) * 0.7f);
                float vib = vibrato && t > 0.12f ? 1f + 0.0055f * (float)System.Math.Sin(t * 34f) : 1f;
                float f = freq * vib;
                ph += f / Rate; ph2 += f * 1.5f / Rate;
                float frac = (float)(ph - System.Math.Floor(ph));
                float v;
                float cut = 0.35f;
                switch (wave)
                {
                    case 0: v = frac < 0.3f ? 1f : -1f; break;
                    case 1: v = (frac < 0.45f ? 1f : -1f) * 0.7f + ((ph2 - System.Math.Floor(ph2)) < 0.5 ? 0.3f : -0.3f); break;
                    case 2: v = 1f - 4f * System.Math.Abs(frac - 0.5f); cut = 0.25f; break;
                    case 3:
                        noise = noise * 1664525u + 1013904223u;
                        v = (float)System.Math.Sin(ph * 2.0 * System.Math.PI) + ((noise >> 9) / 8388608f - 1f) * 0.08f;
                        cut = 0.6f;
                        break;
                    case 4:
                        v = frac * 2f - 1f;
                        cut = 0.08f + 0.35f * System.Math.Min(1f, t / 0.08f);   // el "bwaa" del metal: el filtro se abre
                        break;
                    default:
                        v = (frac < 0.5f ? 1f : -1f) * (0.8f + 0.2f * (float)System.Math.Sin(t * 38f));
                        cut = 0.3f;
                        break;
                }
                lp += (v - lp) * cut;
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
                ph += (110f + (45f - 110f) * (t / 0.22f)) / Rate;
                d[s0 + i] += (float)System.Math.Sin(ph * 2.0 * System.Math.PI) * (float)System.Math.Exp(-t * 16f) * vol;
            }
        }

        private static void Timpani(float[] d, float start, float vol)
        {
            int s0 = (int)(start * Rate), len = (int)(0.9f * Rate);
            double ph = 0;
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += (75f - 8f * System.Math.Min(1f, t * 4f)) / Rate;
                d[s0 + i] += (float)System.Math.Sin(ph * 2.0 * System.Math.PI) * (float)System.Math.Exp(-t * 4.5f) * vol;
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
                d[s0 + i] += (nz - lp * 0.5f) * (float)System.Math.Exp(-t * 26f) * vol;
            }
        }

        private static void Cymbal(float[] d, float start, float vol, System.Random rng)
        {
            int s0 = (int)(start * Rate), len = (int)(1.2f * Rate);
            float prev = 0f;
            for (int i = 0; i < len && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float nz = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = nz - prev; prev = nz;   // agudo y metálico
                d[s0 + i] += hp * (float)System.Math.Exp(-t * 3.2f) * vol;
            }
        }
    }
}
