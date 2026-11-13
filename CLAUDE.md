# Ratms

Juego 2D por turnos al estilo Worms, pero con ratas. Multijugador online (host + clientes vía código de sala).
El usuario habla español: responde, comenta el código y escribe los commits en **español**.

## Stack
- **Unity 6000.0.50f1**, URP 17.0.4, 2D (Rigidbody2D, sprites, PPU=32 en el terreno).
- **Netcode for GameObjects 2.1.1** + **Unity Relay** (Authentication 3.3.3, Relay 1.1.1).
- Input: API clásica `Input.GetAxisRaw` / `GetKeyDown` (el paquete Input System está instalado pero no se usa en los scripts).
- Tras cambiar C#, **compila en batchmode** (comando en la sección Git) y corrige los `error CS` antes de commitear. Lo que no se puede verificar compilando (sensación de juego, multijugador real) se apunta para que el usuario lo pruebe.

## Estructura (`Assets/Scripts`)
| Carpeta | Namespace | Qué hay |
|---|---|---|
| `Multiplayer/` | `Ratms.Networking` | Todo el juego en red: movimiento, armas, turnos, vida, UI de partida, Relay |
| `World/` | `Ratms.World` | Terreno procedural y destructible, biomas, fondos, agua mortal |
| `Core/` | `Ratms.Core` / `Ratms.UI` | Menú principal, opciones, carga de escenas |
| `Testing/` | `Ratms.Testing` | `AutoPlayTest`: prueba automática con capturas (solo se activa con `-ratmsAutoTest`) |
| `Camerafollow.cs` | (sin namespace) | Cámara del juego (SÍ se usa en red): seguimiento, shake, foco de impacto, zoom con rueda |
| `Player/`, `Weapons ( armas )/`, `turns (turnos)/` | (sin namespace) | **Legacy de un jugador**, previo al multijugador. No ampliar; el juego real es la versión de red |
| `Assets/Editor/` | — | `AutoTestBuild` (build para la prueba), `RatmsPrefabSetup` (añade componentes a prefabs por código) |

## Arquitectura de red (respetar siempre)
- **Servidor autoritativo**: daño, vida, turnos, explosiones y victoria los decide el servidor. `NetworkVariable` con `writePerm: Server` salvo estado que el dueño publica (flip, velocidad, ángulo del arma).
- Única excepción aceptada: el daño por caída lo calcula el cliente dueño y lo pide por `ServerRpc`.
- La física de la rata la mueve su **Owner**; el knockback llega por `ClientRpc` y solo lo aplica el Owner.
- **Terreno determinista**: el servidor elige semilla → todos generan el mismo mapa. Los agujeros se replican con `NetworkDestructibleTerrain.CarveClientRpc` (cada peer carvea localmente).
- Popups y etiquetas de vida son locales, se crean por código (sin prefab): `WorldDamagePopup`, `RatHealthLabel`.
- ⚠ **El pivote del prefab de la rata está ~0.8 u POR DEBAJO del cuerpo** (dentro del suelo). Para distancias de explosión, golpes, popups o "¿toca el agua?" usa `NetworkHealth.BodyCenter`, nunca `transform.position`. Para colocar cosas en la mano usa `NetworkPlayerMovement.HandPosition` desde un `LateUpdate` con `[DefaultExecutionOrder(100)]` (el Animator pisa el weaponHolder y el movimiento lo corrige en su LateUpdate).
- El dibujo de la rata quedaba ~0.19 u por debajo de su collider (patas metidas en la hierba): `colliderDropToFeet` baja el collider y el groundCheck en runtime.
- **Collider del terreno = contornos exactos** (`PixelContours`, marching squares → un `EdgeCollider2D` por bucle, ~40 ms por explosión en 2048×1024). Los bordes NO tienen "dentro": algo que acabe dentro de la tierra caería a través del mapa → `NetworkPlayerMovement.UnstuckFromTerrain` saca a la rata hacia arriba. Proyectiles en colisión continua. El sistema antiguo sigue como `TerrainColliderMode.LegacySkyline`.
- **Píxeles de decoración**: alfa 254 (`DestructibleTerrain.DecorAlpha`) = se ven pero NO son sólidos. Usa siempre `DestructibleTerrain.IsSolidPixel(c)` en vez de `c.a > 0`.
- Cráteres: cuenco (`craterDepthRatio` 0.65) + embudo a 45° (`craterFunnelHeight`) para que nunca queden paredes en voladizo, borde chamuscado y limpieza de decoración flotante.
- La rata se "aparca" quieta en el suelo (gravedad 0) para no resbalar en cuestas (`holdOnSlopes` en `NetworkPlayerMovement`). Cualquier empujón debe pasar por `BeginKnockbackFlight` (ya lo hace `ApplyKnockbackClientRpc`) para soltarla.
- Al andar con suelo bajo los pies, la velocidad va PARALELA a la cuesta de delante (`TryGetGroundNormal(dir)`, límite `maxWalkSlopeNormalY` 0.6 ≈ 53°). Empujar solo en X contra la pendiente hacía que la fricción clavase la rata.
- **Un arma por turno** (`firedThisTurn`): tras usarla puedes moverte (huir) pero no volver a disparar.
- Para añadir componentes a un prefab, mejor un método en `Assets/Editor/RatmsPrefabSetup.cs` ejecutado en batchmode (`-executeMethod`) que editar el YAML a mano (el prefab de la rata es una variante con `m_AddedComponents`).

