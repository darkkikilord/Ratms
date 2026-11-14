#!/bin/bash
# ================================================================
#  Ratms — Crear en GitHub las issues de lo que QUEDA por hacer
#  (actualizado en 0.19: la lista antigua estaba casi toda hecha)
# ================================================================
#
#  Requisitos (una sola vez):
#    1) Instalar GitHub CLI:  winget install --id GitHub.cli
#    2) Autenticarse:         gh auth login
#
#  Uso (desde la raíz del repo, en Git Bash):
#      ./create_roadmap_issues.sh
#
#  Las labels no se duplican; las issues SÍ, así que ejecútalo una vez.
# ================================================================

set -e
REPO="darkkikilord/Ratms"

create_label() {
  gh label create "$1" --color "$2" --description "$3" --repo "$REPO" 2>/dev/null || echo "  · label '$1' ya existe"
}

echo ">>> Labels..."
create_label "feature"       "a2eeef" "Algo nuevo"
create_label "probar"        "fbca04" "Hay que probarlo en partida real"
create_label "plan-versiones" "1d76db" "Bloque del plan por versiones (CLAUDE.md)"

issue() {
  gh issue create --repo "$REPO" --title "$1" --label "$2" --body "$3"
}

echo ">>> Issues..."

issue "[0.20] Jugar solo: IA de ratas enemigas con dificultades" "feature,plan-versiones" \
"Bloque 3 del plan. Fácil: se mueve poco, apunta con mucho error y sin viento. Normal: calcula el tiro de bazooka/granada con algo de fallo. Difícil: compensa el viento, elige arma según distancia (bate de cerca, ataque aéreo si estás a cubierto) y evita agua y barriles. La IA es un equipo más en partida local."

issue "[Probar] 0.19 en multijugador real" "probar" \
"- La voltereta del rival se ve en el otro PC.
- Una rata lanzada por una explosión en el turno de otro se hace daño al caer.
- El bazucazo a los pies hace daño al que dispara.
- Alcantarilla: host y cliente ven el mismo mapa (antes el cliente veía las ratas bajo tierra).
- Un PC en español y otro en inglés: cada uno ve los avisos en su idioma.
- Revancha online: el host vuelve a la sala con todos."

issue "Mi equipo: elegir casco, cola, ojo... pieza a pieza" "feature" \
"Ahora 'Otra' sortea la rata entera. Que se pueda elegir cada pieza de RatLook (casco, cola, orejas, ojo, boca, marcas) con flechas en el editor."

issue "[Bloque 5] Presentación: cámara inteligente, más biomas de trinchera, animaciones" "feature,plan-versiones" \
"Cámara que sigue proyectiles y encuadra a los implicados; biomas nuevos (trinchera nevada, ciudad en ruinas...) con WormsTerrainShape; animaciones de victoria/derrota."

issue "[Bloque 6] Online pulido: reconexión" "feature,plan-versiones" \
"Si un cliente se cae, que pueda volver a la misma partida con su equipo (ahora queda eliminado)."

issue "[Bloque 7] Publicar en itch.io" "feature,plan-versiones" \
"Icono, pantalla de carga, build limpia (sin AutoPlayTest), página de itch.io con capturas."

echo ">>> Hecho."
