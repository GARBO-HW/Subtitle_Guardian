$ErrorActionPreference = 'Stop'
$url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$dest = ".subtitleguardian\runtime\ffmpeg"
$zip = "$dest\ffmpeg.zip"

if (!(Test-Path $dest)) {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
}

Write-Host "Downloading ffmpeg..."
Invoke-WebRequest -Uri $url -OutFile $zip

Write-Host "Extracting ffmpeg..."
Expand-Archive -Path $zip -DestinationPath $dest -Force

$extracted = Get-ChildItem -Path $dest -Directory | Select-Object -First 1
Move-Item -Path "$($extracted.FullName)\bin" -Destination "$dest" -Force
Remove-Item -Path $extracted.FullName -Recurse -Force
Remove-Item -Path $zip -Force

Write-Host "FFmpeg installed to $dest\bin"
