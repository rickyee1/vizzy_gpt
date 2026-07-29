$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'src\VizzyGPT.Core\VizzyGPT.Core.csproj') --configuration Release
$source = Join-Path $root 'src\VizzyGPT.Core\bin\Release\netstandard2.1\VizzyGPT.Core.dll'
$targetDir = Join-Path $root 'unity\VizzyGPT\Assets\VizzyGPT\Plugins'
New-Item -ItemType Directory -Force $targetDir | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $targetDir 'VizzyGPT.Core.dll') -Force
