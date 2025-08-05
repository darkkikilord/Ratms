using UnityEngine;

/// <summary>
///  Clase base para cualquier arma.
///  Lleva un campo público <see cref="firePoint"/> que indica
///  desde dónde salen los proyectiles.
/// </summary>
public abstract class WeaponBase : MonoBehaviour
{
    [Header("Info del arma")]
    public string weaponName;
    public Sprite icon;

    [Header("Punto de disparo")]
    public Transform firePoint;   // ← ¡nuevo!

    /// <param name="firePoint">Transform que indica la boca del arma</param>
    /// <param name="direction">Dirección y magnitud de la fuerza</param>
    public abstract void Fire(Transform firePoint, Vector2 direction);
}
