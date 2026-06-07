$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "publish\win-x64"
$scriptPath = Join-Path $PSScriptRoot "CortexQR.iss"

Write-Host "Publishing Cortex QR v1.0.0..."
dotnet publish (Join-Path $root "CortexQR.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=false

$isccCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $isccPath) {
    $defaultIscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $defaultIscc) {
        $isccPath = $defaultIscc
    }
}

if (-not $isccPath) {
    throw "ISCC.exe was not found. Install innosetup-6.7.3.exe, then run this script again."
}

Write-Host "Building installer with Inno Setup..."
& $isccPath $scriptPath

Write-Host "Done. Installer output:"
Get-ChildItem (Join-Path $PSScriptRoot "Output") -Filter "*.exe"