## Flujo de partida
1. `MainMenu` → `MultiplayerMenuController` + `RelayManager`: el host crea sala (código corto), los clientes se unen. Viven en un objeto persistente (DontDestroyOnLoad) con el `NetworkManager`.
2. El host carga la escena de juego con `NetworkManager.SceneManager.LoadScene` (`gameSceneName = "Escenario_TerrainTest"` en la escena MainMenu).
3. `GameSceneSpawnManager` spawnea una rata por cliente en `OnLoadEventCompleted` (no antes, o los clientes ven 0 ratas). El `PlayerPrefab` del NetworkManager va VACÍO.
4. `NetworkTurnManager` (singleton): rotación round-robin por **clientId**, 30 s por turno, 1.5 s de transición. Al disparar, el turno espera a que el proyectil impacte (`ShortenEndTurnClientRpc`, fallback 3.5 s). Cada turno sortea el **viento** (`Wind` -1..1; `WindAcceleration` estático); misil factor 1, granada 0.25, dinamita 0. Crea el `TurnHUD` (cuenta atrás + viento) si la escena no lo tiene.
5. Muerte (`NetworkHealth.OnDiedServer`) o desconexión → `RemovePlayerServer`. Queda ≤1 → `GameEnded` + `WinnerClientId` → `VictoryScreen`.

Prefabs de red: `Assets/Prefabs/Prefabs multiplayer/` (`rat.network`, `missile.network`, `Grenade`), registrados en `Assets/DefaultNetworkPrefabs.asset`. Todo prefab nuevo con `NetworkObject` debe añadirse ahí.

Armas (`WeaponsMenuUI.WeaponId`, teclas 1-7): Bazooka (`NetworkBazooka` → `NetworkMissile`), Granada (`NetworkGrenadeThrower` → `NetworkGrenade`), **Dinamita** (mismo prefab que la granada con `PrepareAsDynamite`: sin rebote, mecha 5 s, el turno dura la mecha para huir), **Escopeta** (`NetworkShotgun`, rayo, 2 disparos/turno), **Bate** (`NetworkBat`), **Teletransporte** (`NetworkTeleport`, ratón/clic, el servidor valida), **Cuerda ninja** (`NetworkNinjaRope`, DistanceJoint2D del dueño, no gasta el arma del turno; con cuerda la rata nunca se "aparca"). Los componentes de armas van en la raíz de la rata y se añaden con `RatmsPrefabSetup.EnsureRatComponents` (batchmode). Visuales de armas en el mundo (sin padre) y `[DefaultExecutionOrder(100)]`.

Estética (estilo **trinchera + gore + humor negro**, lo pidió el usuario): `RatAppearance` dibuja por código una rata sarnosa con casco de trinchera (franja = color del equipo por `OwnerClientId`), oreja rota, ojo rojo, dientes amarillos, cigarro; sustituye al sprite .ase (`useProceduralRat`). `Fx` (partículas locales: explosiones, humo, chispas, salpicadura, lápida, **sangre que mancha el terreno** vía `DestructibleTerrain.StainAtWorld`, vísceras al morir). Motor propio en `Fx`: array de structs `P` + pool de SpriteRenderers, `ref P New(...)` y se rellenan campos (stretch = gota alargada, collides/bounces/sticks = rebota y se queda tirado por píxeles del terreno, stains = mancha, trailKind = estela sangre/humo, midT = color en 3 fases, wind). Sprites pixel-art de vísceras con `Pix(...)` (filas de caracteres, 32 px/u). Al morir: `Fx.Gibs(punto, RatAppearance.Team)` (casco con la franja del equipo); golpe → `RatAppearance.HitFlash()`; <35 % de vida → gotea. Autotest: escaparate G01–G08. `Sfx` (18 sonidos sintetizados, volumen = opt_master × opt_sfx). `NetworkHealth`: quejidos, frases de muerte y la rata del turno se ríe. Gore desactivable con PlayerPrefs `opt_gore` = 0 (aún sin botón en Opciones).

Menú: "Jugar" = **modo Práctica** (`PracticeSession`: host local + 3 dianas que se reponen). ESC×2 sale al menú (`TurnHUD.ExitToMenu`).

Tipos de daño: `DamageType` (Generic, Explosion, Fire*, Water, Fall, Melee). *Reservado sin uso.

