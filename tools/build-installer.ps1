$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$issFile = Join-Path $repoRoot 'SubtitleGuardian_Setup.iss'
$publishDir = Join-Path $repoRoot 'Publish'
$outputDir = Join-Path $repoRoot 'Installer'

Write-Host "Checking for Inno Setup compiler..."

$isccPath = $null
$candidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

foreach ($path in $candidates) {
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if ($null -eq $isccPath) {
    # Check PATH
    try {
        $cmd = Get-Command "ISCC" -ErrorAction SilentlyContinue
        if ($null -ne $cmd) {
            $isccPath = $cmd.Source
        }
    } catch {}
}

if ($null -eq $isccPath) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not found."
    Write-Host "Please install Inno Setup from: https://jrsoftware.org/isdl.php"
    Write-Host "After installing, run this script again or right-click '$issFile' and select 'Compile'."
    exit 1
}

Write-Host "Found ISCC at: $isccPath"
Write-Host "Compiling installer..."

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Run ISCC
& $isccPath $issFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "Installer created successfully in: $outputDir"
} else {
    Write-Error "Failed to create installer. Exit code: $LASTEXITCODE"
}
