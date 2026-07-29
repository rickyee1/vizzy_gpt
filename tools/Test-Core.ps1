$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'VizzyGPT.sln'
$dotnet = $null

if ($env:DOTNET_ROOT) {
    $candidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $dotnet = $candidate
    }
}

if (-not $dotnet) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $dotnet = $candidate
    }
}

if (-not $dotnet) {
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        $dotnet = $command.Source
    }
}

if (-not $dotnet) {
    throw 'Unable to find dotnet.exe via DOTNET_ROOT, LOCALAPPDATA, or PATH.'
}

if ($env:CODEX_SHELL -eq '1') {
    & $dotnet build $solution --configuration Release
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $runtimeConfig = Join-Path $root 'tests\VizzyGPT.Core.Tests\bin\Release\net8.0\VizzyGPT.Core.Tests.runtimeconfig.json'
    $runtimeConfigJson = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
    $runtimeConfigJson.runtimeOptions.configProperties |
        Add-Member -NotePropertyName 'MSTest.EnableParentProcessQuery' -NotePropertyValue $false -Force
    $runtimeConfigJson | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $runtimeConfig -Encoding ascii

    $testHost = Join-Path $root 'tests\VizzyGPT.Core.Tests\bin\Release\net8.0\testhost.exe'
    $disabledTestHost = "$testHost.disabled"
    if (Test-Path -LiteralPath $disabledTestHost) {
        Remove-Item -LiteralPath $disabledTestHost -Force
    }
    Rename-Item -LiteralPath $testHost -NewName 'testhost.exe.disabled'

    & $dotnet test $solution --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    exit 0
}

& $dotnet test $solution --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
