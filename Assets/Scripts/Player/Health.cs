using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    [Header("Feedback de daño")]
    [SerializeField] private GameObject damagePopupPrefab;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        Debug.Log($"{gameObject.name} recibió {amount} de daño. Vida restante: {currentHealth}");

        ShowDamagePopup(amount);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    void ShowDamagePopup(float amount)
    {
        if (damagePopupPrefab != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 1.5f;
            GameObject popup = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity);

            DamagePopup damageScript = popup.GetComponent<DamagePopup>();
            if (damageScript != null)
            {
                damageScript.Setup(amount);
            }
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} ha muerto.");
        gameObject.SetActive(false);
    }
}
