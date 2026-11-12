using UnityEngine;
using Ratms.Networking;

/// <summary>
///  Vida en SINGLE-PLAYER (legacy).
///
///  En multijugador la vida la lleva NetworkHealth.cs — este componente
///  solo sigue aquí para que las viejas escenas single-player no se rompan.
///  Cuando recibe daño, muestra un WorldDamagePopup (mismo sistema que el
///  multi) para que no aparezca la "T" del antiguo DamagePopup en Canvas.
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        Debug.Log($"{gameObject.name} recibió {amount} de daño. Vida restante: {currentHealth}");

        // Popup flotante (mismo sistema que el multijugador, sin Canvas).
        WorldDamagePopup.Spawn(
            transform.position + Vector3.up * 0.5f,
            Mathf.RoundToInt(amount),
            DamageType.Generic);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} ha muerto.");
        gameObject.SetActive(false);
    }
}
