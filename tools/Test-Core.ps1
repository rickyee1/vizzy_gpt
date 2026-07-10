$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root 'VizzyGPT.sln') --configuration Release
