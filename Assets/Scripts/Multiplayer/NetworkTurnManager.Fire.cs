using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Parte del TurnManager que reparte el FUEGO por la red (0.18).
    ///  Ver <see cref="FireSystem"/>: el servidor decide dónde nace cada llama
    ///  y aquí se la manda a todos; cada PC la simula y la dibuja.
    /// </summary>
    public partial class NetworkTurnManager
    {
        /// <summary>Nacen llamas en todos los clientes (posición, velocidad y vida de cada una).</summary>
        [ClientRpc]
        public void IgniteClientRpc(Vector2[] pos, Vector2[] vel, float[] life)
        {
            if (pos == null || vel == null || life == null) return;
            int n = Mathf.Min(pos.Length, Mathf.Min(vel.Length, life.Length));
            for (int i = 0; i < n; i++) FireSystem.Spawn(pos[i], vel[i], life[i]);
        }

        /// <summary>Una rata se está quemando: humo, chispas y un "¡AY!" (local, en todos).</summary>
        [ClientRpc]
        public void BurningFxClientRpc(ulong ratId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) return;
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(ratId, out var no) || no == null) return;
            var h = no.GetComponent<NetworkHealth>();
            if (h == null) return;
            Vector2 c = h.BodyCenter;
            Fx.Sparks(c + Vector2.up * 0.2f, 4, new Color(1f, 0.6f, 0.15f, 1f), 1f, 2.5f);
            Fx.TrailPuff(c + Vector2.up * 0.35f);
            var look = no.GetComponent<RatAppearance>();
            if (look != null) look.HitFlash();
        }

        /// <summary>
        ///  (Servidor) Una llama está en <paramref name="at"/>: si toca un barril,
        ///  el barril revienta (y suelta más fuego: reacción en cadena).
        /// </summary>
        public void ServerFireTouches(Vector2 at)
        {
            if (!IsServer) return;
            foreach (var it in items.Values)
            {
                if (it.gone || it.kind != ItemKind.Barrel) continue;
                Vector2 c = it.pos + Vector2.up * 0.2f;
                if (Mathf.Abs(at.x - c.x) > 0.35f || Mathf.Abs(at.y - c.y) > 0.45f) continue;
                it.gone = true;
                pendingBlasts.Add((Time.time + 0.4f, c));
                RemoveItemClientRpc(it.id, "", Color.white);
                return;   // RemoveItemClientRpc toca la colección en el host: salimos ya
            }
        }
    }
}