## Convenciones
- Comentarios abundantes en español con `/// <summary>` explicando el *porqué* (el usuario aprende leyéndolos). Mantener ese estilo.
- `[Header]` + `[Tooltip]` en español en todos los campos del Inspector.
- Al borrar un script o prefab, borrar también su `.meta` y comprobar que ningún `.prefab`/`.unity` referencia su GUID.

## Prueba automática (ver el juego sin jugar)
`powershell -ExecutionPolicy Bypass -File tools\autotest.ps1` (Unity cerrado; `-SkipBuild` reutiliza la build, `-Biome Sewer` fuerza bioma).
Compila la build y el juego se juega solo (~90 s, host local sin internet): mide andar/saltar/cuestas con números, saca primeros planos con las líneas de los colliders dibujadas (`*_colliders.png`: terreno magenta, rata cian), prueba a salir de cráteres, comprueba el viento con misiles de prueba, dispara bazooka y granada, y prueba dinamita, bate y agua contra una **rata diana** (`NetworkPlayerMovement.SpawnNextAsTestDummy`: no cuenta para turnos ni victoria). Deja ~20 capturas + `informe.txt` (línea de tiempo, ✓/⚠, errores y warnings) en `AutoTest\Run_<fecha>\`. Mira las PNG con Read. Al añadir armas o mecánicas, amplía el guion en `Assets/Scripts/Testing/AutoPlayTest.cs` (hay ganchos `TestInputX`, `TestPressJump`, `TestPressFire`, `TestSetAim` en la rata).
Resultado de referencia (13 nov 2026): `Resultado: OK`, 0 errores, 0 warnings.
También prueba escopeta, teletransporte y cuerda. Otros papeles (`-autoTestRole`): `practice` (botón Jugar → 3 dianas → ESC → menú) y **multijugador real**: `powershell -ExecutionPolicy Bypass -File tools\autotest-multi.ps1` (host + cliente por 127.0.0.1: turnos, viento igual en ambos, dinamita vista en el cliente, el host gana si el cliente se va). Las pruebas usan `CurrentSurfaceY` (suelo real con cráteres) y buscan línea de tiro/destinos válidos; algunos resultados (salir de cráteres) dependen del mapa aleatorio.

## Git
- Rama de trabajo: `Main` (sigue a `origin/Main`, repo `darkkikilord/Ratms`).
- **Flujo acordado**: trabajar directamente en `Main`. Tras cada tarea que compile → commit + `git push` sin pedir confirmación. Si algo sale mal se revierte con git. Nunca `push --force`.
- Compilar sin abrir Unity (Unity debe estar cerrado): `B:\Juegos\6000.0.50f1\Editor\Unity.exe -batchmode -nographics -quit -projectPath <repo> -logFile <log>` y buscar `error CS` en el log.
- `origin/Pre-multipalyer` es un intento antiguo de multijugador de otro colaborador (Guillermo, ago 2025); **no fusionar**.
- Commits con versión: `Ratms 0.X: resumen`. Último: 0.13 (+ commits sueltos de práctica, sonido, rata de trinchera, gore, efectos de calidad y andar siguiendo el suelo).
- Las builds exportadas van fuera del repo, en `Documents/Ratms Versiones`.

## Estado y roadmap (nov 2026)
Hecho: multijugador con Relay, bazooka, granada, dinamita, bate, trayectoria (se corta contra el terreno), viento, HUD con cuenta atrás, terreno destructible procedural, biomas, agua mortal, daño por caída, pantalla de victoria, nombres de jugador, un arma por turno.

Pendiente (detalle en `create_roadmap_issues.sh`; aún no se han creado las issues en GitHub):
- **Probar en partida real (2 clientes)**: espera del turno hasta el impacto; victoria por desconexión; que la dinamita se vea como dinamita en el cliente (no host).
- **Ajustar a mano (Inspector)**: fuerza del viento (`maxWindAcceleration` 3.5), empujón del bate (14), radio/mecha de la dinamita (4 u / 5 s), zoom inicial (6).
- Escenario de producción (ahora solo `Escenario_TerrainTest`); islas flotantes con collider propio (`TerrainGenerator.cs`).
- Equipos: varias ratas por jugador → el turno rotaría por rata, no por cliente.
- Cajas de suministros, daño por fuego, munición limitada por arma.
- Botón de gore (opt_gore) y de "risas" en el menú de Opciones.
- La mano (weaponHolder) es la del rig antiguo: las armas no caen exactamente en la pata de la rata nueva.
- El spawn aleatorio a veces pone ratas junto al borde del mapa (muy cerca del agua).
- Salir de cráteres que se juntan con cuevas profundas a veces no es posible solo saltando (depende del mapa).
- Reconstruir el collider tarda ~40 ms por explosión: si se nota un tirón, limitar el recálculo a la zona del cráter.

Problemas conocidos menores:
- `EditorBuildSettings` lista `Assets/Scenes/Test_Network.unity`, que ya no existe.
- `Assets/Prefabs/armas/misil_0.prefab` (legacy) conserva un campo `damagePopupPrefab` huérfano; Unity lo ignora.
