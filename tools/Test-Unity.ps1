$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'unity\VizzyGPT'
$artifacts = Join-Path $root 'artifacts'
$results = Join-Path $artifacts 'editmode-results.xml'
$log = Join-Path $artifacts 'unity-editmode.log'
$unity = 'D:\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe'

if (-not (Test-Path -LiteralPath $unity)) {
    throw "Unity editor not found: $unity"
}

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$arguments = @(
    '-batchmode',
    '-nographics',
    '-projectPath', "`"$projectPath`"",
    '-runTests',
    '-testPlatform', 'EditMode',
    '-testResults', "`"$results`"",
    '-logFile', "`"$log`""
)
$process = Start-Process -FilePath $unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
$exitCode = $process.ExitCode

if ($exitCode -ne 0) {
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Tail 120
    }

    throw "Unity EditMode tests failed with exit code $exitCode. See $log"
}

if (-not (Test-Path -LiteralPath $results)) {
    throw "Unity did not produce test results: $results"
}

[xml]$report = Get-Content -LiteralPath $results
$run = $report.'test-run'
if ($run.result -ne 'Passed') {
    throw "Unity EditMode test result was '$($run.result)'. See $results"
}

Write-Host "Unity EditMode tests passed: $($run.passed)/$($run.total)"
