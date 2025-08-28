using TMPro;
using UnityEngine;

public class DamagePopup : MonoBehaviour
{
    [SerializeField] private float floatSpeed = 1f;
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float fadeSpeed = 1.5f;

    private TextMeshProUGUI text;  // Usamos TextMeshProUGUI porque estamos en un Canvas
    private CanvasGroup canvasGroup;

    void Awake()
    {
        text = GetComponentInChildren<TextMeshProUGUI>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Setup(float damageAmount)
    {
        text.text = damageAmount.ToString();
        text.color = damageColor;
        canvasGroup.alpha = 1f;
    }

    void Update()
    {
        // Subir el texto poco a poco
        transform.Translate(Vector3.up * floatSpeed * Time.deltaTime);

        // Reducir la opacidad
        canvasGroup.alpha -= fadeSpeed * Time.deltaTime;

        // Destruir cuando est� completamente invisible
        if (canvasGroup.alpha <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
