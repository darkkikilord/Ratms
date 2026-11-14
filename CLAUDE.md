# Ratms

Juego 2D por turnos al estilo Worms, pero con ratas. Multijugador online (host + clientes vía código de sala).
El usuario habla español: responde, comenta el código y escribe los commits en **español**.

## Stack
- **Unity 6000.0.50f1**, URP 17.0.4, 2D (Rigidbody2D, sprites, PPU=32 en el terreno).
- **Netcode for GameObjects 2.1.1** + **Unity Relay** (Authentication 3.3.3, Relay 1.1.1).
- Input: API clásica `Input.GetAxisRaw` / `GetKeyDown` (el paquete Input System está instalado pero no se usa en los scripts).
- Tras cambiar C#, **compila** con `powershell -ExecutionPolicy Bypass -File tools\compilar-rapido.ps1` (MSBuild, ~2 s, vale con Unity abierto) y corrige los `error CS` antes de commitear. Detalles y cuándo hace falta además la compilación de Unity (Netcode ILPP, prefabs, cierre de versión): skill `/compilar`. Para cerrar una versión: skill `/version`. Lo que no se puede verificar compilando (sensación de juego, multijugador real) se apunta para que el usuario lo pruebe.

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
- La física de la rata la mueve su **Owner**; el knockback llega por `ClientRpc` y solo lo aplica el Owner. En los demás PCs el Rigidbody2D de la rata es **Kinematic** (`ApplyAuthorityPhysics`): si fuese dinámico, la física local la haría caer mientras el NetworkTransform la recoloca y el dibujo se separaba del arma ("rata glicheada").
- Menú persistente: `MultiplayerMenuController.Instance` es el del objeto DontDestroyOnLoad; al recargar MainMenu, la copia de la escena le pasa sus paneles (`AdoptSceneUI`) y se apaga. Usa `Instance`/`InOnlineRoom`, no la referencia del Inspector.
- **Terreno determinista**: el servidor elige semilla → todos generan el mismo mapa. Los agujeros se replican con `NetworkDestructibleTerrain.CarveClientRpc` (cada peer carvea localmente).
- Popups y etiquetas de vida son locales, se crean por código (sin prefab): `WorldDamagePopup`, `RatHealthLabel`.
- ⚠ **El pivote del prefab de la rata está ~0.8 u POR DEBAJO del cuerpo** (dentro del suelo). Para distancias de explosión, golpes, popups o "¿toca el agua?" usa `NetworkHealth.BodyCenter`, nunca `transform.position`. Para colocar cosas en la mano usa `NetworkPlayerMovement.HandPosition` desde un `LateUpdate` con `[DefaultExecutionOrder(100)]` (el Animator pisa el weaponHolder y el movimiento lo corrige en su LateUpdate).
- El dibujo de la rata quedaba ~0.19 u por debajo de su collider (patas metidas en la hierba): `colliderDropToFeet` baja el collider y el groundCheck en runtime.
- **Collider del terreno = contornos exactos** (`PixelContours`, marching squares → un `EdgeCollider2D` por bucle, ~12 ms por explosión en 2048×1024 desde 0.19). Los bordes NO tienen "dentro": algo que acabe dentro de la tierra caería a través del mapa → `NetworkPlayerMovement.UnstuckFromTerrain` saca a la rata hacia arriba. Proyectiles en colisión continua. El sistema antiguo sigue como `TerrainColliderMode.LegacySkyline`.
- **Píxeles de decoración**: alfa 254 (`DestructibleTerrain.DecorAlpha`) = se ven pero NO son sólidos. Usa siempre `DestructibleTerrain.IsSolidPixel(c)` en vez de `c.a > 0`.
- Cráteres: cuenco (`craterDepthRatio` 0.65) + embudo a 45° (`craterFunnelHeight`) para que nunca queden paredes en voladizo, borde chamuscado y limpieza de decoración flotante.
- **Daño por caída**: lo vigila el dueño para TODAS sus ratas (antes del `return` de `CanControl` en `Update`): una rata lanzada en el turno de otro también se hace daño al caer.
- **Auto-daño**: el misil ya NO excluye a quien dispara (como en Worms); solo ignora el CHOQUE con él el primer medio segundo (`ownerIgnoreTime`).
- **Voltereta**: es solo dibujo → `BackflipServerRpc` → `BackflipClientRpc` para que la vean los demás. Cualquier efecto visual "del dueño" que no sea posición necesita su RPC.
- ⚠ **Bioma**: `BiomeSyncer` repinta con `TerrainGenerator.RegenerateSameSeed()`. NUNCA `Generate()` fuera del menú/pruebas: sortea semilla y cada PC acabaría con un mapa distinto (era el bug de la alcantarilla: el cliente veía las ratas bajo tierra).
- La rata se "aparca" quieta en el suelo (gravedad 0) para no resbalar en cuestas (`holdOnSlopes` en `NetworkPlayerMovement`). Cualquier empujón debe pasar por `BeginKnockbackFlight` (ya lo hace `ApplyKnockbackClientRpc`) para soltarla.
- Al andar con suelo bajo los pies, la velocidad va PARALELA a la cuesta de delante (`TryGetGroundNormal(dir)`, límite `maxWalkSlopeNormalY` 0.6 ≈ 53°). Empujar solo en X contra la pendiente hacía que la fricción clavase la rata.
- **Un arma por turno** (`firedThisTurn`): tras usarla puedes moverte (huir) pero no volver a disparar.
- Para añadir componentes a un prefab, mejor un método en `Assets/Editor/RatmsPrefabSetup.cs` ejecutado en batchmode (`-executeMethod`) que editar el YAML a mano (el prefab de la rata es una variante con `m_AddedComponents`).

