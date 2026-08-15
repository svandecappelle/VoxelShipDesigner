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

Write-Host "OK -> $output\ShipDesign.App.exe" -ForegroundColor Green
