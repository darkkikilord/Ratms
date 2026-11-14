using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Parte del TurnManager para el ARSENAL DE TRINCHERA (0.21): el veneno
    ///  que hace efecto al empezar cada turno, el reparto de las nubes de gas
    ///  y el "queso apestoso" que atrae a las ratas.
    /// </summary>
    public partial class NetworkTurnManager
    {
        /// <summary>(Servidor) Al empezar el turno de un equipo, sus ratas envenenadas pierden vida.</summary>
        partial void PoisonOnTurnBegin()
        {
            foreach (var t in teams)
            {
                if (t.index != CurrentTeam.Value) continue;
                foreach (var r in t.rats)
                    if (r != null && r.TryGetComponent<NetworkHealth>(out var h) && h.Poisoned)
                        h.PoisonTickServer();
            }
        }

        [ClientRpc]
        public void GasClientRpc(Vector2[] pos, Vector2[] vel)
        {
            if (pos == null || vel == null) return;
            int n = Mathf.Min(pos.Length, vel.Length);
            for (int i = 0; i < n; i++) GasSystem.Spawn(pos[i], vel[i], Random.Range(7f, 10f), Random.Range(0.8f, 1.15f));
            Sfx.Play(Sfx.Id.Flame, 0.5f);
        }

        /// <summary>
        ///  (Servidor) QUESO APESTOSO: durante <paramref name="seconds"/> s, las
        ///  ratas a menos de <paramref name="radius"/> de <paramref name="at"/>
        ///  corren hacia él sin poder evitarlo (lo aplica el dueño de cada rata).
        /// </summary>
        public void ServerLure(Vector2 at, float radius, float seconds)
        {
            if (!IsServer) return;
            // Todas las ratas del mapa (también las que no son de ningún equipo, como las dianas).
            foreach (var r in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.IsSpawned) continue;
                    if (!r.TryGetComponent<NetworkHealth>(out var h) || h.IsDead.Value) continue;
                    if (Vector2.Distance(h.BodyCenter, at) > radius) continue;
                    r.LureClientRpc(at, seconds);
                }
        }
    }
}