## Flujo de partida
1. `MainMenu` → `MultiplayerMenuController` + `RelayManager`: el host crea sala (código corto), los clientes se unen. Viven en un objeto persistente (DontDestroyOnLoad) con el `NetworkManager`.
2. El host carga la escena de juego con `NetworkManager.SceneManager.LoadScene` (`gameSceneName = "Escenario_TerrainTest"` en la escena MainMenu).
3. `GameSceneSpawnManager` spawnea los **equipos** en `OnLoadEventCompleted` (no antes, o los clientes ven 0 ratas): online un equipo por cliente; en partida local (`MatchMode.Local`) todos los equipos son del host. Cada equipo tiene `MatchRules.EffectiveRatsPerTeam` ratas (por defecto 4). Equipo, nombre y vida se pasan por estáticos (`NetworkPlayerMovement.SpawnNextTeam/SpawnNextName`, `NetworkHealth.SpawnNextMaxHealth`) y la rata los escribe en sus NetworkVariables (`TeamIndex`, `RatName`, `MaxHealthNet`) en `OnNetworkSpawn` del servidor. La primera rata de cada cliente es su player object; el resto `SpawnWithOwnership`. El `PlayerPrefab` del NetworkManager va VACÍO.
4. `NetworkTurnManager` (singleton): turnos POR EQUIPOS. Alterna equipos y, dentro de cada equipo, la rata rota sola (como Worms; elegir rata será un objeto que gaste turno). Estado en red: `CurrentRatId` (NetworkObjectId de la rata del turno), `CurrentTeam`, `CurrentTurnClientId` (dueño). **Control y armas**: `CanControl` = `tm.IsActiveRat(NetworkObjectId)` (el dueño tiene varias ratas); los ServerRpc de armas validan `tm.CanAct(sender, NetworkObjectId)`; `WeaponsMenuUI` solo atiende a la rata del turno. Turno de `MatchRules.TurnSeconds` (30), 1.5 s de transición. La cámara sigue a la rata del turno en todos los clientes. Al disparar, el turno espera a que el proyectil impacte (`ShortenEndTurnClientRpc`, fallback 3.5 s). Cada turno sortea el **viento** (`Wind` -1..1; `WindAcceleration` estático); misil factor 1, granada 0.25, dinamita 0. Crea el `TurnHUD` (cuenta atrás + viento) si la escena no lo tiene.
5. Muerte (`NetworkHealth.OnDiedServer` → `tm.OnRatDiedServer`) o desconexión: un equipo sin ratas vivas queda eliminado (aviso grande). Queda 1 equipo → `GameEnded` + `WinnerTeam`/`WinnerClientId` → `VictoryScreen`. En práctica (1 equipo) solo acaba si muere tu rata.
6. **Muerte súbita**: a los `MatchRules.SuddenDeathMinutes` (10) el agua sube `SuddenDeathRisePerTurn` (1.2 u) al acabar cada turno (`tm.WaterRise`, lo anima `WaterDeathZone.SurfaceY`).
7. **Reglas** (`MatchRules`, PlayerPrefs `rule_*`): equipos locales, ratas por equipo, vida, segundos de turno, muerte súbita. Las lee el host. `Override*` para la prueba automática (no tocar los PlayerPrefs del jugador). El botón **Jugar** abre `PlayModeMenu` (IMGUI): Práctica o Partida local con equipos/ratas/muerte súbita. `TurnHUD` dibuja: quién juega (equipo + rata), reloj y cuenta atrás de muerte súbita, barras de vida por equipo y avisos (`NetworkTurnManager.OnAnnouncement`).

