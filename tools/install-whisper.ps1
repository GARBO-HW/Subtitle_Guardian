$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repoRoot '.subtitleguardian'
$runtimeRoot = Join-Path $root 'runtime'
$modelsRoot = Join-Path $root 'models'

$whisperRuntimeBin = Join-Path $runtimeRoot 'whispercpp\bin'
New-Item -ItemType Directory -Force -Path $whisperRuntimeBin | Out-Null

$tmp = Join-Path $env:TEMP ('whispercpp-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest' -Headers @{ 'User-Agent' = 'SubtitleGuardian-Installer' }
$asset = $rel.assets | Where-Object { $_.name -eq 'whisper-bin-x64.zip' } | Select-Object -First 1
if ($null -eq $asset) { throw 'whisper-bin-x64.zip not found in latest release assets.' }

$zip = Join-Path $tmp $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $tmp -Force

$whisperCli = Get-ChildItem -Path $tmp -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1
$mainExe = Get-ChildItem -Path $tmp -Recurse -Filter 'main.exe' | Select-Object -First 1
if ($null -eq $whisperCli -and $null -eq $mainExe) { throw 'whisper-cli.exe/main.exe not found in extracted zip.' }

$srcDir = if ($null -ne $whisperCli) { $whisperCli.Directory.FullName } else { $mainExe.Directory.FullName }
Copy-Item -Path (Join-Path $srcDir '*') -Destination $whisperRuntimeBin -Force

Write-Host "Installed whisper.cpp runtime to: $whisperRuntimeBin"

$modelDir = Join-Path $modelsRoot 'whisper\medium@1\files'
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null
$modelPath = Join-Path $modelDir 'ggml-medium.bin'

if (!(Test-Path $modelPath) -or ((Get-Item $modelPath).Length -lt 10000000)) {
  Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin' -OutFile $modelPath
}

$sizeMb = [math]::Round(((Get-Item $modelPath).Length / 1MB), 1)
Write-Host "Installed model: $modelPath ($sizeMb MB)"

$ffmpeg = Join-Path $runtimeRoot 'ffmpeg\bin\ffmpeg.exe'
if (!(Test-Path $ffmpeg)) { throw "ffmpeg not found at: $ffmpeg" }

$testWav = Join-Path $tmp 'test.wav'
& $ffmpeg -y -v error -f lavfi -i anullsrc=r=16000:cl=mono -t 0.5 -c:a pcm_s16le $testWav

$exe = if (Test-Path (Join-Path $whisperRuntimeBin 'whisper-cli.exe')) { (Join-Path $whisperRuntimeBin 'whisper-cli.exe') } else { (Join-Path $whisperRuntimeBin 'main.exe') }
$outBase = Join-Path $tmp 'test_out'
& $exe -m $modelPath -f $testWav -of $outBase -oj -l en | Out-Null

$jsonOut = $outBase + '.json'
if (!(Test-Path $jsonOut)) { throw "Whisper test did not produce json output: $jsonOut" }

Write-Host "Whisper test OK: produced $jsonOut"
