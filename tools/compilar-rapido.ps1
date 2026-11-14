# Compilación rápida de los scripts de Ratms (~2-10 s) sin abrir ni cerrar Unity.
#
# Usa MSBuild de Visual Studio sobre los .csproj que genera Unity, pero rehace la
# lista de archivos .cs al vuelo: así detecta scripts nuevos o borrados aunque
# Unity aún no haya regenerado los .csproj.
#
# Solo comprueba que el C# compila (errores CS). No sustituye a la build real de
# Unity: no genera la build, no importa assets ni ejecuta el código de red (ILPP).
#
# Uso:  powershell -ExecutionPolicy Bypass -File tools\compilar-rapido.ps1 [-Warnings]
param([switch]$Warnings)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) { $msbuild = & $vswhere -latest -prerelease -products * -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) { Write-Host 'No encuentro MSBuild: usa la compilación en batchmode de Unity.'; exit 2 }

$runtimeSrc = Join-Path $repo 'Assembly-CSharp.csproj'
$editorSrc  = Join-Path $repo 'Assembly-CSharp-Editor.csproj'
if (-not (Test-Path $runtimeSrc)) { Write-Host 'Falta Assembly-CSharp.csproj: abre Unity una vez (o usa batchmode) para que lo genere.'; exit 2 }

$tmp = Join-Path $env:TEMP 'ratms_quickbuild'

# Quita la lista fija de <Compile Include> y pone un comodín sobre Assets.
# Los .csproj están en .gitignore, así que estas copias no se suben.
function New-QuickProject([string]$src, [string]$dst, [string]$include, [string]$exclude) {
    $xml = Get-Content $src -Raw
    $xml = [regex]::Replace($xml, '\s*<Compile Include="[^"]*"\s*/>', '')
    $xml = $xml.Replace('Include="Assembly-CSharp.csproj"', 'Include="Ratms.Quick.csproj"')
    # Salida e intermedios en %TEMP%, para no pisar lo que usa Unity/Visual Studio.
    # Tiene que ir ANTES del Import de Microsoft.CSharp.targets: ahí se calcula TargetPath.
    $name  = [IO.Path]::GetFileNameWithoutExtension($dst)
    $xml   = [regex]::Replace($xml, '<OutputPath>[^<]*</OutputPath>', "<OutputPath>$tmp\bin\</OutputPath>")
    $xml   = $xml.Replace('<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />',
        "<PropertyGroup>`n    <IntermediateOutputPath>$tmp\obj\$name\</IntermediateOutputPath>`n  </PropertyGroup>`n  <ItemGroup>`n    <Compile Include=`"$include`" Exclude=`"$exclude`" />`n  </ItemGroup>`n  <Import Project=`"`$(MSBuildToolsPath)\Microsoft.CSharp.targets`" />")
    Set-Content -Path $dst -Value $xml -Encoding UTF8
}

$runtimeDst = Join-Path $repo 'Ratms.Quick.csproj'
$editorDst  = Join-Path $repo 'Ratms.Quick.Editor.csproj'
New-QuickProject $runtimeSrc $runtimeDst 'Assets\**\*.cs' 'Assets\**\Editor\**'
$target = $runtimeDst
if (Test-Path $editorSrc) {
    New-QuickProject $editorSrc $editorDst 'Assets\**\Editor\**\*.cs' ''
    $target = $editorDst   # compila el runtime (ProjectReference) y luego el editor
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$out = & $msbuild $target -nologo -v:q -m -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
$sw.Stop()

# Rutas cortas y sin repetir (MSBuild a veces imprime el mismo aviso dos veces).
$clean = $out | ForEach-Object { $_.Replace("$repo\", '') -replace '\s*\[[^\]]*\.csproj\]\s*$', '' } | Select-Object -Unique
$errors = @($clean | Where-Object { $_ -match ': error ' })
$warns  = @($clean | Where-Object { $_ -match ': warning ' })

$errors | ForEach-Object { Write-Host $_ }
if ($Warnings) { $warns | ForEach-Object { Write-Host $_ } }
if ($code -ne 0 -and $errors.Count -eq 0) { $clean | Select-Object -Last 20 | ForEach-Object { Write-Host $_ } }

$estado = if ($code -eq 0) { 'OK' } else { 'FALLA' }
Write-Host ("Compilación rápida: {0} — {1} errores, {2} warnings, {3:N1} s" -f $estado, $errors.Count, $warns.Count, $sw.Elapsed.TotalSeconds)
exit $code
