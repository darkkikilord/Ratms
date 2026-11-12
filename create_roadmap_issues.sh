#!/bin/bash
# ================================================================
#  Ratms — Script para crear el ROADMAP de issues en GitHub
# ================================================================
#
#  Requisitos (una sola vez):
#    1) Instalar GitHub CLI:  https://cli.github.com/
#       En Windows: winget install --id GitHub.cli
#       En macOS:   brew install gh
#    2) Autenticarse:  gh auth login
#       (elige GitHub.com → HTTPS → login con navegador)
#
#  Uso:
#    Desde la raíz del repo Ratms:
#      chmod +x create_roadmap_issues.sh
#      ./create_roadmap_issues.sh
#
#  Qué hace:
#    Crea las labels necesarias (si no existen) y todas las issues
#    del roadmap del multijugador. Se pueden ejecutar más de una vez
#    sin miedo: si las labels ya existen, no las duplica. Las issues
#    SÍ se duplicarán si vuelves a ejecutar, así que solo hazlo una vez.
# ================================================================

set -e

REPO="darkkikilord/Ratms"

echo ">>> Creando labels..."

# Función helper: crea label si no existe
create_label() {
  local name="$1"
  local color="$2"
  local desc="$3"
  gh label create "$name" --color "$color" --description "$desc" --repo "$REPO" 2>/dev/null || echo "  · label '$name' ya existe, saltando"
}

create_label "fase-5-cleanup"  "d4c5f9" "Cerrar cabos sueltos de la Fase 5"
create_label "fase-6-pulido"   "0e8a16" "Pulido y refinamiento de mecánicas existentes"
create_label "fase-6-contenido" "1d76db" "Contenido nuevo: armas, mecánicas, mapas"
create_label "bug"             "d73a4a" "Algo no funciona como debería"
create_label "feature"         "a2eeef" "Feature nuevo"
create_label "polish"          "fbca04" "Pulido/UX"
create_label "prioridad-alta"  "b60205" "Bloquea partidas jugables"
create_label "prioridad-media" "e99695" "Importante pero no bloquea"
create_label "prioridad-baja"  "c5def5" "Nice to have"

echo ""
echo ">>> Creando issues..."

# ================================================================
# Fase 5 — Cleanup (cerrar cabos sueltos)
# ================================================================

gh issue create --repo "$REPO" \
  --title "[Cleanup] Borrar NetworkBootstrap.cs (panel IMGUI de andamio)" \
  --label "fase-5-cleanup,polish,prioridad-baja" \
  --body "El panel IMGUI de NetworkBootstrap.cs era andamio de la Fase 2-3. En Fase 5 se sustituyó por MultiplayerMenuController integrado en MainMenu. Verificar que ningún prefab lo tiene enganchado y borrarlo (y su .meta).

