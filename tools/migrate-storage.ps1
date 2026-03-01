$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$newRoot = Join-Path $repoRoot '.subtitleguardian'
$oldRoot = Join-Path $env:LOCALAPPDATA 'SubtitleGuardian'

Write-Host "Old root: $oldRoot"
Write-Host "New root: $newRoot"

if (!(Test-Path $oldRoot)) {
  Write-Host "Nothing to migrate (old root not found)."
  exit 0
}

New-Item -ItemType Directory -Force -Path $newRoot | Out-Null

$items = @('models', 'runtime', 'cache')
foreach ($name in $items) {
  $src = Join-Path $oldRoot $name
  if (!(Test-Path $src)) { continue }

  $dst = Join-Path $newRoot $name
  New-Item -ItemType Directory -Force -Path $dst | Out-Null

  Move-Item -Path (Join-Path $src '*') -Destination $dst -Force -ErrorAction SilentlyContinue
}

try {
  Remove-Item -Path $oldRoot -Recurse -Force
} catch {
}

Write-Host "Migration done."

