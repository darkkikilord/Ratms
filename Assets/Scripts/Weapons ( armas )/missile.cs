using UnityEngine;

public class missile : MonoBehaviour
{
    [SerializeField] private float damage = 50f;
    [SerializeField] private GameObject damagePopupPrefab;

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
            ShowDamagePopup(collision.contacts[0].point);
        }

        Destroy(gameObject);
    }

    void ShowDamagePopup(Vector3 position)
    {
        if (damagePopupPrefab == null) return;

        // 👇 Forzamos Z = -4 para que se vea bien
        Vector3 spawnPosition = new Vector3(position.x, position.y, -4f);

        GameObject popup = Instantiate(damagePopupPrefab, spawnPosition, Quaternion.identity);

        // 🔥 Buscar canvas dinámicamente por tag
        GameObject canvasGO = GameObject.FindWithTag("WorldCanvas");
        if (canvasGO != null)
        {
            popup.transform.SetParent(canvasGO.transform, worldPositionStays: true);
        }
        else
        {
            Debug.LogWarning("⚠️ No se encontró ningún objeto con el tag 'WorldCanvas'. Asegúrate de asignarlo.");
        }

        var damageScript = popup.GetComponent<DamagePopup>();
        if (damageScript != null)
        {
            damageScript.Setup(damage);
        }
    }
}
