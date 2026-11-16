using System;
using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  MI EQUIPO (primer paso de los cosméticos): nombre del equipo y de
    ///  cada una de las 4 ratas, con su aspecto. "Otra" sortea una rata
    ///  nueva y la deja FIJA; "Al azar" hace que cada partida salga una
    ///  distinta. Se guarda en <see cref="TeamProfile"/> (PlayerPrefs).
    /// </summary>
    public class TeamEditor : MonoBehaviour
    {
        public Action OnClose;
        private static readonly System.Random rng = new();
        private static readonly ulong[] previewCache = new ulong[TeamProfile.Slots];


        private static string PartLabel(RatLook l, int part) => part switch
        {
            0 => RatLook.BodyName(l.body),
            1 => RatLook.FurName(l.fur),
            2 => RatLook.HatName(l.hat),
            3 => RatLook.TailName(l.tail),
            4 => RatLook.EyeName(l.eye),
            5 => RatLook.MouthName(l.mouth),
            6 => RatLook.EarName(l.ear),
            7 => RatLook.MarkName(l.mark),
            9 => RatLook.VoiceName(l.voice),
            _ => RatLook.AccName(l.acc),
        };
        private const int Parts = 10;   // 0.40: + voz

        private static GUIStyle partStyle;

        private static int Wrap(int v, int dir, int n) => ((v + dir) % n + n) % n;

        private static RatLook Cycle(RatLook l, int part, int dir)
        {
            switch (part)
            {
                case 0: l.body = (RatLook.BodyType)Wrap((int)l.body, dir, RatLook.Bodies); break;
                case 1: l.fur = (RatLook.FurType)Wrap((int)l.fur, dir, RatLook.Furs); break;
                case 2:
                    // 0.35: los sombreros de logro no salen hasta conseguirlo (Career).
                    for (int k = 0; k < RatLook.Hats; k++)
                    {
                        l.hat = (RatLook.HatType)Wrap((int)l.hat, dir, RatLook.Hats);
                        if (!Career.IsHatLocked(l.hat)) break;
                    }
                    break;
                case 3: l.tail = (RatLook.TailType)Wrap((int)l.tail, dir, RatLook.Tails); break;
                case 4: l.eye = (RatLook.EyeType)Wrap((int)l.eye, dir, RatLook.Eyes); break;
                case 5: l.mouth = (RatLook.MouthType)Wrap((int)l.mouth, dir, RatLook.Mouths); break;
                case 6: l.ear = (RatLook.EarType)Wrap((int)l.ear, dir, RatLook.Ears); break;
                case 7: l.mark = (RatLook.MarkType)Wrap((int)l.mark, dir, RatLook.Marks); break;
                case 9: l.voice = (RatLook.VoiceType)Wrap((int)l.voice, dir, RatLook.Voices); break;
                default: l.acc = (RatLook.AccessoryType)Wrap((int)l.acc, dir, RatLook.Accessories); break;
            }
            return l;
        }

        private void OnGUI()
        {
            TrenchGUI.Begin();
            var r = new Rect((TrenchGUI.VW - 1000f) * 0.5f, (TrenchGUI.VH - 960f) * 0.5f, 1000f, 960f);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 16f, r.width, 50f), "MI EQUIPO");
            TrenchGUI.Small(new Rect(r.x + 40f, r.y + 66f, r.width - 80f, 40f),
                "Ponle nombre a tu equipo y a tus ratas. Cambia cada pieza con sus botones. «Otra» sortea una rata entera; «Al azar», distinta cada partida.");

            float x = r.x + 40f, y = r.y + 120f, w = r.width - 80f;
            TrenchGUI.Label(new Rect(x, y, 220f, 44f), "Equipo");
            TeamProfile.TeamName = TrenchGUI.Field(new Rect(x + 230f, y, w - 230f, 44f), TeamProfile.TeamName, 20, "Escribe el nombre de tu equipo…");
            y += 64f;

            for (int i = 0; i < TeamProfile.Slots; i++)
            {
                var row = new Rect(x, y, w, 156f);
                GUI.DrawTexture(new Rect(row.x - 6f, row.y - 4f, row.width + 12f, row.height + 8f), Texture2D.blackTexture);

                // Retrato (ampliado ×3)
                ulong look = TeamProfile.Look(i);
                if (look == 0)
                {
                    if (previewCache[i] == 0) previewCache[i] = RatLook.Random(rng).Pack();
                    look = previewCache[i];
                }
                var spr = RatAppearance.Preview(look, RatAppearance.ColorForTeam(0));
                var tr = spr.textureRect;
                var c = GUI.color;
                if (TeamProfile.Look(i) == 0) GUI.color = new Color(1f, 1f, 1f, 0.45f);   // al azar: retrato "fantasma"
                GUI.DrawTextureWithTexCoords(new Rect(row.x + 4f, row.y + 4f, 112f, 112f), spr.texture,
                    new Rect(tr.x / spr.texture.width, tr.y / spr.texture.height, tr.width / spr.texture.width, tr.height / spr.texture.height));
                GUI.color = c;
                if (TeamProfile.Look(i) == 0) TrenchGUI.Label(new Rect(row.x + 44f, row.y + 36f, 60f, 44f), "¿?");

                float fx = row.x + 136f;
                TrenchGUI.Small(new Rect(fx, row.y + 2f, 300f, 26f), Loc.F("Rata {0}", i + 1));
                string current = TeamProfile.RatName(i);
                string edited = TrenchGUI.Field(new Rect(fx, row.y + 30f, 340f, 42f), current, 16, "Nombre al azar");
                if (edited != current) TeamProfile.SetRatName(i, edited);
                // 0.22: pieza a pieza. Clic = siguiente opción (clic derecho = anterior).
                // Si estaba "al azar", se fija la rata que se ve y se cambia esa pieza.
                {
                    var l = RatLook.Unpack(look);
                    // 0.24: 9 piezas en dos filas (5 + 4) y la ventaja del cuerpo arriba.
                    var stats = l.Stats;
                    if (!string.IsNullOrEmpty(stats.summary))
                    {
                        var c0 = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f);
                        // 0.30: y el tamaño del blanco (la gigante recibe más, la enana menos)
                        string size = stats.HitScale >= 1.12f ? Loc.T("blanco grande") : stats.HitScale <= 0.9f ? Loc.T("blanco pequeño") : "";
                        TrenchGUI.Small(new Rect(fx + 90f, row.y + 2f, 560f, 26f), Loc.T(stats.summary) + (size.Length > 0 ? "  ·  " + size : ""));
                        GUI.color = c0;
                    }
                    float bw = (row.xMax - fx) / 5f;
                    for (int p = 0; p < Parts; p++)
                    {
                        var br = new Rect(fx + (p % 5) * bw, row.y + 80f + (p / 5) * 38f, bw - 6f, 34f);
                        bool hit = TrenchGUI.Button(br, "");
                        if (partStyle == null)
                            partStyle = new GUIStyle(GUI.skin.label)
                            {
                                alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = false,
                                clipping = TextClipping.Overflow, font = TrenchGUI.Typewriter,
                                normal = { textColor = new Color(1f, 0.93f, 0.78f) },
                            };
                        GUI.Label(br, Loc.T(PartLabel(l, p)), partStyle);
                        if (hit)
                        {
                            int dir = Event.current.button == 1 ? -1 : 1;
                            var nl = Cycle(l, p, dir);
                            TeamProfile.SetLook(i, nl.Pack());
                            // 0.40: al cambiar la voz, la rata se presenta para que la oigas
                            if (p == 9) Ratms.Networking.RatVoice.SayText(null, nl.Pack(), Loc.T("¡Hola! Soy tu rata y así hablo."));
                        }
                    }
                }

                if (TrenchGUI.Button(new Rect(row.xMax - 330f, row.y + 30f, 160f, 48f), "Otra"))
                    TeamProfile.SetLook(i, RatLook.Random(rng).Pack());
                if (TrenchGUI.Button(new Rect(row.xMax - 160f, row.y + 30f, 160f, 48f), "Al azar", TeamProfile.Look(i) == 0))
                {
                    TeamProfile.SetLook(i, 0);
                    previewCache[i] = RatLook.Random(rng).Pack();
                }
                y += 172f;
            }

            if (TrenchGUI.Button(new Rect(r.x + r.width * 0.3f, r.yMax - 76f, r.width * 0.4f, 54f), "Listo", big: true) ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                PlayerPrefs.Save();
                OnClose?.Invoke();
            }
            TrenchGUI.End();
        }
    }
}
