$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$testFolder = Join-Path $PSScriptRoot 'test-results'
New-Item -ItemType Directory -Path $testFolder -Force | Out-Null
$testRun = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -ArgumentList '--self-test' -WorkingDirectory $testFolder -WindowStyle Hidden -PassThru
if (!$testRun.WaitForExit(30000)) { $testRun.Kill(); throw 'Self-tests timed out.' }
if ($testRun.ExitCode -ne 0) { throw 'Self-tests failed. Check test-results.' }
Get-Content -LiteralPath (Join-Path $testFolder 'self-test-result.txt')
Get-Content -LiteralPath (Join-Path $testFolder 'telemetry-test-result.txt')
Get-Content -LiteralPath (Join-Path $testFolder 'update-test-result.txt')
$renderRun = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -ArgumentList '--ui-smoke' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
if (!$renderRun.WaitForExit(30000)) { $renderRun.Kill(); throw 'UI render timed out.' }
if ($renderRun.ExitCode -ne 0) { throw 'UI render failed.' }
Get-Content -LiteralPath (Join-Path $testFolder 'ui-test-result.txt')
Write-Output 'UI renders include top/bottom and expanded HD catalogue at both sizes.'
$updateRun = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -ArgumentList '--update-integration-test' -WorkingDirectory $testFolder -WindowStyle Hidden -PassThru
if (!$updateRun.WaitForExit(30000)) { $updateRun.Kill(); throw 'Updater integration timed out.' }
if ($updateRun.ExitCode -ne 0) { throw 'Updater integration failed. Check test-results.' }
Get-Content -LiteralPath (Join-Path $testFolder 'update-integration-result.txt')
$standaloneFolder = Join-Path $testFolder ('standalone-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $standaloneFolder | Out-Null
$standaloneExe = Join-Path $standaloneFolder 'RuneLite-Optimizer.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -Destination $standaloneExe
foreach ($testMode in @('--self-test','--ui-smoke')) {
    $standaloneRun = Start-Process -FilePath $standaloneExe -ArgumentList $testMode -WorkingDirectory $standaloneFolder -WindowStyle Hidden -PassThru
    if (!$standaloneRun.WaitForExit(30000)) { $standaloneRun.Kill(); throw 'Standalone EXE test timed out.' }
    if ($standaloneRun.ExitCode -ne 0) { throw 'Standalone EXE test failed.' }
}
Write-Output 'PASS: EXE alone, without adjacent tools, catalogue, documentation or archive.'
