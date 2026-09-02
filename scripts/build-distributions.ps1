param(
    [string]$Version = '0.2.0-rc1',
    [string]$GameBuild = '24960315',
    [Parameter(Mandatory)]
    [string]$BepInExZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$work = Join-Path $dist '_build'
$portable = Join-Path $work 'portable'
$baseName = "PLATONICA_SPACE_KR_${Version}_build${GameBuild}"
$portableZip = Join-Path $dist ($baseName + '_portable.zip')
$installerExe = Join-Path $dist ($baseName + '_setup.exe')

if (-not (Test-Path -LiteralPath $BepInExZip)) { throw "BepInEx ZIP not found: $BepInExZip" }
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
if (Test-Path -LiteralPath $portableZip) { Remove-Item -LiteralPath $portableZip -Force }
if (Test-Path -LiteralPath $installerExe) { Remove-Item -LiteralPath $installerExe -Force }
New-Item -ItemType Directory -Force -Path $portable | Out-Null

Expand-Archive -LiteralPath $BepInExZip -DestinationPath $portable -Force
Copy-Item -LiteralPath (Join-Path $repo 'package\BepInEx\plugins\KR.LanguageFontPoc') -Destination (Join-Path $portable 'BepInEx\plugins') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $portable -Force
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "C# compiler not found: $csc" }
& $csc /nologo /target:winexe /optimize+ "/out:$installerExe" "/win32manifest:$repo\installer\app.manifest" "/resource:$portableZip,payload.zip" "/reference:$framework\System.IO.Compression.dll" "/reference:$framework\System.IO.Compression.FileSystem.dll" /reference:System.Windows.Forms.dll (Join-Path $repo 'installer\Installer.cs')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerExe)) { throw 'Installer compilation failed.' }

Get-FileHash -Algorithm SHA256 -LiteralPath $portableZip,$installerExe
