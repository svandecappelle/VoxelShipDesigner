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

# Copie les pièces et templates à côté de l'exe pour que le build publié reste
# utilisable tel quel (PartLibrary/ShipTemplateLoader les cherchent en remontant depuis l'exe).
foreach ($assetDir in @("Parts", "Templates")) {
    $source = Join-Path $root "Assets\$assetDir"
    $target = Join-Path $output "Assets\$assetDir"
    if (Test-Path $source) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "OK -> $output\ShipDesign.App.exe" -ForegroundColor Green
