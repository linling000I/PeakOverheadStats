param(
    [string]$Configuration = "Release",
    [string]$GameDir = "E:\steam1\steamapps\common\PEAK"
)

$ErrorActionPreference = "Stop"

$project = "src\PeakOverheadStats\PeakOverheadStats.csproj"

Write-Host "Building PeakOverheadStats ($Configuration)..."
& dotnet build $project -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

$dll = "src\PeakOverheadStats\bin\$Configuration\netstandard2.1\PeakOverheadStats.dll"
$pluginsDir = Join-Path $GameDir "BepInEx\plugins"

if (Test-Path $pluginsDir) {
    Copy-Item $dll $pluginsDir -Force
    Write-Host "Deployed to $pluginsDir"
} else {
    Write-Warning "Plugins dir not found: $pluginsDir"
}

Write-Host "Done."
