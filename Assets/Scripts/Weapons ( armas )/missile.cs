using UnityEngine;
using Ratms.Networking;

/// <summary>
///  Misil en SINGLE-PLAYER (legacy).
///
///  En multijugador esto lo sustituye NetworkMissile.cs — este componente
///  solo sigue aquí para que las escenas viejas no se rompan. Cuando colisiona,
///  hace daño y muestra el popup nuevo (WorldDamagePopup) — ya no usa el
///  antiguo DamagePopup que dejaba la "T" parásita.
/// </summary>
public class missile : MonoBehaviour
{
    [SerializeField] private float damage = 50f;

    private GameObject owner;
    private Collider2D myCol;
    private Collider2D ownerCol;

    public void SetOwner(GameObject ownerObj)
    {
        owner = ownerObj;
        myCol = GetComponent<Collider2D>();
        ownerCol = owner.GetComponent<Collider2D>();

        if (myCol != null && ownerCol != null)
        {
            Physics2D.IgnoreCollision(myCol, ownerCol, true);
            Invoke(nameof(ReenableCollisionWithOwner), 0.5f);
        }
    }

    void ReenableCollisionWithOwner()
    {
        if (myCol != null && ownerCol != null)
        {
            Physics2D.IgnoreCollision(myCol, ownerCol, false);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject == owner) return;

        Health health = collision.collider.GetComponent<Health>();
        if (health != null)
        {
            health.TakeDamage(damage);

            // Popup en el punto exacto del impacto
            Vector3 hitPoint = collision.contacts.Length > 0
                ? (Vector3)collision.contacts[0].point
                : transform.position;
            WorldDamagePopup.Spawn(hitPoint, Mathf.RoundToInt(damage), DamageType.Explosion);
        }

        Destroy(gameObject);
    }
}
