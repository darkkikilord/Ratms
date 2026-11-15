using Ratms.UI;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  MODOS DE JUEGO (0.25) — la parte de la partida.
    ///   • Fuertes: lo hacen el terreno (TerrainGenerator.FortShape) y las
    ///     apariciones (cada equipo en su mitad).
    ///   • Colina: bandera en el centro; quien ACABA su turno con una rata
    ///     junto a ella suma un punto. Gana quien llegue a <see cref="HillTarget"/>.
    ///   • Duelo: una rata por equipo y, cada turno, un arma al azar (la única con munición).
    ///   • Minas: al empezar, el mapa se siembra de minas.
    ///   • Barra libre: munición infinita (MatchRules.DefaultAmmo).
    /// </summary>
    public partial class NetworkTurnManager
    {
        public const float HillRadius = 2.6f;
        public const int HillTarget = 5;

        /// <summary>Modo de esta partida (lo ven todos).</summary>
        public readonly NetworkVariable<byte> GameModeNet = new(0);
        public GameMode CurrentGame => (GameMode)GameModeNet.Value;
        /// <summary>Colina: dónde está la bandera.</summary>
        public readonly NetworkVariable<Vector2> HillPos = new(Vector2.zero);
        /// <summary>Colina: puntos por equipo (índice = TeamIndex).</summary>
        public NetworkList<int> HillScore;

        private static readonly WeaponsMenuUI.WeaponId[] DuelPool =
        {
            WeaponsMenuUI.WeaponId.Bazooka, WeaponsMenuUI.WeaponId.Grenade, WeaponsMenuUI.WeaponId.Dynamite,
            WeaponsMenuUI.WeaponId.Shotgun, WeaponsMenuUI.WeaponId.ClusterBomb, WeaponsMenuUI.WeaponId.AirStrike,
            WeaponsMenuUI.WeaponId.Napalm, WeaponsMenuUI.WeaponId.Flamethrower, WeaponsMenuUI.WeaponId.MachineGun,
            WeaponsMenuUI.WeaponId.Sniper, WeaponsMenuUI.WeaponId.Bow, WeaponsMenuUI.WeaponId.Mortar,
            WeaponsMenuUI.WeaponId.Pigeon, WeaponsMenuUI.WeaponId.RatBomb, WeaponsMenuUI.WeaponId.Bat,
            WeaponsMenuUI.WeaponId.Bayonet, WeaponsMenuUI.WeaponId.GasCan, WeaponsMenuUI.WeaponId.HolyCheese,
        };

        partial void ModesAwake() => HillScore = new NetworkList<int>();

        partial void ModesOnSpawn()
        {
            if (GetComponent<ModeVisuals>() == null) gameObject.AddComponent<ModeVisuals>();
            if (IsServer) GameModeNet.Value = (byte)MatchRules.EffectiveGame;
        }

        partial void ModesOnGameStart()
        {
            HillScore.Clear();
            for (int i = 0; i < MatchRules.MaxTeams; i++) HillScore.Add(0);
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            switch (CurrentGame)
            {
                case GameMode.Colina:
                    if (gen != null && terrain != null)
                    {
                        float cx = terrain.transform.position.x + terrain.WorldSize.x * 0.5f;
                        var s = gen.GetSurfaceWorldPos(cx, 60);
                        HillPos.Value = s.HasValue ? (Vector2)s.Value : new Vector2(cx, terrain.transform.position.y + terrain.WorldSize.y * 0.5f);
                    }
                    AnnounceClientRpc(Loc.Pack("¡REY DE LA COLINA!\nAcaba tu turno junto a la bandera ({0} puntos para ganar)", HillTarget), new Color(1f, 0.85f, 0.3f));
                    break;
                case GameMode.Minas:
                    PlantMinesServer(gen, terrain);
                    AnnounceClientRpc("¡CAMPO DE MINAS!\nPisa con cuidado", new Color(1f, 0.55f, 0.3f));
                    break;
                case GameMode.Duelo:
                    AnnounceClientRpc("¡DUELO!\nUn arma al azar en cada turno", new Color(1f, 0.85f, 0.3f));
                    break;
                case GameMode.Fuertes:
                    AnnounceClientRpc("¡FUERTES!\nCada equipo en su castillo", new Color(1f, 0.85f, 0.3f));
                    break;
                case GameMode.BarraLibre:
                    AnnounceClientRpc("¡BARRA LIBRE!\nMunición infinita", new Color(1f, 0.85f, 0.3f));
                    break;
            }
        }

        private void PlantMinesServer(TerrainGenerator gen, DestructibleTerrain terrain)
        {
            if (gen == null || terrain == null) return;
            var avoid = new System.Collections.Generic.List<Vector3>();
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None)) avoid.Add(m.transform.position);
            int count = Mathf.Clamp(Mathf.RoundToInt(terrain.WorldSize.x / 3.2f), 6, 18);
            for (int i = 0; i < count; i++)
            {
                var p = gen.GetRandomValidSpawnWorld(avoid, 2.6f);
                if (!p.HasValue) break;
                avoid.Add(p.Value);
                NetworkTrenchArsenal.SpawnLooseServer(NetworkGadget.Kind.Mine, (Vector2)p.Value + Vector2.up * 0.25f);
            }
        }

        partial void ModesOnTurnBegin()
        {
            if (CurrentGame != GameMode.Duelo || CurrentTeam.Value < 0) return;
            int team = CurrentTeam.Value;
            var pick = DuelPool[Random.Range(0, DuelPool.Length)];
            if (pick == WeaponsMenuUI.WeaponId.HolyCheese && Random.value < 0.6f) pick = WeaponsMenuUI.WeaponId.Bazooka;   // rarita
            for (int w = 0; w < WeaponCount; w++)
            {
                var id = (WeaponsMenuUI.WeaponId)w;
                bool movement = id == WeaponsMenuUI.WeaponId.Rope || id == WeaponsMenuUI.WeaponId.Shovel;
                SetAmmoServer(team, id, id == pick ? 1 : movement ? -1 : 0);
            }
            AnnounceClientRpc(Loc.Pack("Arma del duelo: {0}", WeaponsMenuUI.ShortNameEs(pick)), new Color(1f, 0.85f, 0.3f));
            DuelWeaponClientRpc((int)pick, CurrentRatId.Value);
        }

        private void SetAmmoServer(int team, WeaponsMenuUI.WeaponId w, int n)
        {
            if (team < 0 || w == WeaponsMenuUI.WeaponId.None) return;
            int i = team * WeaponCount + (int)w;
            while (Ammo.Count <= i) Ammo.Add(MatchRules.DefaultAmmo((WeaponsMenuUI.WeaponId)(Ammo.Count % WeaponCount)));
            Ammo[i] = n;
        }

        /// <summary>Duelo: el dueño de la rata del turno se pone el arma sorteada en la mano.</summary>
        [ClientRpc]
        private void DuelWeaponClientRpc(int weapon, ulong ratId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(ratId, out var no) || !no.IsOwner) return;
            var menu = no.GetComponentInChildren<WeaponsMenuUI>();
            if (menu != null) menu.SelectedIndexNet.Value = weapon;
        }

        /// <summary>Colina: al acabar el turno de un equipo, punto si tiene una rata junto a la bandera.</summary>
        private bool ModesOnTurnEnd(int teamIndex)
        {
            if (CurrentGame != GameMode.Colina || teamIndex < 0 || GameEnded.Value) return false;
            var team = teams.Find(t => t.index == teamIndex);
            if (team == null || !TeamAlive(team)) return false;
            bool onHill = false;
            foreach (var r in team.rats)
            {
                if (!IsAlive(r)) continue;
                var h = r.GetComponent<NetworkHealth>();
                if (h != null && Vector2.Distance(h.BodyCenter, HillPos.Value) <= HillRadius) { onHill = true; break; }
            }
            if (!onHill) return false;
            while (HillScore.Count <= teamIndex) HillScore.Add(0);
            HillScore[teamIndex] = HillScore[teamIndex] + 1;
            int pts = HillScore[teamIndex];
            AnnounceClientRpc(Loc.Pack("¡{0} domina la colina! ({1}/{2})", TeamDisplayName(teamIndex, translate: false), pts, HillTarget), new Color(1f, 0.85f, 0.3f));
            if (pts >= HillTarget) { EndWithWinnerServer(team); return true; }
            return false;
        }

        /// <summary>Fin de partida con un ganador concreto (lo usa la Colina).</summary>
        private void EndWithWinnerServer(Team t)
        {
            if (GameEnded.Value) return;
            CurrentTurnClientId.Value = ulong.MaxValue;
            CurrentRatId.Value = ulong.MaxValue;
            CurrentTeam.Value = -1;
            TurnTimeRemaining.Value = 0f;
            InTransition.Value = false;
            gameStarted = false;
            WinnerTeam.Value = t.index;
            WinnerClientId.Value = t.owner;
            StatsSendServer();
            GameEnded.Value = true;
        }
    }

    /// <summary>
    ///  Lo que se VE de los modos en cada PC: la bandera y su círculo en la
    ///  Colina, el marcador y una etiqueta con el modo.
    /// </summary>
    public class ModeVisuals : MonoBehaviour
    {
        private NetworkTurnManager tm;
        private GameObject flag;
        private SpriteRenderer cloth;
        private LineRenderer ring;
        private GUIStyle style;

        private void Awake() => tm = GetComponent<NetworkTurnManager>();

        private void Update()
        {
            if (tm == null || !tm.IsSpawned) return;
            bool hill = tm.CurrentGame == GameMode.Colina && tm.HillPos.Value != Vector2.zero;
            if (hill && flag == null) BuildFlag();
            if (flag != null)
            {
                flag.SetActive(hill);
                if (!hill) return;
                flag.transform.position = tm.HillPos.Value;
                // La tela ondea y se pinta del color del equipo que va ganando
                int lead = -1, best = 0;
                for (int i = 0; i < tm.HillScore.Count; i++) if (tm.HillScore[i] > best) { best = tm.HillScore[i]; lead = i; }
                cloth.color = lead >= 0 ? (Color)RatAppearance.ColorForTeam(lead) : new Color(0.95f, 0.92f, 0.85f);
                cloth.transform.localScale = new Vector3(1f + 0.08f * Mathf.Sin(Time.time * 6f), 1f, 1f);
            }
        }

        private void BuildFlag()
        {
            flag = new GameObject("BanderaColina");
            // Mástil
            var pole = new GameObject("Mastil").AddComponent<SpriteRenderer>();
            pole.transform.SetParent(flag.transform, false);
            pole.sprite = Solid(new Color32(90, 64, 40, 255));
            pole.transform.localScale = new Vector3(0.08f, 1.6f, 1f);
            pole.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            pole.sortingOrder = 30;
            // Tela
            cloth = new GameObject("Tela").AddComponent<SpriteRenderer>();
            cloth.transform.SetParent(flag.transform, false);
            cloth.sprite = Solid(Color.white, new Vector2(0f, 0.5f));
            cloth.transform.localPosition = new Vector3(0.04f, 1.38f, 0f);
            cloth.transform.localScale = new Vector3(0.7f, 0.42f, 1f);
            cloth.sortingOrder = 31;
            // Círculo de la zona
            ring = flag.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 48;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * NetworkTurnManager.HillRadius);
            }
            ring.widthMultiplier = 0.06f;
            ring.material = new Material(Shader.Find("Sprites/Default"));
            ring.startColor = ring.endColor = new Color(1f, 0.85f, 0.3f, 0.45f);
            ring.sortingOrder = 29;
        }

        private static Sprite Solid(Color32 c, Vector2? pivot = null)
        {
            var t = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            t.SetPixel(0, 0, c); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 1, 1), pivot ?? new Vector2(0.5f, 0.5f), 1f);
        }

        private void OnGUI()
        {
            if (tm == null || !tm.IsSpawned || tm.CurrentGame == GameMode.Normal || tm.GameEnded.Value) return;
            TrenchGUI.Begin();
            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperLeft, font = TrenchGUI.Typewriter, normal = { textColor = new Color(1f, 0.85f, 0.45f) } };
            string text = Loc.T("Modo") + ": " + Loc.T(MatchRules.GameName(tm.CurrentGame));
            if (tm.CurrentGame == GameMode.Colina)
            {
                var sb = new System.Text.StringBuilder(text).Append("   ·   ");
                for (int i = 0; i < tm.HillScore.Count; i++)
                {
                    if (i >= tm.TeamNames.Count) break;
                    sb.Append(tm.TeamDisplayName(i)).Append(' ').Append(tm.HillScore[i]).Append("   ");
                }
                sb.Append(Loc.F("(meta {0})", NetworkTurnManager.HillTarget));
                text = sb.ToString();
            }
            GUI.Label(new Rect(14f, 34f, 900f, 30f), text, style);
            TrenchGUI.End();
        }
    }
}
