<#
  PUBLICAR EN ITCH.IO (Ratms)

  Sube la build de Windows a tu página de itch.io con "butler", la
  herramienta oficial de itch. Solo sube lo que ha cambiado (los parches
  pesan poco) y tus amigos, con la app de itch, reciben la versión nueva
  automáticamente.

  La primera vez:
    1. Crea la página del juego en https://itch.io/game/new
       (tipo "Downloadable", y en "Visibility" deja "Restricted" o "Draft"
       si solo quieres que entren tus amigos con el enlace).
    2. Ejecuta este script con tu usuario y el nombre de la página:
         powershell -ExecutionPolicy Bypass -File tools\publicar-itch.ps1 -Usuario TU_USUARIO -Juego ratms
       Se descarga butler y te abre el navegador para iniciar sesión
       (solo la primera vez). El usuario y el juego quedan guardados en
       tools\publicar-itch.config para las siguientes.

  Las siguientes veces:
    powershell -ExecutionPolicy Bypass -File tools\publicar-itch.ps1
  (compila la build con la prueba de menús y la sube con la versión del juego,
  p. ej. "v0.30"). Con -SkipBuild sube la build que ya hay en AutoTest\Build.
#>
param(
    [string]$Usuario,
    [string]$Juego,
    [string]$Canal = "windows",
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $PSScriptRoot "publicar-itch.config"

# --- Usuario y juego (se recuerdan) ---
if (Test-Path $configPath) {
    $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
    if (-not $Usuario) { $Usuario = $cfg.usuario }
    if (-not $Juego) { $Juego = $cfg.juego }
}
if (-not $Usuario -or -not $Juego) {
    Write-Host "Falta tu usuario de itch.io y el nombre de la página del juego." -ForegroundColor Yellow
    Write-Host "Ejemplo: powershell -ExecutionPolicy Bypass -File tools\publicar-itch.ps1 -Usuario pepito -Juego ratms"
    exit 1
}
@{ usuario = $Usuario; juego = $Juego } | ConvertTo-Json | Set-Content $configPath -Encoding utf8

# --- Versión del juego (la del menú principal) ---
$dressing = Get-Content (Join-Path $repo "Assets\Scripts\Core\MenuDressing.cs") -Raw
$version = if ($dressing -match 'Version\s*=\s*"v?([^"]+)"') { $Matches[1] } else { "dev" }

# --- butler (se descarga una vez) ---
$butlerDir = Join-Path $env:LOCALAPPDATA "Ratms\butler"
$butler = Join-Path $butlerDir "butler.exe"
if (-not (Test-Path $butler)) {
    Write-Host "Descargando butler (la herramienta de subida de itch.io)..."
    New-Item -ItemType Directory -Force $butlerDir | Out-Null
    $zip = Join-Path $butlerDir "butler.zip"
    Invoke-WebRequest "https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default" -OutFile $zip
    Expand-Archive $zip -DestinationPath $butlerDir -Force
    Remove-Item $zip
}

# --- ¿Sesión iniciada? (la primera vez abre el navegador) ---
& $butler login
if ($LASTEXITCODE -ne 0) { Write-Host "No se pudo iniciar sesión en itch.io." -ForegroundColor Red; exit 1 }

# --- Build ---
$build = Join-Path $repo "AutoTest\Build"
if (-not $SkipBuild) {
    Write-Host "Compilando la build (con la prueba de menús)..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "autotest.ps1") -Role menus | Select-String "Resultado"
}
if (-not (Test-Path (Join-Path $build "Ratms.exe"))) { Write-Host "No hay build en $build" -ForegroundColor Red; exit 1 }

# Copia limpia, sin la carpeta de depuración de Burst (no se reparte)
$stage = Join-Path $env:TEMP "ratms_itch"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Get-ChildItem $build | Where-Object { $_.Name -notlike "*DoNotShip*" } | Copy-Item -Destination $stage -Recurse

# --- Subir ---
Write-Host "Subiendo Ratms v$version a $Usuario/$Juego`:$Canal ..." -ForegroundColor Cyan
& $butler push $stage "$Usuario/$Juego`:$Canal" --userversion $version
if ($LASTEXITCODE -ne 0) { Write-Host "La subida falló." -ForegroundColor Red; exit 1 }
Write-Host ""
Write-Host "¡Subido! Página: https://$Usuario.itch.io/$Juego" -ForegroundColor Green
Write-Host "Tus amigos con la app de itch recibirán la v$version solos."
