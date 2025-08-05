using UnityEngine;

/// <summary>
///  Lanza un misil en la dirección y fuerza indicadas.
///  El prefab del misil debe tener Rigidbody2D (y opcionalmente un script missile).
/// </summary>
public class Bazooka : WeaponBase
{
    [Header("Disparo")]
    public GameObject projectilePrefab;   // ← ¡ahora sí existe!
    public float shootForce = 10f;

    /// <inheritdoc/>
    public override void Fire(Transform firePoint, Vector2 direction)
    {
        if (firePoint == null || projectilePrefab == null) return;

        // Instancia el misil en la boca del cañón
        GameObject proj = Instantiate(projectilePrefab,
                                      firePoint.position,
                                      firePoint.rotation);

        // Le aplica la fuerza
        Rigidbody2D rb = proj.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.AddForce(direction, ForceMode2D.Impulse);

        // Opcional: decirle al misil quién lo disparó
        missile m = proj.GetComponent<missile>();
        if (m != null)
            m.SetOwner(transform.root.gameObject);
    }
}