Prefabs de red: `Assets/Prefabs/Prefabs multiplayer/` (`rat.network`, `missile.network`, `Grenade`), registrados en `Assets/DefaultNetworkPrefabs.asset`. Todo prefab nuevo con `NetworkObject` debe añadirse ahí.

Armas (`WeaponsMenuUI.WeaponId`, teclas 1-7): Bazooka (`NetworkBazooka` → `NetworkMissile`), Granada (`NetworkGrenadeThrower` → `NetworkGrenade`), **Dinamita** (mismo prefab que la granada con `PrepareAsDynamite`: sin rebote, mecha 5 s, el turno dura la mecha para huir), **Escopeta** (`NetworkShotgun`, rayo, 2 disparos/turno), **Bate** (`NetworkBat`), **Teletransporte** (`NetworkTeleport`, ratón/clic, el servidor valida), **Cuerda ninja** (`NetworkNinjaRope`, DistanceJoint2D del dueño, no gasta el arma del turno; con cuerda la rata nunca se "aparca"). Los componentes de armas van en la raíz de la rata y se añaden con `RatmsPrefabSetup.EnsureRatComponents` (batchmode). Visuales de armas en el mundo (sin padre) y `[DefaultExecutionOrder(100)]`.

Estética (estilo **trinchera + gore + humor negro + comedia tipo Worms**, lo pidió el usuario): `RatAppearance` dibuja por código cada rata POR PIEZAS según su `RatLook` (anatomía gorda/flaca/jorobada/musculosa/topo, pelaje, cola normal/larga/muñón/sin cola, manca, pata de palo, orejas, ojo parche/loco/monóculo, casco Brodie/Adrian/Pickelhaube/gorra/pañuelo/máscara de gas, boca, marcas, diente de oro). `RatLook.Pack()` → `uint` en `NetworkPlayerMovement.LookCode` (lo elige el servidor; dentro de un equipo, anatomías distintas). Editor **Mi equipo** (`TeamEditor`, desde Jugar): nombre de equipo, nombres y aspecto fijo/al azar de 4 ratas en `TeamProfile` (PlayerPrefs `team_*`); en local lo usa el primer equipo y online viaja al host en el hello de `LobbySync` (`ProfileOf`); el nombre de equipo va a `GameSceneSpawnManager.CustomTeamNames`. Siguiente paso de cosméticos: elegir pieza a pieza (casco, cola...) en vez de sortear. El equipo se reconoce por la BUFANDA y el detalle del casco (color de equipo). Fotogramas: quieta×2, parpadeo, andar×4, aire, mueca de dolor (`HitFlash`); 3 niveles de HERIDAS (más sangre en el pelaje con menos vida). Galería para revisar diseños: `tools\autotest.ps1 -Role gallery` (R01–R03). Armas en pixel-art (`WeaponArt`, mismo estilo; `WeaponSkin` cambia bazuca/granada de la mano, `ProjectileSkin` viste misil y granada). `Fx` (partículas locales: explosiones, humo, chispas, salpicadura, lápida, **sangre que mancha el terreno** vía `DestructibleTerrain.StainAtWorld`, vísceras al morir). Motor propio en `Fx`: array de structs `P` + pool de SpriteRenderers, `ref P New(...)` y se rellenan campos (stretch = gota alargada, collides/bounces/sticks = rebota y se queda tirado por píxeles del terreno, stains = mancha, trailKind = estela sangre/humo, midT = color en 3 fases, wind). Sprites pixel-art de vísceras con `Pix(...)` (filas de caracteres, 32 px/u). Al morir: `Fx.Gibs(punto, RatAppearance.Team)` (casco con la franja del equipo); golpe → `RatAppearance.HitFlash()`; <35 % de vida → gotea. Autotest: escaparate G01–G08. `Sfx` (18 sonidos sintetizados, volumen = opt_master × opt_sfx). `NetworkHealth`: quejidos, frases de muerte y la rata del turno se ríe. Gore desactivable con PlayerPrefs `opt_gore` = 0 (aún sin botón en Opciones).

