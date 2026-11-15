using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ESPECTÁCULO (0.27) — la parte de red:
    ///   • Burlas (tecla P) y frases al empezar el turno, con la voz de cada rata.
    ///   • Tormenta: al acabar algunos turnos cae un RAYO de verdad (hace daño y cráter).
    /// </summary>
    public partial class NetworkTurnManager
    {
        partial void SpectacleOnSpawn()
        {
            if (GetComponent<TauntInput>() == null) gameObject.AddComponent<TauntInput>();
            if (GetComponent<TurnMarker>() == null) gameObject.AddComponent<TurnMarker>();   // 0.29: flecha del turno
            WeatherFx.Ensure();
            DeathReplay.Ensure();   // 0.30: graba los últimos segundos para la repetición
        }

        [ServerRpc(RequireOwnership = false)]
        public void TauntServerRpc(ulong ratId, ServerRpcParams rpc = default)
        {
            var rat = CurrentRat;
            if (rat == null || rat.NetworkObjectId != ratId || rat.OwnerClientId != rpc.Receive.SenderClientId) return;
            SayClientRpc(ratId, Random.Range(0, RatLines.Taunts.Length), true);
        }

        /// <summary>Una rata dice algo (código: &lt;100 burla, ≥100 frase de turno).</summary>
        [ClientRpc]
        private void SayClientRpc(ulong ratId, int code, bool hop)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(ratId, out var no)) return;
            // 0.33: cada rata con SU voz (RatVoice: tono, garganta, aspereza... según su aspecto).
            RatVoice.Say(no, RatVoice.Kind.Talk, 0.95f);
            if (PlayerPrefs.GetInt("opt_laughs", 1) == 1)
                SpeechBubbles.Say(no.transform, Ratms.UI.Loc.T(code >= 100 ? RatLines.TurnStart[(code - 100) % RatLines.TurnStart.Length] : RatLines.Taunts[code % RatLines.Taunts.Length]));
            if (hop && no.IsOwner)
            {
                var rb = no.GetComponent<Rigidbody2D>();
                if (rb != null && Mathf.Abs(rb.linearVelocity.y) < 0.5f) rb.linearVelocity += Vector2.up * 2.6f;
            }
        }

        /// <summary>(Servidor, al empezar un turno) a veces la rata suelta una frase.</summary>
        private bool weatherAnnounced;

        partial void SpectacleOnTurnBegin()
        {
            // 0.30: al empezar la partida se avisa de qué hace el clima
            if (!weatherAnnounced && CurrentRatId.Value != ulong.MaxValue)
            {
                weatherAnnounced = true;
                string w = WeatherRules.Describe(WeatherFx.Current);
                if (w != null) AnnounceClientRpc(w, new Color(0.7f, 0.85f, 1f));
            }
            if (CurrentRatId.Value != ulong.MaxValue && Random.value < 0.3f)
                SayClientRpc(CurrentRatId.Value, 100 + Random.Range(0, RatLines.TurnStart.Length), false);
        }

        /// <summary>(Solo PRUEBAS, servidor) fuerza un rayo de tormenta ahora.</summary>
        public void TestLightningServer() { var w = WeatherFx.Current; WeatherFx.Current = Weather.Tormenta; forceBolt = true; SpectacleOnTurnEnd(); WeatherFx.Current = w; }
        private bool forceBolt;

        /// <summary>(Servidor, al acabar un turno) en TORMENTA, a veces cae un rayo.</summary>
        private void SpectacleOnTurnEnd()
        {
            bool force = forceBolt; forceBolt = false;
            if (WeatherFx.Current != Weather.Tormenta || (!force && Random.value > 0.4f)) return;
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            if (gen == null || terrain == null) return;
            // Mitad de las veces, cerca de una rata (que dé miedo de verdad)
            Vector2 at;
            var rats = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None);
            if (rats.Length > 0 && Random.value < 0.5f)
            {
                var r = rats[Random.Range(0, rats.Length)];
                var s = gen.GetSurfaceWorldPos(r.transform.position.x + Random.Range(-2f, 2f));
                at = s.HasValue ? (Vector2)s.Value : (Vector2)r.transform.position;
            }
            else
            {
                float x = terrain.transform.position.x + Random.Range(0.05f, 0.95f) * terrain.WorldSize.x;
                var s = gen.GetSurfaceWorldPos(x);
                if (!s.HasValue) return;
                at = s.Value;
            }
            var ndt = NetworkDestructibleTerrain.Instance;
            if (ndt != null) ndt.ServerCarveAt(at, 1.2f);
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
            {
                if (h == null || h.IsDead.Value) continue;
                float d = h.BlastDistance(at);
                if (d > 2.2f) continue;
                int dmg = Mathf.CeilToInt(22f * (1f - d / 2.2f)) + 3;
                h.ApplyDamageServer(dmg, h.BodyCenter, DamageType.Explosion, at);
                h.ApplyKnockbackClientRpc(((h.BodyCenter - at).normalized + Vector2.up) * 3f, 0.3f);
            }
            LightningClientRpc(at);
        }

        [ClientRpc]
        private void LightningClientRpc(Vector2 at)
        {
            WeatherFx.Flash();
            Fx.Explosion(at, 1.2f);
            LightningBolt.Spawn(at);
            Sfx.Play(Sfx.Id.Thunder, 1f, 0.1f);
            CameraFollow.Instance?.Shake(0.3f, 0.4f);
        }
    }
}
