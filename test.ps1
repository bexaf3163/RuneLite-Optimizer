$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$testFolder = Join-Path $PSScriptRoot 'test-results'
New-Item -ItemType Directory -Path $testFolder -Force | Out-Null
$testRun = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -ArgumentList '--self-test' -WorkingDirectory $testFolder -WindowStyle Hidden -PassThru
if (!$testRun.WaitForExit(30000)) { $testRun.Kill(); throw 'Self-tests timed out.' }
if ($testRun.ExitCode -ne 0) { throw 'Self-tests failed. Check test-results.' }
Get-Content -LiteralPath (Join-Path $testFolder 'self-test-result.txt')
$renderRun = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\RuneLite-Optimizer.exe') -ArgumentList '--ui-smoke' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
if (!$renderRun.WaitForExit(30000)) { $renderRun.Kill(); throw 'UI render timed out.' }
if ($renderRun.ExitCode -ne 0) { throw 'UI render failed.' }
Write-Output 'UI render: 3 tabs at 2 window sizes. Inspect test-results PNGs.'