Menú: "Jugar" = **modo Práctica** (`PracticeSession`: host local + 3 dianas que se reponen). ESC×2 sale al menú (`TurnHUD.ExitToMenu`).

Tipos de daño: `DamageType` (Generic, Explosion, Fire*, Water, Fall, Melee). *Reservado sin uso.

## Economía de armas (0.16)
`NetworkTurnManager.Items.cs` (parte `partial` del TurnManager; los ganchos `Items*` son métodos parciales):
- **Munición** por equipo y arma en `NetworkList<int> Ammo` (índice equipo×13+arma, `WeaponsMenuUI.Count`; -1 = infinita; valores en `MatchRules.DefaultAmmo`). El cliente comprueba `WeaponsMenuUI.HasAmmo` antes de disparar y avisa con `ConsumeAmmoServerRpc` (`NetworkPlayerMovement.SpendAmmo`).
- **Cajas** (armas / botiquín / trampa) y **barriles**: NO son objetos de red. El servidor decide y avisa con `SpawnItemClientRpc`/`RemoveItemClientRpc`; cada cliente los dibuja y deja caer igual (terreno idéntico). Recoger y explotar lo decide el servidor. Toda explosión que abre agujero pasa por `NetworkDestructibleTerrain.OnServerExplosion` → barriles en cadena (`ServerBlast`). Los barriles tienen BoxCollider2D (las ratas se suben, los misiles chocan).
- **Cambiar rata** (`WeaponId.SelectRat`, tecla 8): clic en otra rata tuya → `SelectRatServerRpc` → juega el próximo turno de tu equipo (`Team.forcedRat`) y se pierde el turno actual.
- Prueba: `tools\autotest.ps1 -Role items`.

## Mutilaciones (0.17)
`NetworkHealth.Injuries` (byte en red, lo escribe el servidor): bits 0-1 patas perdidas, 2-3 brazos, 4 tuerta, 5 pata de palo, 6 garfio. `TryInjureServer` decide en `ApplyDamageServer` (golpes ≥25, o ≥12 con poca vida; caída ≥20 → patas). La ZONA sale del origen del golpe (`ApplyDamageServer(..., source)`: las explosiones pasan su centro): por debajo → patas, por arriba → cabeza, de lado/bocajarro → brazos. Efectos: `SpeedFactor`/`JumpFactor` (sin patas = `Crawling`, sin salto), `CanUseHands` (sin brazos: `WeaponsMenuUI.NeedsHands` bloquea armas y ESPACIO = **mordisco** `NetworkBat.Bite`, cura un poco), `AimWobbleDeg` (temblor en `NetworkPlayerMovement.AimWobble`, también en el disparo), tuerta = viñeta en `TurnHUD` y solo 3 puntos de trayectoria. Las cajas de armas pueden traer prótesis (`GiveProsthesisServer`). `RatAppearance` dibuja muñones, garfio, venda en el ojo y la rata a rastras (todo el dibujo baja `legH` px). Los defectos de nacimiento (`RatLook.oneArm/pegLeg`) son SOLO aspecto. Regla `MatchRules.Mutilations` (`rule_mutil`). Prueba: `tools\autotest.ps1 -Role mutil` (M01–M07) y la galería R04; en el resto de papeles las mutilaciones van apagadas (`OverrideMutilations = false`).

