<#
  Ratms — Prueba automática de MULTIJUGADOR REAL (host + cliente).

  Lanza DOS copias del juego en este PC conectadas por 127.0.0.1:
    · HOST:    crea la partida, dispara bazooka y escopeta, y comprueba
               que GANA cuando el cliente se va.
    · CLIENTE: se conecta, suelta dinamita (y comprueba que la ve como
               dinamita), y en su segundo turno se desconecta a propósito.
  Los dos apuntan cada cambio de turno con su viento (deben coincidir).

  Uso (desde la raíz del repo, con Unity CERRADO):
    powershell -ExecutionPolicy Bypass -File tools\autotest-multi.ps1
    powershell -ExecutionPolicy Bypass -File tools\autotest-multi.ps1 -SkipBuild
#>
param(
    [switch]$SkipBuild,
    [string]$Biome = "",
    [string]$UnityExe = "B:\Juegos\6000.0.50f1\Editor\Unity.exe",
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$repo     = Split-Path -Parent $PSScriptRoot
$autoDir  = Join-Path $repo "AutoTest"
$buildExe = Join-Path $autoDir "Build\Ratms.exe"
New-Item -ItemType Directory -Force $autoDir | Out-Null

if (-not $SkipBuild) {
    $buildLog = Join-Path $autoDir "build.log"
    Write-Host ">>> Compilando build..."
    $p = Start-Process -FilePath $UnityExe -NoNewWindow -Wait -PassThru -ArgumentList @(
        "-batchmode", "-quit", "-projectPath", "`"$repo`"",
        "-executeMethod", "AutoTestBuild.BuildWindows",
        "-autoTestBuildPath", "`"$buildExe`"", "-logFile", "`"$buildLog`"")
    $csErrors = Select-String -Path $buildLog -Pattern "error CS\d+" | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique
    if ($p.ExitCode -ne 0 -or $csErrors) { Write-Host "!!! La build FALLÓ"; $csErrors | ForEach-Object { Write-Host "    $_" }; exit 1 }
    Write-Host ">>> Build OK"
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$hostDir = Join-Path $autoDir "Multi_${stamp}_host"
$clientDir = Join-Path $autoDir "Multi_${stamp}_cliente"
New-Item -ItemType Directory -Force $hostDir, $clientDir | Out-Null

function Start-Game($role, $dir, $x) {
    $a = @("-ratmsAutoTest", "-autoTestRole", $role, "-autoTestOut", "`"$dir`"",
        "-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540",
        "-logFile", "`"$(Join-Path $dir 'player.log')`"")
    if ($Biome -and $role -eq "host") { $a += @("-autoTestBiome", $Biome) }   # el cliente lo recibe por red
    Start-Process -FilePath $buildExe -PassThru -ArgumentList $a
}

Write-Host ">>> Lanzando HOST y CLIENTE..."
$h = Start-Game "host" $hostDir
Start-Sleep -Seconds 2
$c = Start-Game "client" $clientDir

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((-not $h.HasExited -or -not $c.HasExited) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
foreach ($proc in @($h, $c)) { if (-not $proc.HasExited) { Write-Host "!!! Un juego no terminó a tiempo — lo cierro."; Stop-Process -Id $proc.Id -Force } }

foreach ($pair in @(@("HOST", $hostDir), @("CLIENTE", $clientDir))) {
    Write-Host ""
    Write-Host "=================== $($pair[0]) ==================="
    $r = Join-Path $pair[1] "informe.txt"
    if (Test-Path $r) { Get-Content $r -Encoding UTF8 } else { Write-Host "!!! Sin informe (mira player.log)" }
}
