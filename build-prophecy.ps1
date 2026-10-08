$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $root "publish/Prophecy-$stamp/OmenCore-Plus-Prophecy-Unified"
& dotnet publish (Join-Path $root 'src/OmenCoreApp/OmenCoreApp.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $out -v minimal
if ($LASTEXITCODE -ne 0) { throw 'OmenCore publish failed.' }
& dotnet publish (Join-Path $root 'src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $out -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Hardware worker publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'docs/PROPHECY-INTEGRATION.md') -Destination (Join-Path $out 'README-Prophecy.md')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $out
Copy-Item -LiteralPath (Join-Path $root 'src/Prophecy.Integration/notices') -Destination (Join-Path $out 'prophecy-notices') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'src/Prophecy.Integration/setup-mvolt.ps1') -Destination $out
$zip = Join-Path (Split-Path $out) 'OmenCore-Plus-Prophecy-4.4.1-unified-win-x64.zip'
Compress-Archive -LiteralPath $out -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -LiteralPath (Join-Path (Split-Path $out) 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Portable folder: $out"
Write-Host "ZIP: $zip"