## Arsenal y fuego (0.18)
- Armas nuevas en `WeaponsMenuUI.WeaponId` 8-12 (`WeaponsMenuUI.Count` = 13 = tamaño del bloque de munición por equipo): **Lanzallamas** (9, mantener ESPACIO, gasolina 2.2 s), **Metralleta** (Q, ráfaga de 14 balas con rayo), **Bomba de racimo** (E, la granada con `NetworkGrenade.PrepareAsCluster` → 5 bombetas `PrepareAsBomblet`; `Variant` en red), **Ataque aéreo** (R) y **Napalm** (F): clic en el mapa, biplano (`PlaneFlyby`, solo visual) y 5 `NetworkMissile` con `PrepareAsAirBomb(napalm)` que caen rectas, sí dañan al que las pide y no acortan el turno. Lanzallamas, metralleta y avión viven en `NetworkArsenal` (raíz de la rata, vía `RatmsPrefabSetup`).
- **Fuego** (`FireSystem`): llamas locales (no son objetos de red). El servidor las crea con `FireSystem.ServerIgnite` → `NetworkTurnManager.IgniteClientRpc` (parte `NetworkTurnManager.Fire.cs`); cada PC las simula (caen, se pegan al suelo, se apagan en el agua, se quedan encima de las ratas). Daño SOLO en el servidor cada 0.5 s (`DamageType.Fire`, 4-6 + saltito). Una llama que toca un barril lo revienta (`ServerFireTouches`); los barriles sueltan fuego de verdad.
- **Mano**: `RatAppearance.TryGetHand` calcula la pata delantera según la anatomía; `NetworkPlayerMovement.LateUpdate` lleva ahí el `weaponHolder` (todo lo que cuelga de él y `HandPosition` le siguen).
- **Salir de agujeros**: en el aire la rata no tiene rozamiento (`SetSlippery`) ni empuja contra la pared (`TouchingWall`): antes el roce con la pared se comía el salto. Saltar contra una pared de hasta `maxLedgeClimb` (1.6 u; el salto normal sube 0.74) la escala (`LedgeClimbVelocity`). Doble toque de salto = **voltereta** hacia atrás (~1.65 u, `RatAppearance.Backflip`).
- **Spawn**: `TerrainGenerator.IsGoodSpawn` exige suelo llano, sitio a lo ancho y salida (sin paredes de más de 2.8 u en 3.5 u a algún lado). Desde 0.19 además solo vale aire CONECTADO CON EL CIELO (`SkyConnectedAir`, relleno por inundación): nada de empezar en una bolsa de cueva.
- Prueba: `tools\autotest.ps1 -Role arsenal` (A00-A15: mano con cada arma, todas las armas nuevas contra dianas, barril que arde, voltereta y escalar una pared levantada con `DestructibleTerrain.TestFillRectWorld`).

## Menús (0.16)
Estilo común IMGUI `Core/TrenchGUI` (paneles de caja de munición). `PlayModeMenu` (Jugar: práctica o local + `RulesEditor` con todas las reglas), `OptionsOverlay` (volúmenes, sangre, risas `opt_laughs`, temblor `opt_shake`, pantalla completa; `OptionsMenu` aplica el mezclador vía `TrenchGUI.OnVolumesChanged`), `PauseMenu` (ESC en partida: seguir, opciones, cómo se juega, desertar). `MenuDressing` viste el menú principal (tinte de guerra, humo, obuses lejanos, desfile de ratas aleatorias, botones de madera, lema y versión). Lobby online: `LobbySync` (mensajes con nombre de NGO: nombres, ¡Listo!, resumen de reglas) + `LobbyCard` (IMGUI junto al panel del lobby; el host edita reglas y solo puede empezar si todos están listos). `Music` sintetiza dos marchas (menú y batalla) y cambia con la escena.

