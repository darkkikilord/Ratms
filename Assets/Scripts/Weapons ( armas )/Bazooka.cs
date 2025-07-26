using UnityEngine;

public class Bazooka : WeaponBase
{
    public GameObject projectilePrefab;
    public float shootForce = 10f;

    public override void Fire(Transform firePoint, Vector2 direction)
    {
        GameObject projectile = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.AddForce(direction * shootForce, ForceMode2D.Impulse);
        }

        // 🔁 Aseguramos que el misil ignore al jugador que lo disparó durante un corto periodo
        missile missileScript = projectile.GetComponent<missile>();
        if (missileScript != null)
        {
            missileScript.SetOwner(transform.root.gameObject); // 🔥 la rata que sostiene el arma
        }
    }
}
