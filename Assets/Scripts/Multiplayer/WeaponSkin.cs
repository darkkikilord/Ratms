using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Cambia el dibujo de las armas que van EN LA MANO por el pixel-art de
    ///  <see cref="WeaponArt"/> (bazuca de trinchera y granada de palo),
    ///  respetando dónde están en el prefab: la boca de la bazuca queda justo
    ///  en el Firepoint (por donde sale el misil).
    ///
    ///  Lo añade NetworkPlayerMovement al aparecer la rata. Solo visual.
    /// </summary>
    public class WeaponSkin : MonoBehaviour
    {
        private void Start()
        {
            var bazooka = FindDeep(transform, "BazookaVisual");
            var fire = FindDeep(transform, "Firepoint");
            if (bazooka != null && bazooka.TryGetComponent<SpriteRenderer>(out var bsr))
            {
                bsr.sprite = WeaponArt.Bazooka();
                // Escala para que la boca del cañón caiga en el Firepoint.
                float dist = fire != null ? Vector2.Distance(fire.position, bazooka.position) : 0.45f;
                float k = Mathf.Clamp(dist / (WeaponArt.BazookaMuzzlePx / 32f), 0.5f, 1.4f);
                SetWorldScale(bazooka, k);
            }

            var grenade = FindDeep(transform, "GranadeVisual");
            if (grenade != null && grenade.TryGetComponent<SpriteRenderer>(out var gsr))
            {
                gsr.sprite = WeaponArt.StickGrenade();
                SetWorldScale(grenade, 0.75f);
            }
        }

        /// <summary>Pone la escala local para que en el mundo mida <paramref name="k"/> (con 32 px/u).</summary>
        private static void SetWorldScale(Transform t, float k)
        {
            Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(k / Mathf.Max(0.0001f, Mathf.Abs(ps.x)), k / Mathf.Max(0.0001f, Mathf.Abs(ps.y)), 1f);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