## Terreno estilo Worms, rendimiento y arreglos (0.19)
- **Forma**: `WormsTerrainShape.Build` (rejilla de 4 px + campo de densidad): perfil de colinas + ruido 2D cerca de la superficie (salientes y arcos), túneles con ruido "ridged", 1-3 islas flotantes, 0-2 canales de agua, suavizado y limpieza de migas. `TerrainGenerator.shapeStyle` = Worms (por defecto) o Classic (el de antes). La hierba se pinta columna a columna en cada paso aire→tierra (`PaintWormsShape`). El fondo oscuro de cuevas usa `GetBackdropLine()` (perfil −44 px y nunca por encima de la superficie real), no la línea de altura.
- Las "piedras" de la tierra son rocas con luz/sombra (antes parecían agujeros).
- **Rendimiento de explosiones** (~37 → ~12 ms): el cráter recorre una caja ajustada y descarta rápido los píxeles lejanos (2 ms); `PixelContours.Build(..., changed)` guarda la rejilla sólido/aire entre explosiones y solo rehace la zona cambiada (`DestructibleTerrain.MarkSolidChanged`; `solidChangedAll` al cargar mapa). Si añades algo que cambie qué píxel es SÓLIDO, márcalo ahí.
- Granada: 45 de daño y radio 3 (antes 70 / 3.5, pegaba demasiado).
- Ver el mapa sin jugar: con Unity abierto, MCP `execute_code` crea un GameObject con `DestructibleTerrain` (+SpriteRenderer, llamando a su `Awake` por reflexión) y `TerrainGenerator`, llama a `GenerateWithSeed` y guarda `tex` como PNG.
- `autotest-multi.ps1 -Biome Sewer` (bioma del host) y el informe de cada lado trae la **huella del mapa**: host y cliente deben coincidir.

## Menús, idiomas y victoria (0.19)
- **Idiomas** (`Core/Loc.cs` + `Loc.English.cs`): el ESPAÑOL es la clave. `Loc.T("Jugar")`, `Loc.F("Turno de {0}", x)`. Todo lo que pasa por `TrenchGUI` (Title/Label/Small/Button/Choice/Toggle/Slider) se traduce solo. Textos de la escena: `LocText` (lo instala `MainMenuController` en todo el canvas; si el código cambia el texto, lo retraduce). Idioma en PlayerPrefs `opt_lang` (por defecto, el del sistema); selector ES/EN en el menú principal y en Opciones. En el editor, `[Loc] Sin traducción: "..."` avisa de lo que falta en el diccionario.
- **Textos por red**: el servidor manda SIEMPRE español; se traduce al MOSTRARLO (`TurnHUD.Announce`, `WorldDamagePopup.SpawnImpact`, resumen de reglas del lobby) con `Loc.Show`. Si llevan nombres: `Loc.Pack("¡{0} toma el relevo!", nombre)` (plantilla + datos; los datos también se traducen si están en el diccionario, p. ej. "Rojos"). `TeamDisplayName(i, translate: false)` para mandar el nombre crudo.
- **Aspecto** (`TrenchGUI` rehecho): paneles de caja de munición (chapa oliva, latón, tornillos) y botones de tablón que se levantan al pasar el ratón; fuentes **Black Ops One** (plantilla) y **Special Elite** (máquina de escribir) en `Resources/Fonts` (licencias OFL/Apache en `LICENCIAS.txt`). Para TextMeshPro hay assets ESTÁTICOS `Resources/Fonts/* SDF.asset` (crear la fuente TMP al vuelo salía con letras basura). ⚠ TMP: **primero la fuente y después el contorno**; si pones contorno y luego cambias de fuente, las letras salen del atlas viejo (basura). Textos dentro de paneles ocultos: contorno con `OutlineWhenShown`.
- **Menú principal** (`MainMenuLook`): poste de trinchera a la derecha con tablones-flecha pixel-art (`PlankHover`: crecen, se balancean y crujen), cartel "CUARTEL GENERAL", selector ES/EN arriba a la derecha, panel multijugador ordenado en una caja de munición. `PinToCanvas` coloca el poste respecto al canvas (el `MainPanel` de la escena es más ancho que la pantalla).
- **Victoria** (`VictoryOverlay`, lo enciende `VictoryScreen`): cartel con frase, ganador, tabla por equipo (bajas, daño hecho/recibido, vivas) y mejor golpe — `NetworkTurnManager.Stats.cs` las apunta en el servidor (`NetworkHealth.OnDamagedServer`, daño atribuido a la rata del turno) y las manda con `StatsClientRpc` → `MatchStats.Last`. **Revancha**: local/práctica → `RematchRunner` (apaga la red, pasa un instante por el MENÚ y arranca otra igual; rearrancar el host dentro de la escena de juego deja a Netcode con objetos destruidos); online → el host vuelve a la sala con todos.
- `Mi equipo`: campos con texto de ejemplo (`TrenchGUI.Field`) y aspecto descrito con `RatLook.Describe()` (traducido).
- Prueba: `tools\autotest.ps1 -Role menus` (U00–U08: menú, multijugador, jugar, mi equipo, opciones, arsenal, pausa, VICTORIA y REVANCHA) y `-Lang en` para las capturas en inglés (no toca las preferencias del jugador: `Loc.SetSessionLanguage`).

