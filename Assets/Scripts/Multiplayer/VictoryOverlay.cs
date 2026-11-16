using System;
using System.Collections;
using System.Collections.Generic;
using Ratms.UI;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  CARTEL DE VICTORIA (0.19; rehecho en 0.38). Lo enciende <see cref="VictoryScreen"/>;
    ///  los botones llaman a sus acciones.
    ///
    ///   • ENTRADA: la frase cae como un SELLO (grande → su tamaño, con rebote y
    ///     un poco torcida) y estalla CONFETI del color del ganador y dorado,
    ///     con "pops" de fuegos.
    ///   • PODIO (izquierda): las ratas del equipo ganador BAILANDO (las vivas)
    ///     y las caídas en gris. En empate, lápidas.
    ///   • TABLA por equipo (<see cref="MatchStats"/>): las cifras van SUBIENDO
    ///     fila a fila.
    ///   • MEDALLAS: Carnicero (más bajas), Saco de boxeo (más daño recibido),
    ///     Fuego amigo (más bajas propias) y Mejor golpe.
    ///   • LOGROS que has conseguido en ESTA partida (<see cref="Career"/>).
    /// </summary>
    public class VictoryOverlay : MonoBehaviour
    {
        public string phrase, winnerLine;
        public Color winnerColor = Color.white;
        public int winnerTeam = -1;
        public Action OnRematch, OnLeave;
        public bool rematchEnabled = true;
        public string rematchLabel = "¡REVANCHA!", leaveLabel = "Volver al menú";

        private float shownAt;
        private int pops;

        // --- Confeti (en coordenadas virtuales de TrenchGUI) ---
        private struct Bit { public Vector2 pos, vel; public float rot, spin, w, h; public Color c; }
        private readonly List<Bit> confetti = new();
        private float nextBurst;
        private int bursts;

        // --- Podio ---
        private struct Podium { public ulong look; public string name; public bool alive; }
        private readonly List<Podium> podium = new();
        private float podiumCachedAt = -10f;

        private static Texture2D medalTex, stoneTex;

        private void OnEnable()
        {
            shownAt = Time.unscaledTime;
            pops = 0;
            bursts = 0;
            nextBurst = shownAt + 0.15f;
            confetti.Clear();
            podium.Clear();
            podiumCachedAt = -10f;
        }

        private void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            // Tres andanadas de confeti (y "pops") al principio; luego, alguno suelto.
            if (now >= nextBurst && bursts < 3)
            {
                Burst(bursts == 0 ? 110 : 60);
                bursts++;
                nextBurst = now + 0.7f;
                Sfx.PlayPitched(Sfx.Id.Shot, 0.22f, 1.6f + 0.2f * bursts);
            }
            else if (bursts >= 3 && confetti.Count < 40 && UnityEngine.Random.value < dt * 6f) Burst(2, fromTop: true);
            for (int i = confetti.Count - 1; i >= 0; i--)
            {
                var b = confetti[i];
                b.vel.y += 380f * dt;                      // gravedad (en px virtuales)
                b.vel.x *= Mathf.Exp(-1.2f * dt);
                b.vel.y = Mathf.Min(b.vel.y, 260f);        // cae flotando, como papel
                b.pos += b.vel * dt;
                b.pos.x += Mathf.Sin(now * 3f + i) * 20f * dt;
                b.rot += b.spin * dt;
                if (b.pos.y > TrenchGUI.VH + 40f) confetti.RemoveAt(i); else confetti[i] = b;
            }
        }

        private void Burst(int n, bool fromTop = false)
        {
            Color team = winnerTeam >= 0 ? (Color)TurnHUD.TeamColor(winnerTeam) : new Color(0.7f, 0.7f, 0.7f);
            Color[] pal = { team, TrenchGUI.Gold, Color.white, Color.Lerp(team, Color.white, 0.4f), new Color(0.95f, 0.35f, 0.3f) };
            float vw = TrenchGUI.VW;
            for (int i = 0; i < n; i++)
            {
                Vector2 pos, vel;
                if (fromTop) { pos = new Vector2(UnityEngine.Random.Range(0f, vw), -20f); vel = new Vector2(UnityEngine.Random.Range(-40f, 40f), UnityEngine.Random.Range(40f, 120f)); }
                else
                {
                    bool left = i % 2 == 0;   // dos cañones, uno a cada lado de abajo
                    pos = new Vector2(left ? 40f : vw - 40f, TrenchGUI.VH - 30f);
                    float a = (left ? UnityEngine.Random.Range(55f, 80f) : UnityEngine.Random.Range(100f, 125f)) * Mathf.Deg2Rad;
                    vel = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * UnityEngine.Random.Range(700f, 1250f);
                }
                confetti.Add(new Bit
                {
                    pos = pos, vel = vel, rot = UnityEngine.Random.Range(0f, 360f), spin = UnityEngine.Random.Range(-540f, 540f),
                    w = UnityEngine.Random.Range(8f, 16f), h = UnityEngine.Random.Range(4f, 8f), c = pal[UnityEngine.Random.Range(0, pal.Length)],
                });
            }
        }

        private void RefreshPodium()
        {
            if (Time.unscaledTime - podiumCachedAt < 1f) return;
            podiumCachedAt = Time.unscaledTime;
            podium.Clear();
            if (winnerTeam < 0) return;
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.InstanceID))
            {
                if (m == null || !m.IsSpawned || m.IsTestDummy || m.TeamIndex.Value != winnerTeam) continue;
                var h = m.GetComponent<NetworkHealth>();
                podium.Add(new Podium { look = m.LookCode.Value, name = m.RatName.Value.ToString(), alive = h != null && !h.IsDead.Value });
            }
            podium.Sort((a, b) => b.alive.CompareTo(a.alive));
            if (podium.Count > 4) podium.RemoveRange(4, podium.Count - 4);
        }

        private void OnGUI()
        {
            GUI.depth = -20;
            TrenchGUI.Begin();
            float now = Time.unscaledTime, age = now - shownAt;
            float t = Mathf.Clamp01(age / 0.35f);
            TrenchGUI.Dim(0.6f * t);
            DrawConfetti(behind: true);

            var tm = NetworkTurnManager.Instance;
            var stats = MatchStats.Last;
            RefreshPodium();
            int rows = stats != null ? stats.teams.Count : 0;
            float rowStep = rows > 6 ? 36f : 46f;
            var recent = Career.UnlockedSince(now - (stats != null ? stats.seconds : 600f) - 10f);
            float tableH = 50f + Mathf.Max(rows, 2) * rowStep;
            float w = 1320f, h = 230f + Mathf.Max(tableH, 330f) + 120f + (recent.Count > 0 ? 50f : 0f) + 100f;
            var r = new Rect((TrenchGUI.VW - w) * 0.5f, (TrenchGUI.VH - h) * 0.5f, w, h);
            float s = Mathf.Lerp(0.9f, 1f, 1f - (1f - t) * (1f - t));
            var prev = GUI.matrix;
            var c = new Vector2(r.center.x * TrenchGUI.Scale, r.center.y * TrenchGUI.Scale);
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), c);

            TrenchGUI.Panel(r);

            // ---------- Sello con la frase ----------
            {
                float st = Mathf.Clamp01((age - 0.05f) / 0.45f);
                float k = st < 1f ? 1f + 1.2f * (1f - st) * (1f - st) - 0.12f * Mathf.Sin(st * Mathf.PI) : 1f;
                float ang = Mathf.Lerp(-14f, -3f, st);
                var tr = new Rect(r.x, r.y + 22f, r.width, 64f);
                var m0 = GUI.matrix;
                Vector2 piv = new Vector2(tr.center.x * TrenchGUI.Scale, tr.center.y * TrenchGUI.Scale);
                GUIUtility.ScaleAroundPivot(new Vector2(k, k), piv);
                GUIUtility.RotateAroundPivot(ang, piv);
                var cc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(st * 3f));
                TrenchGUI.Title(tr, phrase);
                GUI.color = cc;
                GUI.matrix = m0;
                if (st >= 1f && pops == 0) { pops = 1; if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.12f, 0.2f); }
            }
            var old = TrenchGUI.LabelStyle.normal.textColor;
            var oldAlign = TrenchGUI.LabelStyle.alignment;
            int oldSize = TrenchGUI.LabelStyle.fontSize;
            TrenchGUI.LabelStyle.normal.textColor = winnerColor;
            TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
            TrenchGUI.LabelStyle.fontSize = 36;
            TrenchGUI.Label(new Rect(r.x, r.y + 100f, r.width, 50f), winnerLine);
            TrenchGUI.LabelStyle.fontSize = oldSize;

            float top = r.y + 170f;
            // ---------- Podio ----------
            var pod = new Rect(r.x + 40f, top, 380f, Mathf.Max(tableH, 330f));
            DrawPodium(pod, now, tm);

            // ---------- Tabla (las cifras suben) ----------
            float y = top, x = r.x + 450f, tw = r.width - 490f;
            float[] col = { 0f, 0.3f, 0.45f, 0.65f, 0.87f };
            string[] head = { "Equipo", "Bajas", "Daño hecho", "Daño recibido", "Vivas" };
            TrenchGUI.LabelStyle.normal.textColor = TrenchGUI.Gold;
            for (int i = 0; i < head.Length; i++)
            {
                TrenchGUI.LabelStyle.alignment = i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                float cw = (i + 1 < col.Length ? col[i + 1] : 1f) - col[i];
                TrenchGUI.Label(new Rect(x + tw * col[i], y, tw * cw, 36f), head[i]);
            }
            y += 44f;
            if (stats == null) TrenchGUI.Small(new Rect(x, y, tw, 30f), "…");
            else
            {
                int ri = 0;
                foreach (var row in stats.teams)
                {
                    float rt = Mathf.Clamp01((age - 0.5f - ri * 0.12f) / 0.8f);   // cuenta hacia arriba
                    rt = 1f - (1f - rt) * (1f - rt);
                    int Up(int v) => Mathf.RoundToInt(v * rt);
                    bool win = row.team == winnerTeam;
                    if (win && Event.current.type == EventType.Repaint)
                    {
                        var gc = GUI.color;
                        GUI.color = new Color(1f, 0.85f, 0.3f, 0.16f + 0.06f * Mathf.Sin(now * 4f));
                        GUI.DrawTexture(new Rect(x - 12f, y - 2f, tw + 24f, rowStep - 4f), Texture2D.whiteTexture);
                        GUI.color = gc;
                    }
                    string name = tm != null ? tm.TeamDisplayName(row.team) : Loc.F("Equipo {0}", row.team + 1);
                    string kills = row.ownKills > 0 ? Loc.F("{0}  (+{1} propias)", Up(row.kills), Up(row.ownKills)) : Up(row.kills).ToString();
                    string[] cells = { (win ? "★ " : "") + name, kills, Up(row.dealt).ToString(), Up(row.taken).ToString(), Up(row.alive).ToString() };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        TrenchGUI.LabelStyle.alignment = i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                        TrenchGUI.LabelStyle.normal.textColor = i == 0 ? TurnHUD.TeamTextColor(row.team) : TrenchGUI.Paper;
                        float cw = (i + 1 < col.Length ? col[i + 1] : 1f) - col[i];
                        TrenchGUI.Label(new Rect(x + tw * col[i], y, tw * cw, rowStep - 6f), cells[i]);
                    }
                    y += rowStep;
                    ri++;
                }
                // Duración
                int secs = Mathf.FloorToInt(stats.seconds);
                TrenchGUI.Small(new Rect(x, y + 4f, tw, 28f), Loc.F("Duración: {0}:{1:00}", secs / 60, secs % 60));
            }
            TrenchGUI.LabelStyle.normal.textColor = old;
            TrenchGUI.LabelStyle.alignment = oldAlign;

            // ---------- Medallas ----------
            float my = top + Mathf.Max(tableH, 330f) + 14f;
            DrawMedals(new Rect(r.x + 40f, my, r.width - 80f, 100f), stats, tm, age);

            // ---------- Logros de esta partida ----------
            float ly = my + 110f;
            if (recent.Count > 0)
            {
                var cc = GUI.color;
                GUI.color = new Color(1f, 0.82f, 0.3f);
                TrenchGUI.Label(new Rect(r.x + 40f, ly, r.width - 80f, 36f),
                    Loc.T("Logros nuevos:") + "  " + string.Join("  ·  ", recent.ConvertAll(a => "★ " + Loc.T(a.name))));
                GUI.color = cc;
                ly += 50f;
            }

            // ---------- Botones ----------
            float by = r.yMax - 88f, bw = (r.width - 140f) * 0.5f;
            bool en = GUI.enabled;
            GUI.enabled = rematchEnabled;
            if (TrenchGUI.Button(new Rect(r.x + 50f, by, bw, 62f), rematchLabel, big: Loc.T(rematchLabel).Length <= 14)) OnRematch?.Invoke();
            GUI.enabled = en;
            if (TrenchGUI.Button(new Rect(r.xMax - 50f - bw, by, bw, 62f), leaveLabel)) OnLeave?.Invoke();

            GUI.matrix = prev;
            DrawConfetti(behind: false);
            TrenchGUI.End();
        }

        private void DrawConfetti(bool behind)
        {
            if (Event.current.type != EventType.Repaint) return;
            var m0 = GUI.matrix;
            var c0 = GUI.color;
            for (int i = 0; i < confetti.Count; i++)
            {
                if ((i % 3 == 0) != behind) continue;   // un tercio por detrás del cartel, el resto por delante
                var b = confetti[i];
                GUI.matrix = m0;
                Vector2 piv = b.pos * TrenchGUI.Scale;
                GUIUtility.RotateAroundPivot(b.rot, piv);
                float flip = Mathf.Abs(Mathf.Cos(b.rot * Mathf.Deg2Rad * 2f));   // gira "de canto", como papel
                GUI.color = b.c * new Color(0.75f + 0.25f * flip, 0.75f + 0.25f * flip, 0.75f + 0.25f * flip, 1f);
                GUI.DrawTexture(new Rect(b.pos.x - b.w * 0.5f, b.pos.y - b.h * 0.5f * flip, b.w, Mathf.Max(1.5f, b.h * flip)), Texture2D.whiteTexture);
            }
            GUI.matrix = m0;
            GUI.color = c0;
        }

        private void DrawPodium(Rect area, float now, NetworkTurnManager tm)
        {
            var cc = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.28f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = cc;
            if (winnerTeam < 0 || podium.Count == 0)
            {
                // Empate: lápidas
                if (stoneTex == null) stoneTex = MakeStone();
                for (int i = 0; i < 3; i++)
                    GUI.DrawTexture(new Rect(area.x + 40f + i * 110f, area.yMax - 150f, 90f, 120f), stoneTex);
                var al0 = TrenchGUI.LabelStyle.alignment;
                TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
                TrenchGUI.Label(new Rect(area.x, area.y + 30f, area.width, 40f), "Descansen en paz");
                TrenchGUI.LabelStyle.alignment = al0;
                return;
            }
            // Peana dorada
            var basis = new Rect(area.x + 20f, area.yMax - 46f, area.width - 40f, 26f);
            GUI.color = new Color(0.75f, 0.58f, 0.2f);
            GUI.DrawTexture(basis, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.78f, 0.3f);
            GUI.DrawTexture(new Rect(basis.x, basis.y, basis.width, 6f), Texture2D.whiteTexture);
            GUI.color = cc;

            Color32 team = RatAppearance.ColorForTeam(winnerTeam);
            int n = podium.Count;
            float slot = (area.width - 40f) / n;
            float size = Mathf.Min(150f, slot * 1.15f);
            for (int i = 0; i < n; i++)
            {
                var p = podium[i];
                Sprite spr;
                float hop = 0f, rot = 0f;
                if (p.alive)
                {
                    var frames = RatAppearance.WalkFrames(p.look, team);
                    spr = frames[(int)(now * 8f + i) % frames.Length];
                    hop = Mathf.Abs(Mathf.Sin(now * 5f + i * 1.3f)) * 26f;     // botes de celebración
                    rot = Mathf.Sin(now * 5f + i * 1.3f) * 8f;
                }
                else spr = RatAppearance.Preview(p.look, team);
                float cx = area.x + 20f + slot * (i + 0.5f);
                var rr = new Rect(cx - size * 0.5f, basis.y - size + 8f - hop, size, size);
                var tr = spr.textureRect;
                var m0 = GUI.matrix;
                if (Mathf.Abs(rot) > 0.01f) GUIUtility.RotateAroundPivot(rot, new Vector2(rr.center.x, rr.yMax) * TrenchGUI.Scale);
                GUI.color = p.alive ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.75f);
                // Las de la derecha miran a la izquierda: todas hacia el centro.
                bool faceLeft = cx > area.center.x;
                var uv = new Rect(tr.x / spr.texture.width, tr.y / spr.texture.height, tr.width / spr.texture.width, tr.height / spr.texture.height);
                if (faceLeft) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
                GUI.DrawTextureWithTexCoords(rr, spr.texture, uv);
                GUI.matrix = m0;
                GUI.color = cc;
                // Nombre con sombra (en el color del equipo pero claro: sobre el fondo oscuro no se leía)
                var al = TrenchGUI.LabelStyle.alignment;
                int fs = TrenchGUI.LabelStyle.fontSize;
                TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
                TrenchGUI.LabelStyle.fontSize = 18;
                GUI.color = p.alive ? Color.Lerp(TurnHUD.TeamTextColor(winnerTeam), Color.white, 0.45f) : new Color(0.7f, 0.7f, 0.7f);
                TrenchGUI.Label(new Rect(cx - slot * 0.5f, basis.yMax, slot, 26f), p.alive ? p.name : "† " + p.name);
                GUI.color = cc;
                TrenchGUI.LabelStyle.fontSize = fs;
                TrenchGUI.LabelStyle.alignment = al;
            }
            var al2 = TrenchGUI.LabelStyle.alignment;
            TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
            GUI.color = TrenchGUI.Gold;
            TrenchGUI.Label(new Rect(area.x, area.y + 10f, area.width, 36f), "¡LOS VENCEDORES!");
            GUI.color = cc;
            TrenchGUI.LabelStyle.alignment = al2;
        }

        private void DrawMedals(Rect area, MatchStats stats, NetworkTurnManager tm, float age)
        {
            if (stats == null || stats.teams.Count == 0) return;
            string Team(int i) => tm != null ? tm.TeamDisplayName(i) : Loc.F("Equipo {0}", i + 1);
            var medals = new List<(string title, string who, string what, int team)>();
            MatchStats.Row Best(Func<MatchStats.Row, int> f) { MatchStats.Row b = null; foreach (var r in stats.teams) if (b == null || f(r) > f(b)) b = r; return b; }
            var butcher = Best(r => r.kills);
            if (butcher != null && butcher.kills > 0) medals.Add(("Carnicero", Team(butcher.team), Loc.F("{0} bajas", butcher.kills), butcher.team));
            var bag = Best(r => r.taken);
            if (bag != null && bag.taken > 0) medals.Add(("Saco de boxeo", Team(bag.team), Loc.F("{0} de daño recibido", bag.taken), bag.team));
            var friendly = Best(r => r.ownKills);
            if (friendly != null && friendly.ownKills > 0) medals.Add(("Fuego amigo", Team(friendly.team), Loc.F("{0} propias", friendly.ownKills), friendly.team));
            if (stats.bestHit > 0) medals.Add(("Mejor golpe", stats.bestHitBy, Loc.F("{0} de daño", stats.bestHit), -1));
            if (medals.Count == 0) return;
            if (medalTex == null) medalTex = MakeMedal();
            float mw = Mathf.Min(300f, (area.width - (medals.Count - 1) * 14f) / medals.Count);
            float total = medals.Count * mw + (medals.Count - 1) * 14f;
            float x0 = area.x + (area.width - total) * 0.5f;
            var cc = GUI.color;
            for (int i = 0; i < medals.Count; i++)
            {
                float mt = Mathf.Clamp01((age - 1.4f - i * 0.25f) / 0.3f);   // van apareciendo una a una
                if (mt <= 0f) continue;
                var m = medals[i];
                var cell = new Rect(x0 + i * (mw + 14f), area.y + (1f - mt) * 30f, mw, area.height);
                GUI.color = new Color(0f, 0f, 0f, 0.32f * mt);
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, mt);
                GUI.DrawTexture(new Rect(cell.x + 10f, cell.y + 14f, 56f, 72f), medalTex);
                GUI.color = new Color(TrenchGUI.Gold.r, TrenchGUI.Gold.g, TrenchGUI.Gold.b, mt);
                TrenchGUI.Label(new Rect(cell.x + 76f, cell.y + 6f, cell.width - 84f, 32f), m.title);
                GUI.color = m.team >= 0 ? Color.Lerp(TurnHUD.TeamTextColor(m.team), Color.white, 0.45f) * new Color(1, 1, 1, mt) : new Color(1f, 1f, 1f, mt);
                TrenchGUI.Small(new Rect(cell.x + 76f, cell.y + 40f, cell.width - 84f, 26f), m.who);
                GUI.color = new Color(0.85f, 0.85f, 0.8f, mt);
                TrenchGUI.Small(new Rect(cell.x + 76f, cell.y + 64f, cell.width - 84f, 26f), m.what);
            }
            GUI.color = cc;
        }

        /// <summary>Medalla pixel-art 14×18: cinta roja y disco dorado.</summary>
        private static Texture2D MakeMedal()
        {
            string[] rows =
            {
                "RRRR....RRRR..", ".RRRR..RRRR...", "..RRRRRRRR....", "...RRRRRR.....", "....RRRR......",
                "....YYYY......", "..YYYYYYYY....", ".YYWYYYYYYY...", ".YWYYOOYYYY...", "YYYYOYYOYYYY..",
                "YYYYOYYOYYYY..", "YYYYYOOYYYYY..", ".YYYYYYYYYD...", ".YYYYYYYYYD...", "..YYYYYYDD....",
                "...DDDDDD.....", "..............", "..............",
            };
            var t = new Texture2D(14, 18, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 18; y++)
                for (int x = 0; x < 14; x++)
                {
                    char ch = rows[y][x];
                    Color32 c = ch switch
                    {
                        'R' => new Color32(190, 40, 40, 255), 'Y' => new Color32(240, 190, 60, 255), 'W' => new Color32(255, 245, 200, 255),
                        'O' => new Color32(180, 120, 30, 255), 'D' => new Color32(160, 110, 30, 255), _ => new Color32(0, 0, 0, 0),
                    };
                    t.SetPixel(x, 17 - y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Lápida pixel-art 9×12 (empates).</summary>
        private static Texture2D MakeStone()
        {
            string[] rows =
            {
                "..GGGGG..", ".GLLLLLG.", "GLLLLLLLG", "GLLLDLLLG", "GLLDDDLLG", "GLLLDLLLG", "GLLLDLLLG",
                "GLLLLLLLG", "GLLLLLLLG", "GLLLLLLLG", "GGGGGGGGG", "BBBBBBBBB",
            };
            var t = new Texture2D(9, 12, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 9; x++)
                {
                    char ch = rows[y][x];
                    Color32 c = ch switch
                    {
                        'G' => new Color32(80, 80, 85, 255), 'L' => new Color32(140, 140, 145, 255), 'D' => new Color32(60, 60, 65, 255),
                        'B' => new Color32(70, 50, 30, 255), _ => new Color32(0, 0, 0, 0),
                    };
                    t.SetPixel(x, 11 - y, c);
                }
            t.Apply();
            return t;
        }
    }

    /// <summary>
    ///  REVANCHA en partida local o práctica: cierra la sesión de red, espera a
    ///  que se apague del todo y arranca otra igual (mismas reglas, mapa nuevo).
    ///  Vive en un objeto propio que sobrevive al cambio de escena.
    /// </summary>
    public class RematchRunner : MonoBehaviour
    {
        public static void Begin(MatchMode mode, string gameScene)
        {
            var go = new GameObject("[Revancha]");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<RematchRunner>();
            runner.StartCoroutine(runner.Run(mode, gameScene));
        }

        private IEnumerator Run(MatchMode mode, string gameScene)
        {
            var nm = NetworkManager.Singleton;
            LobbySync.SayGoodbye();
            if (nm != null && nm.IsListening) nm.Shutdown();
            float t = 0f;
            while (nm != null && (nm.IsListening || nm.ShutdownInProgress) && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            // La práctica anterior se cierra sola al ver la red apagada (mira cada 0.5 s).
            while (PracticeSession.Active && t < 6f) { t += Time.unscaledDeltaTime; yield return null; }
            // Pasamos un instante por el MENÚ y arrancamos desde allí, igual que una
            // partida normal: si se rearranca el host dentro de la escena de juego,
            // Netcode se queda con objetos de red ya destruidos en su lista
            // (NullReference en cada fotograma).
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            yield return null;
            while (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu") yield return null;
            yield return null;
            yield return null;
            MatchStats.Last = null;
            if (mode == MatchMode.Practice) PracticeSession.Begin(gameScene);
            else if (!LocalMatchSession.Begin(gameScene)) TurnHUD.ExitToMenu();
            Destroy(gameObject);
        }
    }
}
