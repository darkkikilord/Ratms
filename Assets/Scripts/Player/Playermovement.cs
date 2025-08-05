using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    /*──────── PUBLIC ───────────────*/
    [Header("Referencias")]
    public Transform weaponHolder;      // mano de la rata
    public GameObject bazookaPrefab;     // bazooka (con firePoint + TrajectoryLine)
    public SpriteRenderer bodySprite;        // sprite principal

    [Header("Movimiento")]
    public float moveForce = 20f;
    public float maxSpeed = 6f;
    public float linearDrag = 4f;

    [Header("Salto (W)")]
    public float jumpForce = 10f;
    public Transform groundCheck;
    public float groundRadius = 0.1f;
    public LayerMask groundMask;

    [Header("Disparo")]
    public float chargeRate = 10f;  // fuerza/segundo
    public float maxForce = 15f;  // fuerza máx
    public float angleStep = 200f; // °/seg ↑ ↓


    /*──────── PRIVATE ──────────────*/
    Rigidbody2D rb;
    WeaponBase weapon;
    TrajectoryLine traj;
    Transform firePoint;

    bool myTurn;
    bool charging;
    float force;
    float fullAngle = 45f;           // 0‑360°
    float inputX;
    bool facingRight = true;

    Vector3 holderDefaultPos;         // ← posición local original


    /*──────── MONO ─────────────────*/
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        holderDefaultPos = weaponHolder.localPosition;     // guarda ref
    }

    void Start()
    {
        // aplica volteo inicial si el sprite viene flipX
        SetFacing(!bodySprite.flipX);
    }

    void Update()
    {
        if (!myTurn) return;

        /*— Movimiento —*/
        inputX = Input.GetAxisRaw("Horizontal");

        /*— Salto —*/
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundRadius, groundMask);
        if (grounded && Input.GetKeyDown(KeyCode.W))
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);

        /*— Apuntado ↑ ↓ —*/
        float v = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(v) > .1f)
        {
            fullAngle = (fullAngle + v * angleStep * Time.deltaTime + 360f) % 360f;
            ApplyAimRotation();
        }

        /*— Flip + espejo —*/
        if (inputX > 0 && !facingRight) SetFacing(true);
        if (inputX < 0 && facingRight) SetFacing(false);

        /*— Equipar bazooka (1) —*/
        if (Input.GetKeyDown(KeyCode.Alpha1))
            EquipWeapon(bazookaPrefab);

        /*— Cargar / disparar (ESPACIO) —*/
        if (weapon != null && Input.GetKeyDown(KeyCode.Space))
        { charging = true; force = 0f; }

        if (charging)
        {
            force += chargeRate * Time.deltaTime;
            force = Mathf.Min(force, maxForce);
        }

        if (charging && Input.GetKeyUp(KeyCode.Space))
        {
            charging = false;
            weapon.Fire(firePoint, weaponHolder.right * force);
            Object.FindFirstObjectByType<TurnManager>()?
                  .Invoke(nameof(TurnManager.EndTurn), .6f);
        }

        /*— Trayectoria —*/
        if (weapon != null && traj != null)
            traj.Show(charging ? force : 1f, weaponHolder.right);
    }

    void FixedUpdate()
    {
        if (!myTurn) return;

        if (Mathf.Abs(inputX) > .1f)
            rb.AddForce(Vector2.right * inputX * moveForce);

        if (Mathf.Abs(rb.linearVelocity.x) > maxSpeed)
            rb.linearVelocity = new Vector2(Mathf.Sign(rb.linearVelocity.x) * maxSpeed, rb.linearVelocity.y);

        rb.linearDamping = linearDrag;
    }

    /*──────── TURNOS — API ─────────*/
    public void SetTurnActive(bool active)
    {
        myTurn = active;
        enabled = active;
        if (!active && traj != null) traj.Hide();
    }

    public void ResetTurn()
    {
        charging = false;
        force = 0f;
        fullAngle = 45f;
        ApplyAimRotation();

        if (traj != null)
            traj.Show(1f, weaponHolder.right);
    }

    /*──────── HELPERS ──────────────*/
    void SetFacing(bool right)
    {
        facingRight = right;
        bodySprite.flipX = !right;

        // 1) rota 0 / 180° en Y
        Vector3 rot = weaponHolder.localEulerAngles;
        rot.y = right ? 0f : 180f;
        weaponHolder.localEulerAngles = rot;

        // 2) refleja posición local en X
        Vector3 pos = holderDefaultPos;
        pos.x *= right ? 1f : -1f;
        weaponHolder.localPosition = pos;
    }

    void ApplyAimRotation()
    {
        float y = weaponHolder.localEulerAngles.y;               // 0 ó 180
        weaponHolder.localEulerAngles = new Vector3(0f, y, fullAngle);
    }

    void EquipWeapon(GameObject prefab)
    {
        if (prefab == null) return;

        if (weapon != null) Destroy(weapon.gameObject);

        GameObject go = Instantiate(prefab, weaponHolder);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        weapon = go.GetComponent<WeaponBase>();
        traj = go.GetComponentInChildren<TrajectoryLine>(true);
        firePoint = weapon.firePoint;

        ApplyAimRotation();                   // respeta ángulo actual
        if (traj != null)
            traj.Show(1f, weaponHolder.right);
    }
}
