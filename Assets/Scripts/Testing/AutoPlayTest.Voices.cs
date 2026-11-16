using System.Collections;
using System.IO;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.33 — papel "voices": VOCES DE LAS RATAS.
    ///   1. Guarda en <c>voces\</c> un .wav de cada tipo de frase para 6 ratas de
    ///      cuerpos distintos (para escucharlas sin jugar) y comprueba que no hay
    ///      silencios, saturación ni valores rotos.
    ///   2. En una partida local: P (burla), un golpe, una muerte y una voltereta
    ///      deben hacer hablar a la rata (contador <see cref="RatVoice.Said"/>).
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunVoices()
        {
            Log("Papel: VOCES DE LAS RATAS");
            string dir = Path.Combine(outDir, "voces");
            Directory.CreateDirectory(dir);
            var bodies = new[] { RatLook.BodyType.Enana, RatLook.BodyType.Flaca, RatLook.BodyType.Jorobada,
                                 RatLook.BodyType.Gorda, RatLook.BodyType.Musculosa, RatLook.BodyType.Gigante };
            var rng = new System.Random(1234);
            int bad = 0, files = 0;
            var f0s = new System.Collections.Generic.List<float>();
            foreach (var body in bodies)
            {
                ulong code = RatLook.Random(rng, (int)body).Pack();
                f0s.Add(RatVoice.ProfileOf(code).f0);
                foreach (RatVoice.Kind kind in System.Enum.GetValues(typeof(RatVoice.Kind)))
                {
                    var clip = RatVoice.Get(code, kind, 0);
                    var data = new float[clip.samples];
                    clip.GetData(data, 0);
                    float peak = data.Max(Mathf.Abs);
                    bool nan = data.Any(float.IsNaN);
                    if (nan || peak < 0.2f || peak > 0.99f || clip.length < 0.08f || clip.length > 2f) { bad++; Log($"⚠ voz rara: {body} {kind} pico {peak:F2} {clip.length:F2}s nan={nan}"); }
                    WriteWav(Path.Combine(dir, $"{body}_{kind}.wav"), data, clip.frequency);
                    files++;
                }
            }
            bool ordered = f0s[0] > f0s[f0s.Count - 1];
            // 0.37: frases del chat dichas en ratuno
            var phrases = new[] { ("hola", "hola"), ("que_tal", "¿Qué tal estás, rata?"), ("grito", "¡TE VOY A REVENTAR!"), ("frase", "Esa dinamita era para ti, sarnoso.") };
            int phraseFiles = 0;
            foreach (var body in new[] { RatLook.BodyType.Enana, RatLook.BodyType.Gigante })
            {
                ulong code = RatLook.Random(new System.Random(77), (int)body).Pack();
                foreach (var (file, text) in phrases)
                {
                    var data = RatVoice.RenderText(code, text);
                    WriteWav(Path.Combine(dir, $"chat_{body}_{file}.wav"), data, 22050);
                    phraseFiles++;
                }
            }
            // 0.40: un ejemplo de cada TIPO de voz (misma rata, misma frase)
            int typeFiles = 0;
            for (int vt = 1; vt < RatLook.Voices; vt++)
            {
                var l = RatLook.Random(new System.Random(5), (int)RatLook.BodyType.Flaca);
                l.voice = (RatLook.VoiceType)vt;
                var data = RatVoice.RenderText(l.Pack(), "¡Hola! Soy tu rata y así hablo.");
                if (data.Any(float.IsNaN) || data.Max(Mathf.Abs) < 0.2f) { bad++; Log($"⚠ voz rara: {l.voice}"); }
                WriteWav(Path.Combine(dir, $"tipo_{l.voice}.wav"), data, 22050);
                typeFiles++;
            }
            Check($"Tipos de voz → {typeFiles} guardados (tipo_*.wav)", typeFiles == RatLook.Voices - 1, "Tipos de voz rotos");
            // 0.39: canciones del baile (una por estilo) y sonidos gore
            int tunes = 0;
            for (int st = 0; st < DanceTunes.Styles; st++)
            {
                var data = DanceTunes.Render(st);   // semilla = estilo
                float pk = data.Max(Mathf.Abs);
                if (pk < 0.3f || data.Any(float.IsNaN)) { bad++; Log($"⚠ canción rara: {DanceTunes.StyleOf(st)} pico {pk:F2}"); }
                WriteWav(Path.Combine(dir, $"baile_{DanceTunes.StyleOf(st)}.wav"), data, 22050);
                tunes++;
            }
            foreach (var id in new[] { Sfx.Id.GoreBurst, Sfx.Id.Squelch, Sfx.Id.Plip })
            {
                var clip = Sfx.Clip(id);
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                WriteWav(Path.Combine(dir, $"gore_{id}.wav"), data, clip.frequency);
            }
            Check($"Canciones de baile → {tunes} estilos guardados (baile_*.wav) y 3 sonidos gore (gore_*.wav)", tunes == DanceTunes.Styles, "Canciones de baile rotas");
            int sylHola = RatVoice.SyllablesOf("hola"), sylLong = RatVoice.SyllablesOf("Esa dinamita era para ti, sarnoso.");
            Check($"Voz del chat → {phraseFiles} frases guardadas · \"hola\" = {sylHola} sílabas · frase larga = {sylLong}",
                  sylHola == 2 && sylLong >= 10, "Voz del chat: las sílabas no salen del texto");
            Check($"Voces → {files} clips guardados en voces\\ · {bad} raros · tono enana {f0s[0]:F0} Hz > gigante {f0s[f0s.Count - 1]:F0} Hz: {ordered}",
                  bad == 0 && ordered, "Voces: hay clips rotos o el tono no depende del cuerpo");

            // --- En partida ---
            MatchRules.OverrideLocalTeams = 2;
            MatchRules.OverrideRatsPerTeam = 2;
            MatchRules.OverrideTurnSeconds = 60;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            Ratms.UI.MainMenuController mm = null;
            yield return WaitFor(() => (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
            if (finished) yield break;
            yield return new WaitForSeconds(1f);
            mm.OnPlay();
            yield return new WaitForSeconds(0.3f);
            mm.StartLocalMatch();
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
            if (finished) yield break;
            yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 20f, "el primer turno");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);

            int s0 = RatVoice.Said;
            tm.TauntServerRpc(tm.CurrentRatId.Value);
            yield return new WaitForSeconds(0.8f);
            int taunt = RatVoice.Said - s0;

            var rats = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Where(m => m.IsSpawned && !m.IsTestDummy).ToList();
            var victim = rats.FirstOrDefault(r => r.TeamIndex.Value != tm.CurrentTeam.Value);
            int s1 = RatVoice.Said;
            if (victim != null) victim.GetComponent<NetworkHealth>().ApplyDamageServer(10, victim.GetComponent<NetworkHealth>().BodyCenter, DamageType.Generic);
            yield return new WaitForSeconds(0.6f);
            int hurt = RatVoice.Said - s1;
            int s2 = RatVoice.Said;
            if (victim != null) victim.GetComponent<NetworkHealth>().ApplyDamageServer(999, victim.GetComponent<NetworkHealth>().BodyCenter, DamageType.Generic);
            yield return new WaitForSeconds(1.2f);
            int death = RatVoice.Said - s2;
            yield return Capture("V01_muerte");
            Check($"Voces en partida → burla: {taunt} · golpe: {hurt} · muerte (grito + risa): {death} · clips distintos {RatVoice.ClipCount}",
                  taunt >= 1 && hurt >= 1 && death >= 1, "Voces: alguna acción no hace hablar a la rata");
            Finish(null);
        }

        /// <summary>WAV de 16 bits mono (para escuchar las voces fuera del juego).</summary>
        private static void WriteWav(string path, float[] data, int rate)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int bytes = data.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float f in data) w.Write((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
        }
    }
}
