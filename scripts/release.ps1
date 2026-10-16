# Publica una nueva versión de Basilisco en GitHub:
#   1. Compila el .exe portable (un único archivo, sin instalación).
#   2. Sube el código y crea la etiqueta vX.Y.Z.
#   3. Crea la Release en GitHub con las notas del CHANGELOG y adjunta el .exe.
#
# Uso: actualiza <Version> en src/Basilisco/Basilisco.csproj y añade la sección
# correspondiente en CHANGELOG.md, haz commit y ejecuta:  ./scripts/release.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$repo = 'sergio120602/Proyecto-Basilisco'
$project = Join-Path $root 'src/Basilisco/Basilisco.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"
$dist = Join-Path $root 'dist'

if (git -C $root status --porcelain --untracked-files=no) {
    throw 'Hay cambios sin confirmar. Haz commit antes de publicar.'
}
if (git -C $root tag --list $tag) {
    throw "La etiqueta $tag ya existe. Sube la versión en Basilisco.csproj."
}

# Notas de la versión: la sección "## [X.Y.Z]" del CHANGELOG.
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$match = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($version))\][^\n]*\n(.*?)(?=^## \[|\z)")
if (-not $match.Success) {
    throw "No hay sección [$version] en CHANGELOG.md."
}
$notes = $match.Groups[1].Value.Trim()

Write-Host "Compilando Basilisco $tag..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
dotnet publish $project -c Release -o $dist
if ($LASTEXITCODE) { throw 'Falló la compilación.' }

$asset = Join-Path $dist "Basilisco-$tag-win-x64.exe"
Move-Item (Join-Path $dist 'Basilisco.exe') $asset

Write-Host 'Subiendo código y etiqueta...' -ForegroundColor Cyan
git -C $root tag -a $tag -m "Basilisco $tag"
git -C $root push origin HEAD
git -C $root push origin $tag
if ($LASTEXITCODE) { throw 'Falló el push.' }

# Reutilizamos la credencial de GitHub que ya tiene git guardada.
$credential = "protocol=https`nhost=github.com`n" | git credential fill
$token = ($credential | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
$headers = @{ Authorization = "token $token"; 'User-Agent' = 'basilisco-release' }

Write-Host 'Creando la Release en GitHub...' -ForegroundColor Cyan
$body = @{
    tag_name   = $tag
    name       = "Basilisco $tag"
    body       = $notes
    prerelease = $version.StartsWith('0.')
} | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" `
    -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json; charset=utf-8'

$assetName = Split-Path $asset -Leaf
Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$assetName" `
    -Headers $headers -InFile $asset -ContentType 'application/octet-stream' | Out-Null

Write-Host "Publicado: $($release.html_url)" -ForegroundColor Green

# Copia para probar en la raíz del proyecto: sustituye a la de la versión anterior.
Copy-Item $asset $root -Force
foreach ($old in Get-ChildItem $root -Filter 'Basilisco-v*.exe' | Where-Object Name -ne $assetName) {
    try { Remove-Item $old.FullName -Force -ErrorAction Stop }
    catch { Write-Warning "No se pudo borrar $($old.Name) (¿está abierto?). Se borrará al publicar la próxima versión." }
}
