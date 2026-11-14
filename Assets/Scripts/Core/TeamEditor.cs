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
        private static readonly uint[] previewCache = new uint[TeamProfile.Slots];

        private void OnGUI()
        {
            TrenchGUI.Begin();
            var r = new Rect((TrenchGUI.VW - 1000f) * 0.5f, (TrenchGUI.VH - 820f) * 0.5f, 1000f, 820f);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 16f, r.width, 50f), "MI EQUIPO");
            TrenchGUI.Small(new Rect(r.x + 40f, r.y + 66f, r.width - 80f, 40f),
                "Ponle nombre a tu equipo y a tus ratas. «Otra» sortea una rata nueva y la deja fija; «Al azar», distinta cada partida.");

            float x = r.x + 40f, y = r.y + 120f, w = r.width - 80f;
            TrenchGUI.Label(new Rect(x, y, 220f, 44f), "Equipo");
            TeamProfile.TeamName = TrenchGUI.Field(new Rect(x + 230f, y, w - 230f, 44f), TeamProfile.TeamName, 20, "Escribe el nombre de tu equipo…");
            y += 64f;

            for (int i = 0; i < TeamProfile.Slots; i++)
            {
                var row = new Rect(x, y, w, 120f);
                GUI.DrawTexture(new Rect(row.x - 6f, row.y - 4f, row.width + 12f, row.height + 8f), Texture2D.blackTexture);

                // Retrato (ampliado ×3)
                uint look = TeamProfile.Look(i);
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
                TrenchGUI.Small(new Rect(fx, row.y + 78f, 520f, 40f),
                    TeamProfile.Look(i) == 0 ? "Aspecto: al azar en cada partida" : RatLook.Unpack(look).Describe());

                if (TrenchGUI.Button(new Rect(row.xMax - 330f, row.y + 30f, 160f, 48f), "Otra"))
                    TeamProfile.SetLook(i, RatLook.Random(rng).Pack());
                if (TrenchGUI.Button(new Rect(row.xMax - 160f, row.y + 30f, 160f, 48f), "Al azar", TeamProfile.Look(i) == 0))
                {
                    TeamProfile.SetLook(i, 0);
                    previewCache[i] = RatLook.Random(rng).Pack();
                }
                y += 136f;
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
