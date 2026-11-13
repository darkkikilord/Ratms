using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Vida de la rata en MULTIJUGADOR.
    ///
    ///  Claves de diseño:
    ///   • El SERVIDOR es la única autoridad sobre la vida. Nadie más puede
    ///     bajársela (así nadie puede hacer trampa aunque modifique su cliente).
    ///   • <see cref="CurrentHealth"/> e <see cref="IsDead"/> son NetworkVariables:
    ///     el servidor las escribe, todos los clientes las leen y reaccionan.
    ///   • El feedback (popup de daño flotante + numerito de vida) lo gestionan
    ///     <see cref="WorldDamagePopup"/> y <see cref="RatHealthLabel"/>. Ambos
    ///     son scripts programáticos — NO necesitan prefab — así nos quitamos
    ///     de encima los problemas que dieron los TMP_UI dentro de Canvas.
    ///
    ///  Flujo normal de daño:
    ///   1. Un misil/granada (en el servidor) detecta colisión/explosión.
    ///   2. Llama a <see cref="ApplyDamageServer"/> con el tipo de daño.
    ///   3. El servidor actualiza CurrentHealth y dispara el popup en todos
    ///      los clientes vía <see cref="ShowDamagePopupClientRpc"/>.
    ///   4. Si la vida llega a 0, el servidor marca IsDead=true.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour
    {
        [Header("Vida")]
        [SerializeField] private int maxHealth = 100;

        [Header("Muerte")]
        [Tooltip("Si está ON, al morir desactiva el GameObject (como el single-player). Si está OFF, solo oculta visuales.")]
        [SerializeField] private bool disableOnDeath = true;

        // ==============================================================
        // Estado sincronizado
        // ==============================================================
        /// <summary>Vida actual. Solo escribible por el servidor.</summary>
        public readonly NetworkVariable<int> CurrentHealth = new(
            value: 100,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>true = rata KO. Al cambiar, todos los clientes la apagan.</summary>
        public readonly NetworkVariable<bool> IsDead = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Vida máxima de ESTA rata (regla de partida; la sabe todo el mundo).</summary>
        public readonly NetworkVariable<int> MaxHealthNet = new(0);

        public int MaxHealth => MaxHealthNet.Value > 0 ? MaxHealthNet.Value : maxHealth;

        // ==============================================================
        // MUTILACIONES (las decide el servidor; todos las ven igual)
        // ==============================================================
        /// <summary>
        ///  Lesiones de la rata, empaquetadas en un byte:
        ///  bits 0-1 = patas perdidas (0-2), bits 2-3 = brazos perdidos (0-2),
        ///  bit 4 = cabeza herida (tuerta), bit 5 = pata de palo (prótesis),
        ///  bit 6 = garfio (prótesis).
        /// </summary>
        public readonly NetworkVariable<byte> Injuries = new(0);

        public const int ZoneLegs = 0, ZoneArms = 1, ZoneHead = 2;

        public int LegsLost => Injuries.Value & 3;
        public int ArmsLost => (Injuries.Value >> 2) & 3;
        public bool HeadHurt => (Injuries.Value & 16) != 0;
        public bool HasPegLeg => (Injuries.Value & 32) != 0;
        public bool HasHook => (Injuries.Value & 64) != 0;

        /// <summary>Sin patas (y sin pata de palo): va arrastrándose y no salta.</summary>
        public bool Crawling => LegsLost >= 2 && !HasPegLeg;

        /// <summary>¿Puede sujetar armas? Sin brazos solo muerde (el garfio lo arregla a medias).</summary>
        public bool CanUseHands => ArmsLost < 2 || HasHook;

        /// <summary>Multiplicador de la velocidad al andar.</summary>
        public float SpeedFactor => LegsLost == 0 ? 1f : LegsLost == 1 ? (HasPegLeg ? 0.9f : 0.6f) : (HasPegLeg ? 0.55f : 0.35f);

        /// <summary>Multiplicador del salto (0 = no puede saltar).</summary>
        public float JumpFactor => LegsLost == 0 ? 1f : LegsLost == 1 ? (HasPegLeg ? 0.92f : 0.72f) : (HasPegLeg ? 0.6f : 0f);

        /// <summary>Grados que TIEMBLA la puntería (manca sin garfio, o tuerta).</summary>
        public float AimWobbleDeg =>
            (ArmsLost == 0 || (ArmsLost == 1 && HasHook) ? 0f : 7f) + (HeadHurt ? 5f : 0f);

        /// <summary>¿Le vendría bien una prótesis? (le falta algo sin arreglar)</summary>
        public bool NeedsProsthesis => (LegsLost > 0 && !HasPegLeg) || (ArmsLost > 0 && !HasHook);

        /// <summary>Texto corto de las lesiones (para el HUD). Vacío si está entera.</summary>
        public string InjuryText
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (LegsLost == 1) parts.Add(HasPegLeg ? "pata de palo" : "coja");
                else if (LegsLost >= 2) parts.Add(HasPegLeg ? "sin patas (pata de palo)" : "SIN PATAS");
                if (ArmsLost == 1) parts.Add(HasHook ? "garfio" : "manca");
                else if (ArmsLost >= 2) parts.Add(HasHook ? "sin brazos (garfio)" : "SIN BRAZOS");
                if (HeadHurt) parts.Add("tuerta");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>(Servidor) Arranca/rompe una parte del cuerpo. Devuelve false si ya no quedaba nada que romper ahí.</summary>
        public bool InjureServer(int zone)
        {
            if (!IsServer || IsDead.Value) return false;
            int v = Injuries.Value;
            switch (zone)
            {
                case ZoneLegs: if (LegsLost >= 2) return false; v = (v & ~3) | (LegsLost + 1); break;
                case ZoneArms: if (ArmsLost >= 2) return false; v = (v & ~12) | ((ArmsLost + 1) << 2); break;
                default: if (HeadHurt) return false; v |= 16; break;
            }
            Injuries.Value = (byte)v;
            InjuryClientRpc((byte)zone);

            // Aviso grande con humor negro
            var tm = NetworkTurnManager.Instance;
            var m = GetComponent<NetworkPlayerMovement>();
            if (tm != null && m != null && !m.IsTestDummy)
            {
                string n = m.RatName.Value.ToString();
                string text = zone == ZoneLegs ? (LegsLost >= 2 ? $"¡{n} SE QUEDA SIN PATAS!\nA rastras por la trinchera" : $"¡{n} pierde una pata!")
                            : zone == ZoneArms ? (ArmsLost >= 2 ? $"¡{n} SIN BRAZOS!\nAhora pelea a MORDISCOS" : $"¡{n} pierde un brazo!")
                            : $"¡A {n} LE HAN SALTADO UN OJO!";
                tm.AnnounceServer(text, new Color(1f, 0.35f, 0.25f));
            }
            return true;
        }

        /// <summary>(Servidor) Pone una prótesis si le falta algo: pata de palo o garfio. Devuelve el texto o null.</summary>
        public string GiveProsthesisServer()
        {
            if (!IsServer) return null;
            if (LegsLost > 0 && !HasPegLeg) { Injuries.Value = (byte)(Injuries.Value | 32); return "¡PATA DE PALO!"; }
            if (ArmsLost > 0 && !HasHook) { Injuries.Value = (byte)(Injuries.Value | 64); return "¡GARFIO!"; }
            return null;
        }

        /// <summary>Si es true, el próximo golpe que no mate SIEMPRE arranca algo (prueba automática).</summary>
        public static bool ForceInjuries;

        /// <summary>
        ///  (Servidor) ¿Este golpe arranca algo? Probabilidad según el daño y
        ///  la vida que queda; la ZONA según de dónde venga: por debajo
        ///  (dinamita a los pies, caída) → patas; por arriba → cabeza; de
        ///  lado o a bocajarro → brazos.
        /// </summary>
        private void TryInjureServer(int amount, int newHp, DamageType type, Vector2? source)
        {
            if (!MatchRules.EffectiveMutilations || type == DamageType.Water || newHp <= 0) return;
            float hpFrac = newHp / (float)Mathf.Max(1, MaxHealth);
            float chance = type == DamageType.Fall ? (amount >= 20 ? 0.5f : 0f)
                         : amount >= 35 ? 0.75f : amount >= 25 ? 0.45f : (amount >= 12 && hpFrac <= 0.4f) ? 0.3f : 0f;
            if (ForceInjuries && amount >= 5) chance = 1f;
            if (Random.value >= chance) return;

            int zone;
            if (type == DamageType.Fall) zone = ZoneLegs;
            else if (source.HasValue)
            {
                Vector2 d = source.Value - BodyCenter;
                zone = d.y < -0.18f ? ZoneLegs : d.y > 0.4f ? ZoneHead : ZoneArms;
            }
            else zone = Random.value < 0.65f ? ZoneArms : ZoneHead;

            // Si esa parte ya no tiene nada que romper, pasa a la siguiente.
            for (int k = 0; k < 3; k++)
            {
                if (InjureServer(zone)) return;
                zone = (zone + 1) % 3;
            }
        }

        [ClientRpc]
        private void InjuryClientRpc(byte zone)
        {
            Vector2 b = BodyCenter;
            Vector2 at = zone == ZoneLegs ? b + Vector2.down * 0.2f : zone == ZoneHead ? b + Vector2.up * 0.25f : b;
            Fx.LimbOff(at, zone);
            Sfx.Play(Sfx.Id.Splat, 0.9f);
            Sfx.Play(Sfx.Id.Ouch, 1f, 0.1f);
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.15f, 0.25f);
            string t = zone == ZoneLegs ? (LegsLost >= 2 ? "¡MIS PATAS!" : "¡MI PATA!")
                     : zone == ZoneArms ? (ArmsLost >= 2 ? "¡MIS BRAZOS!" : "¡MI BRAZO!")
                     : "¡MI OJO!";
            WorldDamagePopup.SpawnImpact(b + Vector2.up * 1.15f, t, new Color(1f, 0.3f, 0.25f));
        }

        /// <summary>Vida inicial para la PRÓXIMA rata que se spawnee (servidor). 0 = la del prefab.</summary>
        public static int SpawnNextMaxHealth;

        private Collider2D bodyCollider;

        /// <summary>
        ///  Centro REAL del cuerpo de la rata (centro de su collider).
        ///
        ///  ¡OJO! El pivote del prefab (transform.position) está ~0.8 u POR
        ///  DEBAJO del cuerpo, dentro del suelo. Para distancias de explosión,
        ///  popups, golpes o "¿ha tocado el agua?" hay que usar esto, no
        ///  transform.position.
        /// </summary>
        public Vector2 BodyCenter
        {
            get
            {
                if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
                return bodyCollider != null && bodyCollider.enabled
                    ? (Vector2)bodyCollider.bounds.center
                    : (Vector2)transform.position;
            }
        }

        /// <summary>
        ///  Evento que se dispara EN EL SERVIDOR cuando la rata muere.
        ///  El NetworkTurnManager lo escucha para sacar al jugador de la rotación
        ///  de turnos sin esperar al siguiente timeout.
        /// </summary>
        public event System.Action<NetworkHealth> OnDiedServer;

        // ==============================================================
        // CICLO DE VIDA
        // ==============================================================
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                MaxHealthNet.Value = SpawnNextMaxHealth > 0 ? SpawnNextMaxHealth : maxHealth;
                SpawnNextMaxHealth = 0;
                CurrentHealth.Value = MaxHealthNet.Value;
                IsDead.Value = false;
            }

            IsDead.OnValueChanged += OnDeathStateChanged;

            // Auto-añadir el numerito de vida flotante en TODOS los clientes
            // (host incluido). Es solo visual, no toca lógica.
            EnsureHealthLabel();

            // Late-join safety: si un cliente entra y esta rata ya estaba muerta,
            // que no aparezca viva por un frame.
            if (IsDead.Value) ApplyDeathVisual();
        }

        // Rata malherida (< 35 % de vida): va dejando gotas de sangre (local).
        private float nextDrip;
        private void Update()
        {
            if (!IsSpawned || IsDead.Value || MaxHealth <= 0) return;
            float hp = CurrentHealth.Value / (float)MaxHealth;
            if (hp >= 0.35f || Time.time < nextDrip) return;
            nextDrip = Time.time + Mathf.Lerp(0.15f, 0.7f, hp / 0.35f) * Random.Range(0.7f, 1.3f);
            Fx.Drip(BodyCenter + Vector2.down * 0.15f);
        }

        public override void OnNetworkDespawn()
        {
            IsDead.OnValueChanged -= OnDeathStateChanged;
        }

        /// <summary>
        ///  Añade el componente RatHealthLabel si no estaba ya. Así no hay
        ///  que tocar el prefab de la rata para tener el numerito de vida.
        /// </summary>
        private void EnsureHealthLabel()
        {
            var label = GetComponent<RatHealthLabel>();
            if (label == null) label = gameObject.AddComponent<RatHealthLabel>();
            label.Init(this);
        }

        // ==============================================================
        // DAÑO
        // ==============================================================
        /// <summary>
        ///  Aplica daño. SOLO el servidor puede llamarlo.
        ///  Versión por defecto (compatible hacia atrás): tipo = Generic.
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint)
        {
            ApplyDamageServer(amount, hitPoint, DamageType.Generic);
        }

        /// <summary>
        ///  Aplica daño con un tipo específico — explosión, agua, fuego...
        ///  El popup pintará el color/símbolo correspondiente.
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint, DamageType type) => ApplyDamageServer(amount, hitPoint, type, null);

        /// <summary>
        ///  Igual, sabiendo DE DÓNDE viene el golpe (<paramref name="source"/>,
        ///  p. ej. el centro de la explosión): sirve para decidir qué parte
        ///  del cuerpo se arranca.
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint, DamageType type, Vector2? source)
        {
            if (!IsServer) return;
            if (IsDead.Value || amount <= 0) return;

            int newHp = Mathf.Max(0, CurrentHealth.Value - amount);
            CurrentHealth.Value = newHp;

            // Propagamos el popup a todos los clientes (incluido el dueño).
            // Mandamos el tipo como byte para minimizar el tamaño del paquete.
            ShowDamagePopupClientRpc(amount, hitPoint, (byte)type, newHp <= 0);

            if (newHp <= 0)
            {
                IsDead.Value = true;
                OnDiedServer?.Invoke(this);
            }
            else TryInjureServer(amount, newHp, type, source);
        }

        /// <summary>(Servidor) Cura a la rata (botiquín), sin pasar de su vida máxima.</summary>
        public void HealServer(int amount)
        {
            if (!IsServer || IsDead.Value || amount <= 0) return;
            CurrentHealth.Value = Mathf.Min(MaxHealth, CurrentHealth.Value + amount);
        }

        /// <summary>
        ///  ServerRpc para que un cliente pueda pedir daño (no se usa hoy pero
        ///  está disponible por si en el futuro hay armas cliente-autoritativas).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void TakeDamageServerRpc(int amount, Vector3 hitPoint)
        {
            ApplyDamageServer(amount, hitPoint, DamageType.Generic);
        }

        // ==============================================================
        // KNOCKBACK (empujón de explosión)
        // ==============================================================
        /// <summary>
        ///  Empuja a esta rata aplicando un impulso a su Rigidbody2D.
        ///
        ///  Se llama desde el servidor (misil/granada al explotar) y lo aplica
        ///  SOLO el Owner de la rata — es quien controla la física de su
        ///  personaje; el NetworkTransform sincroniza el "vuelo" al resto de
        ///  clientes como cualquier otro movimiento normal.
        ///
        ///  Si la rata acaba de morir, no aplicamos empuje (para no mandar el
        ///  cadáver volando por toda la pantalla — opinión de diseño;
        ///  cámbialo si prefieres ragdoll).
        ///
        ///  <paramref name="freeFlightSeconds"/>: durante este tiempo el
        ///  <see cref="NetworkPlayerMovement"/> ignora su linearDamping y el
        ///  maxSpeed. Así el empujón "vuela" de verdad en lugar de frenar en
        ///  medio segundo por la fricción aérea que la rata usa para caminar.
        /// </summary>
        [ClientRpc]
        public void ApplyKnockbackClientRpc(Vector2 force, float freeFlightSeconds = 0.5f)
        {
            if (!IsOwner) return;
            if (IsDead.Value) return;

            var rb = GetComponent<Rigidbody2D>();
            if (rb == null) return;

            // Reseteamos velocidad vertical para que el empujón "suba" de verdad
            // aunque la rata estuviera ya cayendo. Si la rata se movía horizontal,
            // respetamos ese impulso sumándolo en vez de pisar.
            Vector2 v = rb.linearVelocity;
            v.y = 0f;
            rb.linearVelocity = v;

            rb.AddForce(force, ForceMode2D.Impulse);

            // Pedimos al script de movimiento que desactive fricción y tope de
            // velocidad durante el "vuelo" del empujón.
            var move = GetComponent<NetworkPlayerMovement>();
            if (move != null) move.BeginKnockbackFlight(freeFlightSeconds);
        }

        // ==============================================================
        // POPUPS
        // ==============================================================
        /// <summary>
        ///  Spawnea el popup flotante en TODOS los clientes (cada uno lo crea
        ///  en su escena, no es un objeto en red). El "type" llega como byte
        ///  por la red — lo casteamos a DamageType al llegar.
        /// </summary>
        [ClientRpc]
        private void ShowDamagePopupClientRpc(int amount, Vector3 hitPoint, byte typeByte, bool lethal)
        {
            DamageType type = (DamageType)typeByte;
            lastDamageType = type;
            WorldDamagePopup.Spawn(hitPoint, amount, type);
            if (type == DamageType.Water) { Fx.Splash(hitPoint); Sfx.Play(Sfx.Id.Splash); return; }
            bool laughs = PlayerPrefs.GetInt("opt_laughs", 1) == 1;

            // GORE: salpicón de sangre hacia fuera del golpe (mancha el suelo).
            Vector2 away = BodyCenter - (Vector2)hitPoint;
            Fx.Blood(BodyCenter, away.sqrMagnitude > 0.0004f ? away : Vector2.up, amount);
            var look = GetComponent<RatAppearance>();
            if (look != null) look.HitFlash();
            // Golpe fuerte: salpica a las ratas que estén pegadas.
            if (amount >= 20) RatAppearance.SplatterAround(BodyCenter, amount >= 40 ? 1.8f : 1.3f, gameObject, 1);
            if (!lethal)
            {
                Sfx.Play(Sfx.Id.Ouch, 0.7f, 0.15f);
                if (laughs && Random.value < 0.35f)
                    WorldDamagePopup.SpawnImpact(BodyCenter + Vector2.up * 0.9f, Pick(HurtQuips), new Color(1f, 0.75f, 0.6f));
            }

            // RISA: la rata que tiene el turno se ríe de su víctima (siempre si
            // la mata, a veces si solo la hiere). Humor negro de trinchera.
            var attacker = CurrentTurnRat();
            if (laughs && attacker != null && attacker.gameObject != gameObject && (lethal || Random.value < 0.45f))
            {
                var ah = attacker.GetComponent<NetworkHealth>();
                Vector2 at = (ah != null ? ah.BodyCenter : (Vector2)attacker.transform.position) + Vector2.up * 0.95f;
                WorldDamagePopup.SpawnImpact(at, Pick(Laughs), new Color(1f, 0.9f, 0.35f));
                Sfx.Play(Sfx.Id.Laugh, lethal ? 1f : 0.75f, 0.1f);
            }
        }

        private static readonly string[] HurtQuips =
        {
            "¡Mi cola!", "¡Eso escuece!", "¡Mis bigotes!", "¡Que me desangro!", "¡Auch, cabrón!",
            "¡Ratas!", "¡Mamáaa!", "¡Me has dejado tuerta!", "¡Eso no se hace!", "¡Uy, la víscera!",
        };
        private static readonly string[] Laughs =
        {
            "¡JA, JA, JA!", "¡JIJIJIJI!", "¡Chúpate esa!", "¡Toma ya!", "¡Al hoyo!", "¡Ñiec, ñiec!", "¡JE, JE!",
        };
        private static readonly string[] DeathQuips =
        {
            "¡Al queso eterno!", "Fiambre de trinchera", "Otra para el foso", "Ratatouille", "Se acabó la guerra para esta",
            "Tanto queso para esto...", "Directa al hoyo", "Requiescat in pizza", "Carne de cañón", "¡Adiós, mundo cruel!",
        };

        private static string Pick(string[] arr) => arr[Random.Range(0, arr.Length)];

        /// <summary>Rata (no diana) del jugador que tiene el turno ahora mismo.</summary>
        private static NetworkPlayerMovement CurrentTurnRat()
        {
            var tm = NetworkTurnManager.Instance;
            if (tm == null) return null;
            var m = tm.CurrentRat;
            if (m != null) return m;
            // Durante la transición (el disparo ya acabó el turno) buscamos la
            // última rata que lo tuvo: la que disparó.
            return tm.LastTurnRat;
        }

        // Último tipo de daño recibido (en cada cliente): para saber si la
        // muerte fue en el agua (salpicadura, sin lápida) o en tierra (lápida).
        private DamageType lastDamageType = DamageType.Generic;

        // ==============================================================
        // MUERTE
        // ==============================================================
        private void OnDeathStateChanged(bool oldValue, bool newValue)
        {
            if (!newValue) return;
            // Muerte en tierra: REVIENTA (gore) + lápida + frase de humor negro.
            // En el agua no: ya hay salpicadura.
            if (lastDamageType != DamageType.Water)
            {
                Vector2 b = BodyCenter;
                var look = GetComponent<RatAppearance>();
                Fx.Gibs(b, look != null ? look.Team : (Color32?)null);
                // Las vecinas acaban PRINGADAS de la que ha reventado.
                RatAppearance.SplatterAround(b, 3f, gameObject, 2);
                if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.25f, 0.35f);
                Sfx.Play(Sfx.Id.Splat);
                Fx.Gravestone(b);
                if (PlayerPrefs.GetInt("opt_laughs", 1) == 1)
                    WorldDamagePopup.SpawnImpact(b + Vector2.up * 1.3f, Pick(DeathQuips), new Color(0.95f, 0.25f, 0.2f));
            }
            Sfx.Play(Sfx.Id.Squeak, 0.8f);
            ApplyDeathVisual();
        }

        private void ApplyDeathVisual()
        {
            Debug.Log($"[NetworkHealth] {gameObject.name} ha muerto.");
            if (disableOnDeath)
            {
                gameObject.SetActive(false);
            }
            else
            {
                // Modo "solo oculta visuales" — útil si más adelante queremos
                // animación de muerte antes de desaparecer.
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                    sr.enabled = false;
                foreach (var col in GetComponentsInChildren<Collider2D>(true))
                    col.enabled = false;
            }
        }
    }
}