**Archivos:**
- \`Assets/Scripts/Multiplayer/NetworkBootstrap.cs\`
- \`Assets/Scripts/Multiplayer/NetworkBootstrap.cs.meta\`

**Cómo verificar antes de borrar:**
Grep por 'NetworkBootstrap' en Assets/ (fuera de este script). Si nadie lo referencia, borrar."

gh issue create --repo "$REPO" \
  --title "[Cleanup] Borrar DamagePopup.cs y DamagePopup.prefab (legacy)" \
  --label "fase-5-cleanup,polish,prioridad-baja" \
  --body "El viejo sistema de popup en Canvas (que dejaba la 'T' fantasma) está sustituido por WorldDamagePopup. DamagePopup.cs es un stub que se autodestruye — se puede borrar del todo.

**Archivos a borrar:**
- \`Assets/Scripts/turns (turnos)/DamagePopup.cs\` (+ .meta)
- \`Assets/Prefabs/UI/DamagePopup.prefab\` (+ .meta)

Ya no queda ninguna referencia activa en la escena ni prefabs (limpiado en 0.9)."

gh issue create --repo "$REPO" \
  --title "[Verificar] Timer visible del turno en TurnHUD" \
  --label "fase-5-cleanup,polish,prioridad-media" \
  --body "TurnHUD.cs existe y muestra información del turno actual. Verificar que también muestra el TurnTimeRemaining en pantalla (cuenta atrás de 30s → 0). Si no, añadirlo."

# ================================================================
# Fase 6 — Pulido de mecánicas actuales
# ================================================================

gh issue create --repo "$REPO" \
  --title "[Verificar en juego] Delay de turno tras disparo funciona" \
  --label "fase-6-pulido,prioridad-alta" \
  --body "Metido en 0.9 pero no confirmado en juego. El objetivo: tras disparar, el turno NO cambia hasta que el proyectil impacte (o 3.5s de fallback). El enemigo no debe poder moverse mientras el misil vuela hacia él.

**Cómo probar:**
1. Dos clientes conectados.
2. Cliente A dispara un misil largo (bazooka con carga máxima) hacia cliente B.
3. Verificar: mientras el misil vuela, el turno sigue siendo de A (input de B bloqueado).
4. Al impactar: 1s de pausa dramática, luego turno de B.
5. Si el misil se pierde al vacío: turno cambia a los 3.5s (fallback).

Si algo falla, revisar \`NetworkPlayerMovement.ShortenEndTurnClientRpc()\` y las llamadas en \`NetworkMissile.Explode()\` / \`NetworkGrenade.Explode()\`."

gh issue create --repo "$REPO" \
  --title "[Feature] Pantalla de victoria + volver al menú" \
  --label "fase-6-pulido,feature,prioridad-alta" \
  --body "Actualmente cuando \`turnOrder.Count <= 1\` en NetworkTurnManager.RemovePlayerServer, se para gameStarted=false pero no se muestra nada. La partida queda en un limbo.

**Qué hacer:**
- Añadir un NetworkVariable<ulong> WinnerClientId en NetworkTurnManager (o -1 = empate).
- Cuando queda 1 rata viva: setear WinnerClientId al cliente ganador.
- Crear VictoryScreenUI que se muestra en todos los clientes cuando WinnerClientId cambia.
- Botón 'Volver al menú' que desconecta y vuelve a MainMenu.scene."

gh issue create --repo "$REPO" \
  --title "[Feature] Daño por caída (DamageType.Fall)" \
  --label "fase-6-pulido,feature,prioridad-media" \
  --body "\`DamageType.Fall\` ya está reservado en el enum pero no se usa. Muy Worms: si una explosión te tira alto y caes, te quita HP extra al aterrizar.

**Idea de implementación:**
- En NetworkPlayerMovement, trackear la Y máxima alcanzada mientras \`!grounded\` (después de un salto o knockback).
- Al volver a grounded, calcular \`fallHeight = maxY - currentY\`.
- Si fallHeight > threshold (~4 unidades), aplicar daño proporcional: p.ej. \`(fallHeight - 4) * 5\` HP.
- Llamar a NetworkHealth.ApplyDamageServer(dmg, pos, DamageType.Fall).

El popup ya sabe pintar Fall en color amarillo con símbolo 'v'."

gh issue create --repo "$REPO" \
  --title "[Feature] Escenario de producción (no test)" \
  --label "fase-6-pulido,polish,prioridad-media" \
  --body "Ahora mismo solo hay \`Escenario_TerrainTest.unity\` (el nombre canta que era temporal) y \`Escenario 1 obsoleto.unity\`.

**Qué hacer:**
- Crear un escenario 'de verdad' con arte finalizado, iluminación, fondo, etc.
- Renombrar o duplicar el actual como base y afinar.
- Asegurar que TerrainGenerator produce mapas jugables (revisar semillas, alturas, número de islas)."

# ================================================================
# Fase 6 — Contenido nuevo
# ================================================================

gh issue create --repo "$REPO" \
  --title "[Feature] Equipos de verdad (varias ratas por jugador)" \
  --label "fase-6-contenido,feature,prioridad-media" \
  --body "Ahora mismo cada cliente = 1 rata. En Worms real cada jugador controla 4 ratas del mismo equipo, y el turno rota entre TODAS las ratas de todos los jugadores.

**Cambios necesarios:**
- NetworkTurnManager: rotar por rata (NetworkObject), no por cliente.
- Al morir una rata, seguir vivos si al jugador le quedan más.
- Victoria = último equipo con al menos 1 rata viva (no último jugador).
- GameSceneSpawnManager: spawnear N ratas por cliente en spawnpoints separados.
- Colores de equipo (ya existe teamColors, aplicarlo a las 4 ratas del jugador)."

gh issue create --repo "$REPO" \
  --title "[Feature] Nueva arma: Dinamita" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Como la granada pero:
- No rebota (peso alto).
- Fusible más largo (5s).
- Explosión más grande (radio ~5).
- Daño mayor (~90).
- Se deja en el suelo (no se lanza con fuerza, solo se coloca)."

gh issue create --repo "$REPO" \
  --title "[Feature] Nueva arma: Escopeta" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Arma hitscan (no proyectil físico):
- Raycast desde la punta del arma en la dirección apuntada.
- Daño alto de cerca (60), poco de lejos (10) con falloff.
- Empuje horizontal en la dirección del disparo.
- 2 disparos por turno."

gh issue create --repo "$REPO" \
  --title "[Feature] Nueva arma: Bate (melee)" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Arma cuerpo a cuerpo (DamageType.Melee ya reservado):
- Rango corto (~1.5 unidades).
- Daño ~50.
- Knockback MUY fuerte en la dirección apuntada (útil para tirar enemigos al agua).
- Cero daño de zona."

gh issue create --repo "$REPO" \
  --title "[Feature] Nueva arma: Teletransporte" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Utility, no arma de daño:
- Mueve la rata a la posición del cursor (dentro de un radio).
- Cancelable si el sitio no es válido (dentro de terreno).
- No termina el turno.
- 1 uso por partida (o 1 por turno con cooldown)."

gh issue create --repo "$REPO" \
  --title "[Feature] Nueva arma: Cuerda ninja" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Utility avanzada tipo Worms:
- Se lanza en dirección apuntada.
- Si engancha en terreno: la rata queda colgando y puede oscilar (input horizontal aplica fuerza).
- Puede soltar la cuerda en cualquier momento.
- Movilidad brutal, importante para el sabor Worms."

gh issue create --repo "$REPO" \
  --title "[Feature] Viento que afecta trayectoria" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Vector de viento global que cambia cada turno:
- NetworkVariable<Vector2> Wind en un GameManager.
- Al inicio de cada turno, valor random entre -1 y 1 en X.
- Fuerza aplicada a proyectiles en su Update de física.
- Indicador visual en HUD (flecha)."

gh issue create --repo "$REPO" \
  --title "[Feature] Cajas de suministros" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "Al inicio de cada turno (con % de probabilidad) cae una caja del cielo:
- Contiene HP, arma random, o daño (bomba trampa).
- Al tocarla, se aplica el efecto al que la coge.
- Prefab con sprite + collider + NetworkObject.
- Anim de caída con paracaídas."

gh issue create --repo "$REPO" \
  --title "[Sistema futuro] Daño por fuego (DamageType.Fire)" \
  --label "fase-6-contenido,feature,prioridad-baja" \
  --body "\`DamageType.Fire\` reservado en el enum. Base para lanzallamas, napalm, o partículas de fuego que quedan tras una explosión especial.

**Idea base:**
- Objetos NetworkFireTile que aplican daño por segundo si una rata está sobre ellos.
- Se apagan tras N segundos.
- Popup ya sabe pintar Fire en naranja con símbolo '~'."

echo ""
echo ">>> ¡Listo! Todas las issues creadas."
echo ">>> Puedes verlas en: https://github.com/$REPO/issues"
