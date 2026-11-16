using System.Collections;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.37 — papel "emotes": escaparate de los 8 emotes (partida local, de
    ///  cerca) con una foto de cada uno a mitad del gesto, y una del menú.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunEmotes()
        {
            Log("Papel: EMOTES");
            MatchRules.OverrideLocalTeams = 2;
            MatchRules.OverrideRatsPerTeam = 1;
            MatchRules.OverrideTurnSeconds = 90;
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
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null && tm.CurrentRatId.Value != ulong.MaxValue, 30f, "la partida");
            if (finished) yield break;
            yield return new WaitForSeconds(2f);
            var cam = CameraFollow.Instance;
            if (cam != null) cam.SetZoom(3.2f);
            yield return new WaitForSeconds(1f);

            ChatSystem.TestOpenEmoteMenu(true);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("E00_menu_emotes");
            ChatSystem.TestOpenEmoteMenu(false);

            int played = ChatSystem.EmotesPlayed;
            for (int i = 0; i < ChatSystem.Emotes.Length; i++)
            {
                ChatSystem.SendEmote(i);
                yield return new WaitForSeconds(0.55f);
                yield return Capture($"E{i + 1}_{ChatSystem.Emotes[i].name.Replace("¡", "").Replace("!", "").Replace(" ", "_").ToLower()}");
                yield return new WaitForSeconds(1.2f);   // el servidor pide 1,2 s entre emotes
            }
            int n = ChatSystem.EmotesPlayed - played;
            Check($"Emotes → {n} de {ChatSystem.Emotes.Length} hechos · canciones de baile sonadas: {DanceTunes.Played}",
                  n == ChatSystem.Emotes.Length && DanceTunes.Played >= 1, "Emotes: alguno no se hace o el baile no suena");
            Finish(null);
        }
    }
}
