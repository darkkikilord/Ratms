using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float moveForce = 30f;
    [SerializeField] private float maxSpeed = 10f;
    [SerializeField] private float linearDrag = 4f;

    [Header("Salto")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.1f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Arma")]
    [SerializeField] private WeaponBase equippedWeapon;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject bazookaPrefab;
    [SerializeField] private Transform weaponHolder;

    [Header("Apuntado")]
    [SerializeField] private float angleStep = 2f;
    [SerializeField] private float maxForce = 15f;
    [SerializeField] private float chargeRate = 10f;
    [SerializeField] private LineRenderer aimLine;

    private Rigidbody2D rb;
    private Animator animator;
    private float moveX;
    private bool isGrounded;
    private bool facingRight = true;
    private GameObject currentWeaponGO;
    private TurnManager turnManager;
    private bool hasFired = false;

    private float aimAngle = 45f;
    private float currentCharge = 0f;
    private bool charging = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        rb.gravityScale = 1f;
        rb.linearDamping = linearDrag;
        turnManager = FindObjectOfType<TurnManager>();
    }

    void Update()
    {
        if (!enabled) return;

        moveX = Input.GetAxisRaw("Horizontal");
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (Input.GetKeyDown(KeyCode.W) && isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }

        animator.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));

        // Instanciar bazooka
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (currentWeaponGO != null) Destroy(currentWeaponGO);
            currentWeaponGO = Instantiate(bazookaPrefab, weaponHolder.position, Quaternion.identity, weaponHolder);
            equippedWeapon = currentWeaponGO.GetComponent<WeaponBase>();
        }

        // Apuntar con flechas
        if (Input.GetKey(KeyCode.UpArrow))
        {
            aimAngle += angleStep * Time.deltaTime * 60f;
            aimAngle = Mathf.Clamp(aimAngle, 0f, 90f);
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            aimAngle -= angleStep * Time.deltaTime * 60f;
            aimAngle = Mathf.Clamp(aimAngle, 0f, 90f);
        }

        if (equippedWeapon != null && firePoint != null)
            UpdateAimLine(currentCharge > 0f ? currentCharge : 1f);

        // Cargar fuerza
        if (Input.GetKeyDown(KeyCode.Space) && equippedWeapon != null && !hasFired)
        {
            charging = true;
            currentCharge = 0f;
        }

        if (charging && Input.GetKey(KeyCode.Space))
        {
            currentCharge += chargeRate * Time.deltaTime;
            currentCharge = Mathf.Clamp(currentCharge, 0f, maxForce);
            UpdateAimLine(currentCharge);
        }

        if (charging && Input.GetKeyUp(KeyCode.Space) && equippedWeapon != null && firePoint != null)
        {
            charging = false;
            hasFired = true;

            Vector2 direction = Quaternion.Euler(0f, 0f, facingRight ? aimAngle : (180f - aimAngle)) * Vector2.right;
            equippedWeapon.Fire(firePoint, direction.normalized * currentCharge);

            if (aimLine != null)
                aimLine.enabled = false;

            if (turnManager != null)
                Invoke(nameof(FinishTurn), 0.5f);
        }

        if (moveX > 0 && !facingRight) Flip();
        else if (moveX < 0 && facingRight) Flip();
    }

    void FixedUpdate()
    {
        if (!enabled) return;

        if (Mathf.Abs(rb.linearVelocity.x) < maxSpeed || Mathf.Sign(moveX) != Mathf.Sign(rb.linearVelocity.x))
        {
            rb.AddForce(new Vector2(moveX * moveForce, 0f), ForceMode2D.Force);
        }

        if (moveX == 0)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x *= -1;
        transform.localScale = scale;
    }

    void FinishTurn()
    {
        hasFired = false;
        charging = false;
        currentCharge = 0f;

        if (turnManager != null)
            turnManager.EndTurn();
    }

    public void ResetTurn()
    {
        hasFired = false;
        charging = false;
        currentCharge = 0f;

        if (aimLine != null)
            aimLine.enabled = true;
    }

    void UpdateAimLine(float force = 1f)
    {
        if (aimLine == null || firePoint == null) return;

        Vector2 direction = Quaternion.Euler(0f, 0f, facingRight ? aimAngle : (180f - aimAngle)) * Vector2.right;
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, firePoint.position);
        aimLine.SetPosition(1, firePoint.position + (Vector3)(direction.normalized * force));
        aimLine.enabled = true;
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
