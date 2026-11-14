---
name: version
description: Cierra una versión de Ratms (0.X): compila, prueba, actualiza CLAUDE.md, commitea, etiqueta v0.X, sube a GitHub y guarda el .exe jugable en Documentos\Ratms Versiones. Usar cuando el usuario diga "cierra la versión", "saca la 0.X", "haz la release" o al terminar un bloque del plan.
argument-hint: "[0.X] [descripción corta]"
---

# Cerrar una versión de Ratms

El usuario avanza **un bloque del plan por versión** (lista en CLAUDE.md → "Plan por versiones") y guarda cada versión jugable "de recuerdo".

Argumentos: `$ARGUMENTS` (número y descripción). Si faltan, deduce el número de la última etiqueta (`git tag --sort=-creatordate`) y la descripción del bloque del plan; no preguntes por esto.

## Pasos
1. **Compila** con la skill `compilar` (rápida y luego la de Unity). 0 errores.
2. **Prueba automática**: `powershell -ExecutionPolicy Bypass -File tools\autotest.ps1` (más el `-Role` de lo que haya cambiado: `local`, `items`, `mutil`, `arsenal`...). Lee `informe.txt` y mira algunas capturas con Read. Si sale `⚠` o errores, arréglalos o cuéntaselo al usuario antes de seguir.
   - Esto deja la build en `AutoTest\Build\` (Ratms.exe + carpetas).
3. **CLAUDE.md**: añade una sección corta de la versión (qué hay, clases clave, cómo probarlo), marca el bloque del plan con ✅ y actualiza "Último: 0.X" en Git.
4. **Commit y etiqueta**:
   ```
   git add -A
   git commit -m "Ratms 0.X: resumen en español"
   git tag v0.X
   git push origin Main
   git push origin v0.X
   ```
   Nunca `--force`. No tocar `origin/Pre-multipalyer`.
5. **Copia jugable**: copia TODO el contenido de `AutoTest\Build\` a
   `C:\Users\josem\Documents\Ratms Versiones\Ratms 0.X (descripción corta)\`
   (nombre sin tildes, como las anteriores: "Ratms 0.18 (arsenal y fuego)"). No borres ni toques nada de lo que ya hay en `Ratms Versiones`.
6. **Resumen al usuario**: qué trae la versión, resultado de la prueba, la ruta del .exe y lo que conviene que pruebe a mano (sobre todo en multijugador real).
