# 0.36 — Prueba REAL de la partida rápida (internet + Lobby de Unity): dos copias con perfiles distintos.
# La A crea y anuncia la sala; la B (14 s después) la encuentra y entra sin código. Usa la build de AutoTest\Build.
$repo = "C:\Users\josem\Documents\GitHub\Ratms"
$exe = Join-Path $repo "AutoTest\Build\Ratms.exe"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$dirs = @((Join-Path $repo "AutoTest\Quick_${stamp}_a"), (Join-Path $repo "AutoTest\Quick_${stamp}_b"))
New-Item -ItemType Directory -Force $dirs | Out-Null
function Start-Game($dir, $profile) {
    $a = @("-ratmsAutoTest", "-autoTestRole", "quick", "-autoTestOut", "`"$dir`"", "-ratmsProfile", $profile,
        "-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540", "-logFile", "`"$(Join-Path $dir 'player.log')`"")
    Start-Process -FilePath $exe -PassThru -ArgumentList $a
}
$p1 = Start-Game $dirs[0] "ratms_prueba_a"
Start-Sleep -Seconds 14
$p2 = Start-Game $dirs[1] "ratms_prueba_b"
$deadline = (Get-Date).AddSeconds(150)
while ((-not $p1.HasExited -or -not $p2.HasExited) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
foreach ($p in @($p1, $p2)) { if (-not $p.HasExited) { Write-Host "!!! no terminó: lo cierro"; Stop-Process -Id $p.Id -Force } }
foreach ($d in $dirs) {
    Write-Host "======== $d"
    $r = Join-Path $d "informe.txt"
    if (Test-Path $r) { Get-Content $r -Encoding UTF8 | Select-String "Resultado|Motivo|Partida rápida|Errores" } else { Write-Host "sin informe" }
    Select-String -Path (Join-Path $d "player.log") -Pattern "\[Partida r|\[Relay\]|Lobby|Exception" | Select-Object -First 12 | ForEach-Object { $_.Line }
}
