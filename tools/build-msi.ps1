$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'Publish'
$installerProj = Join-Path $repoRoot 'src\SubtitleGuardian.Installer\SubtitleGuardian.Installer.wixproj'
$outputDir = Join-Path $repoRoot 'Installer'

Write-Host "Building Subtitle Guardian MSI Installer..."

# Check dotnet
if (!(Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI is not installed. Please install .NET SDK."
    exit 1
}

# 1. Publish Application
Write-Host "Publishing application..."
# Clean publish dir
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish "src\SubtitleGuardian.App\SubtitleGuardian.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# Clean PDBs
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item

# 2. Build MSI
Write-Host "Building MSI..."

# Restore and Build
dotnet build $installerProj -c Release

if ($LASTEXITCODE -ne 0) { 
    Write-Error "MSI Build failed. Ensure you have internet access to restore WixToolset packages."
    exit 1
}

# Copy output to Installer folder
$wixOutput = Join-Path $repoRoot 'src\SubtitleGuardian.Installer\bin\Release\SubtitleGuardian_Setup.msi'
if (Test-Path $wixOutput) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    Copy-Item $wixOutput -Destination $outputDir -Force
    Write-Host "MSI Installer created successfully at: $outputDir\SubtitleGuardian_Setup.msi"
} else {
    Write-Error "MSI file not found at expected location: $wixOutput"
}
