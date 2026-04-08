$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$proj = Join-Path $root "Application\WinVDeskEssential.csproj"
$publishDir = Join-Path $root "publish"
$dest = Join-Path $root "WinVDeskEssential.exe"

# Clean previous publish
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Building..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained false -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit 1
}

Copy-Item (Join-Path $publishDir "WinVDeskEssential.exe") $dest -Force
Write-Host "Done" -ForegroundColor Green
Write-Host "  Single exe : $dest (needs publish/ DLLs nearby)" -ForegroundColor Gray
Write-Host "  Full app   : $publishDir\WinVDeskEssential.exe" -ForegroundColor Gray
