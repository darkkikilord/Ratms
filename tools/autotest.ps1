<#
  Ratms — Prueba automática con capturas.

  1) Compila una build de Windows (Unity en modo batch, sin ventana).
  2) Lanza el juego con -ratmsAutoTest: se juega solo ~20 s, saca capturas
     PNG y escribe informe.txt (ver Assets/Scripts/Testing/AutoPlayTest.cs).
  3) Muestra el informe.

  Uso (desde la raíz del repo; si Unity tiene el proyecto abierto, compila desde una copia):
    powershell -ExecutionPolicy Bypass -File tools\autotest.ps1
    powershell -ExecutionPolicy Bypass -File tools\autotest.ps1 -SkipBuild
    powershell -ExecutionPolicy Bypass -File tools\autotest.ps1 -Biome Sewer
    powershell -ExecutionPolicy Bypass -File tools\autotest.ps1 -Role local     (partida local por equipos)

  Todo se guarda en AutoTest\ (ignorado por git):
    AutoTest\Build\Ratms.exe        la build
    AutoTest\build.log              log de compilación
    AutoTest\Run_<fecha>\*.png       capturas
    AutoTest\Run_<fecha>\informe.txt informe
#>
param(
    [switch]$SkipBuild,
    [string]$Biome = "",
    [string]$Role = "",
    [string]$Lang = "",
    [string]$UnityExe = "B:\Juegos\6000.0.50f1\Editor\Unity.exe",
    [int]$TimeoutSeconds = 320
)

$ErrorActionPreference = "Stop"
$repo     = Split-Path -Parent $PSScriptRoot
$autoDir  = Join-Path $repo "AutoTest"
$buildExe = Join-Path $autoDir "Build\Ratms.exe"
New-Item -ItemType Directory -Force $autoDir | Out-Null

# --- 1) Build -------------------------------------------------------------
if (-not $SkipBuild) {
    # Si Unity tiene el proyecto abierto no se puede compilar encima: se
    # compila desde una COPIA (Assets/Packages/ProjectSettings) en %TEMP%.
    # La primera vez tarda más (importa todo); después reutiliza su Library.
    $projectToBuild = $repo
    if (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*Ratms*" }) {
        $projectToBuild = Join-Path $env:LOCALAPPDATA "Temp\claude\ratms_build"
        Write-Host ">>> Unity tiene Ratms abierto: compilo desde una copia en $projectToBuild"
        New-Item -ItemType Directory -Force $projectToBuild | Out-Null
        foreach ($d in "Assets", "Packages", "ProjectSettings") {
            robocopy (Join-Path $repo $d) (Join-Path $projectToBuild $d) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        }
    }
    $buildLog = Join-Path $autoDir "build.log"
    Write-Host ">>> Compilando build (puede tardar unos minutos)..."
    $p = Start-Process -FilePath $UnityExe -NoNewWindow -Wait -PassThru -ArgumentList @(
        "-batchmode", "-quit",
        "-projectPath", "`"$projectToBuild`"",
        "-executeMethod", "AutoTestBuild.BuildWindows",
        "-autoTestBuildPath", "`"$buildExe`"",
        "-logFile", "`"$buildLog`""
    )
    # (Los errores de Library/PackageCache al importar por primera vez son del paquete, no nuestros.)
    $csErrors = Select-String -Path $buildLog -Pattern "error CS\d+" | Where-Object { $_.Line -notmatch "PackageCache" } | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique
    if ($p.ExitCode -ne 0 -or $csErrors) {
        Write-Host "!!! La build FALLÓ (exit $($p.ExitCode)). Errores:"
        $csErrors | ForEach-Object { Write-Host "    $_" }
        Select-String -Path $buildLog -Pattern "\[AutoTestBuild\]|Build Finished|error" | Select-Object -Last 15 | ForEach-Object { Write-Host "    $($_.Line.Trim())" }
        exit 1
    }
    Write-Host ">>> Build OK: $buildExe"
}

if (-not (Test-Path $buildExe)) { Write-Error "No existe la build en $buildExe. Ejecuta sin -SkipBuild." }

# --- 2) Ejecutar la prueba --------------------------------------------------
$runDir = Join-Path $autoDir ("Run_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Force $runDir | Out-Null

$gameArgs = @(
    "-ratmsAutoTest",
    "-autoTestOut", "`"$runDir`"",
    "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
    "-logFile", "`"$(Join-Path $runDir 'player.log')`""
)
if ($Biome) { $gameArgs += @("-autoTestBiome", $Biome) }
if ($Lang) { $gameArgs += @("-autoTestLang", $Lang) }   # en = capturas en inglés
if ($Role)  { $gameArgs += @("-autoTestRole", $Role) }   # local, practice (host/client: usa autotest-multi.ps1)

Write-Host ">>> Ejecutando prueba automática..."
$game = Start-Process -FilePath $buildExe -ArgumentList $gameArgs -PassThru
if (-not $game.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Host "!!! El juego no terminó en $TimeoutSeconds s — lo cierro."
    Stop-Process -Id $game.Id -Force
}

# --- 3) Resultado -----------------------------------------------------------
Write-Host ""
$report = Join-Path $runDir "informe.txt"
if (Test-Path $report) { Get-Content $report -Encoding UTF8 } else { Write-Host "!!! No se generó informe.txt (mira player.log)." }
Write-Host ""
Write-Host "Capturas en: $runDir"
Get-ChildItem $runDir -Filter *.png | ForEach-Object { Write-Host "  $($_.Name)" }
