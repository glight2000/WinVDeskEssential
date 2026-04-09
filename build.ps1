$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$proj = Join-Path $root "Application\WinVDeskEssential.csproj"
$publishDir = Join-Path $root "publish"
$dest = Join-Path $root "WinVDeskEssential.exe"

# Kill running instance if any
$proc = Get-Process -Name "WinVDeskEssential" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Stopping running WinVDeskEssential..." -ForegroundColor Yellow
    $proc | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

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
Write-Host "  Run: $publishDir\WinVDeskEssential.exe" -ForegroundColor Gray