## Convenciones
- Comentarios abundantes en español con `/// <summary>` explicando el *porqué* (el usuario aprende leyéndolos). Mantener ese estilo.
- `[Header]` + `[Tooltip]` en español en todos los campos del Inspector.
- Al borrar un script o prefab, borrar también su `.meta` y comprobar que ningún `.prefab`/`.unity` referencia su GUID.

## Prueba automática (ver el juego sin jugar)
Si Unity tiene el proyecto abierto, `tools\autotest.ps1` compila desde una COPIA en `%LOCALAPPDATA%\Temp\claude\ratms_build` (no hace falta cerrar Unity). Ojo: los `.meta` de scripts nuevos se generan en la copia; cópialos al repo antes de commitear.
`powershell -ExecutionPolicy Bypass -File tools\autotest.ps1` (Unity cerrado; `-SkipBuild` reutiliza la build, `-Biome Sewer` fuerza bioma).
Compila la build y el juego se juega solo (~90 s, host local sin internet): mide andar/saltar/cuestas con números, saca primeros planos con las líneas de los colliders dibujadas (`*_colliders.png`: terreno magenta, rata cian), prueba a salir de cráteres, comprueba el viento con misiles de prueba, dispara bazooka y granada, y prueba dinamita, bate y agua contra una **rata diana** (`NetworkPlayerMovement.SpawnNextAsTestDummy`: no cuenta para turnos ni victoria). Deja ~20 capturas + `informe.txt` (línea de tiempo, ✓/⚠, errores y warnings) en `AutoTest\Run_<fecha>\`. Mira las PNG con Read. Al añadir armas o mecánicas, amplía el guion en `Assets/Scripts/Testing/AutoPlayTest.cs` (hay ganchos `TestInputX`, `TestPressJump`, `TestPressFire`, `TestSetAim` en la rata).
Resultado de referencia (13 nov 2026): `Resultado: OK`, 0 errores, 0 warnings.
También prueba escopeta, teletransporte y cuerda. La prueba normal usa 1 rata por jugador (`MatchRules.OverrideRatsPerTeam = 1`). Otros papeles (`-autoTestRole`, o `tools\autotest.ps1 -Role local`): `local` (partida local 3 equipos × 2 ratas: rotación, solo se mueve la rata del turno, muerte súbita a los 35 s, eliminación y victoria), `practice` (3 dianas → ESC → menú) y **multijugador real** (2 ratas por jugador): `powershell -ExecutionPolicy Bypass -File tools\autotest-multi.ps1` (host + cliente por 127.0.0.1: turnos, viento igual en ambos, dinamita vista en el cliente, el host gana si el cliente se va). Las pruebas usan `CurrentSurfaceY` (suelo real con cráteres) y buscan línea de tiro/destinos válidos; algunos resultados (salir de cráteres) dependen del mapa aleatorio.

## Git
- Rama de trabajo: `Main` (sigue a `origin/Main`, repo `darkkikilord/Ratms`).
- **Flujo acordado**: trabajar directamente en `Main`. Tras cada tarea que compile → commit + `git push` sin pedir confirmación. Si algo sale mal se revierte con git. Nunca `push --force`.
- Compilación rápida: `tools\compilar-rapido.ps1`. Compilación completa de Unity (Unity debe estar cerrado): `B:\Juegos\6000.0.50f1\Editor\Unity.exe -batchmode -nographics -quit -projectPath <repo> -logFile <log>` y buscar `error CS` en el log.
- `origin/Pre-multipalyer` es un intento antiguo de multijugador de otro colaborador (Guillermo, ago 2025); **no fusionar**.
- Commits con versión: `Ratms 0.X: resumen`. Último: 0.19 (menús, idiomas, terreno Worms y arreglos). Cada versión cerrada lleva etiqueta git `v0.X` y un .exe jugable en `Documentos\Ratms Versiones\Ratms 0.X (...)`.
- Las builds exportadas van fuera del repo, en `Documents/Ratms Versiones`.

## Plan por versiones (lo eligió el usuario: un bloque por versión, en este orden)
1. **0.15 — Partida de verdad** ✅ equipos de 4 ratas con rotación automática, barras de vida por equipo, reglas (`MatchRules`), muerte súbita a los 10 min, partida local en el mismo PC.
2. **0.16 — Economía de armas** ✅ munición limitada, cajas (armas/botiquín/trampa, "Cambiar rata"), barriles en cadena. (En 0.16 también: ratas por piezas, armas pixel-art, menús 4, música/avisos 5, lobby 6.)
   **0.17 — Mutilaciones** ✅ (petición del usuario, fuera del plan).
   **0.18 — Arsenal y fuego** ✅ (petición del usuario): lanzallamas, metralleta, bomba de racimo, ataque aéreo y napalm; fuego real; mano bien puesta; salir de agujeros (escalar + voltereta); spawn en buenos sitios; arreglos de red de 0.17.1 (menú al volver de partida, rata rival glicheada).
   **0.19 — Menús, idiomas y terreno Worms** ✅ (petición del usuario): bloque 4 (menús bonitos, opciones con idioma, estadísticas y revancha) + inglés con selector + terreno estilo Worms + arreglos de multijugador (voltereta visible, daño de caída tras explosión, auto-daño del misil, alcantarilla desincronizada, ratas en cuevas cerradas) + granada más floja + explosiones 3× más baratas.
3. ~~Jugar solo: IA de ratas enemigas~~ ❌ DESCARTADO por el usuario (14 nov 2026): el juego NO tendrá IA. No proponerla.
4. Menús ✅ (0.19). Queda: elegir sombrero/cola pieza a pieza en "Mi equipo".
5. Presentación (← SIGUIENTE): música, anuncios, cámara inteligente, más biomas de trinchera, animaciones.
6. Online pulido: lobby con "listo", reglas del host, reconexión, revancha.
7. Publicar: icono, pantalla de carga, itch.io.

## Estado y roadmap (nov 2026)
Hecho: multijugador con Relay, bazooka, granada, dinamita, bate, trayectoria (se corta contra el terreno), viento, HUD con cuenta atrás, terreno destructible procedural, biomas, agua mortal, daño por caída, pantalla de victoria, nombres de jugador, un arma por turno.

Pendiente (detalle en `create_roadmap_issues.sh`; aún no se han creado las issues en GitHub):
- **Probar en partida real (2 clientes)**: espera del turno hasta el impacto; victoria por desconexión; que la dinamita se vea como dinamita en el cliente (no host).
- **Ajustar a mano (Inspector)**: fuerza del viento (`maxWindAcceleration` 3.5), empujón del bate (14), radio/mecha de la dinamita (4 u / 5 s), zoom inicial (6).
- Escenario de producción (ahora solo `Escenario_TerrainTest`); islas flotantes con collider propio (`TerrainGenerator.cs`).
- Probar en partida real las armas 0.18 en el CLIENTE (llamas y avión se simulan en cada PC).
- Probar en partida real (0.19): la voltereta del rival se ve, avisos traducidos con un PC en español y otro en inglés, la alcantarilla ya no se desincroniza.

Problemas conocidos menores:
- `Assets/Prefabs/armas/misil_0.prefab` (legacy) conserva un campo `damagePopupPrefab` huérfano; Unity lo ignora.
