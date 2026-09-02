param(
    [string]$Version = '1.0.0',
    [string]$GameBuild = '24960315',
    [Parameter(Mandatory)]
    [string]$BepInExZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$work = Join-Path $dist ("_build_" + $Version.Replace('.','_').Replace('-','_'))
$payload = Join-Path $work 'payload'
$auto = Join-Path $work 'auto'
$manual = Join-Path $work 'manual'
$baseName = "PLATONICA SPACE 한국어 패치 v${Version}"
$autoZip = Join-Path $dist 'PLATONICA SPACE 한국어 패치 (Auto).zip'
$manualZip = Join-Path $dist 'PLATONICA SPACE 한국어 패치 (Manual).zip'
$installerExe = Join-Path $auto ($baseName + '.exe')
$payloadZip = Join-Path $work 'payload.zip'
$checksums = Join-Path $dist 'SHA-256 체크섬.txt'

if (-not (Test-Path -LiteralPath $BepInExZip)) { throw "BepInEx ZIP not found: $BepInExZip" }
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
foreach ($file in @($autoZip,$manualZip,$checksums)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }
New-Item -ItemType Directory -Force -Path $payload,$auto,$manual | Out-Null

Expand-Archive -LiteralPath $BepInExZip -DestinationPath $payload -Force
Copy-Item -LiteralPath (Join-Path $repo 'package\BepInEx\plugins\KR.LanguageFontPoc') -Destination (Join-Path $payload 'BepInEx\plugins') -Recurse -Force
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $payloadZip -CompressionLevel Optimal -Force

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "C# compiler not found: $csc" }
& $csc /nologo /target:winexe /optimize+ "/out:$installerExe" "/win32manifest:$repo\installer\app.manifest" "/resource:$payloadZip,payload.zip" "/reference:$framework\System.IO.Compression.dll" "/reference:$framework\System.IO.Compression.FileSystem.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $repo 'installer\Installer.cs')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerExe)) { throw 'Installer compilation failed.' }

$autoReadme = (Get-Content -LiteralPath (Join-Path $repo 'distribution\README_Auto.txt') -Raw -Encoding UTF8).Replace('{{VERSION}}',$Version)
$manualReadme = (Get-Content -LiteralPath (Join-Path $repo 'distribution\README_Manual.txt') -Raw -Encoding UTF8).Replace('{{VERSION}}',$Version)
[IO.File]::WriteAllText((Join-Path $auto 'README.txt'),$autoReadme,[Text.UTF8Encoding]::new($false))
Copy-Item -Path (Join-Path $payload '*') -Destination $manual -Recurse -Force
[IO.File]::WriteAllText((Join-Path $manual 'README.txt'),$manualReadme,[Text.UTF8Encoding]::new($false))
Compress-Archive -Path (Join-Path $auto '*') -DestinationPath $autoZip -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $manual '*') -DestinationPath $manualZip -CompressionLevel Optimal -Force

$hashLines = Get-FileHash -Algorithm SHA256 -LiteralPath $autoZip,$manualZip | ForEach-Object { $_.Hash.ToLowerInvariant() + ' *' + (Split-Path -Leaf $_.Path) }
[IO.File]::WriteAllLines($checksums,$hashLines,[Text.UTF8Encoding]::new($false))
Get-FileHash -Algorithm SHA256 -LiteralPath $autoZip,$manualZip,$checksums
