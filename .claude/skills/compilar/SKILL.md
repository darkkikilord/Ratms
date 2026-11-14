---
name: compilar
description: Comprueba que el C# de Ratms compila y corrige los errores. Usar SIEMPRE después de tocar cualquier .cs del proyecto, antes de commitear, o cuando el usuario diga "compila", "¿compila?", "mira los errores".
---

# Compilar Ratms

## 1. Compilación rápida (siempre primero, ~1-3 s)
```
powershell -ExecutionPolicy Bypass -File tools\compilar-rapido.ps1
```
- Usa MSBuild de Visual Studio. Funciona con Unity abierto o cerrado.
- Detecta scripts nuevos/borrados aunque Unity no haya regenerado los `.csproj`.
- Última línea: `Compilación rápida: OK|FALLA — N errores, M warnings`. Código de salida 0 = OK.
- `-Warnings` lista también los warnings (hay ~230 heredados; solo mirar los de archivos que hayas tocado).
- Si dice que falta `Assembly-CSharp.csproj` o MSBuild → pasa directamente al paso 2.

Corrige cada `error CS` y repite hasta 0 errores.

## 2. Compilación de Unity (antes de cerrar una versión o si tocas prefabs, red o el editor)
La rápida NO ejecuta lo que solo hace Unity: el post-procesado de Netcode (ILPP: RPCs y NetworkVariables mal declarados), importar assets, ni los `.meta`.
- Si Unity está **cerrado**: `B:\Juegos\6000.0.50f1\Editor\Unity.exe -batchmode -nographics -quit -projectPath <repo> -logFile <log>` y busca `error` en el log.
- Si Unity está **abierto** (`Get-Process Unity`): usa `tools\autotest.ps1`, que compila desde una copia (y además juega la prueba automática). Copia al repo los `.meta` de scripts nuevos que se generen en la copia.

## 3. Después
- Lo que compilar no puede verificar (sensación de juego, red real) → prueba automática (`tools\autotest.ps1 -Role ...`) o apúntalo para que lo pruebe el usuario.
- Con 0 errores → commit en español y `git push` a `Main` (flujo acordado, sin preguntar).
