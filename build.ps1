param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\ShipDesign.App\ShipDesign.App.csproj"
$output = Join-Path $root $OutputDir

Write-Host "Publishing ShipDesign.App ($Configuration, $Runtime) -> $output" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish a échoué (code $LASTEXITCODE)"
}

# Copie la bibliothèque de pièces à côté de l'exe pour que le build publié
# reste utilisable tel quel (PartLibrary la cherche en remontant depuis l'exe).
$partsSource = Join-Path $root "Assets\Parts"
$partsTarget = Join-Path $output "Assets\Parts"
if (Test-Path $partsSource) {
    New-Item -ItemType Directory -Force -Path $partsTarget | Out-Null
    Copy-Item -Path (Join-Path $partsSource "*") -Destination $partsTarget -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "OK -> $output\ShipDesign.App.exe" -ForegroundColor Green
