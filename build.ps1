$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'tools\prepare.ps1')
& (Join-Path $PSScriptRoot 'tools\build-companion.ps1')
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework compiler not found.' }
$buildFolder = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $buildFolder -Force | Out-Null
$resources = @('hd-settings.tsv','README.md','LICENSE','THIRD-PARTY-NOTICES.md','HANDOFF.md','OPTIMIZATION.md') | ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $_) + ',bundle.' + $_ }
$resources += '/resource:' + (Join-Path $PSScriptRoot 'tools\PresentMon.exe') + ',bundle.PresentMon.exe'
$resources += '/resource:' + (Join-Path $PSScriptRoot 'tools\java-build\OptimizerTelemetry.jar') + ',bundle.OptimizerTelemetry.jar'
$resources += '/resource:' + (Join-Path $PSScriptRoot 'companion\launcher.cjs') + ',bundle.launcher.cjs'
$resources += '/resource:' + (Join-Path $PSScriptRoot 'companion\launch-adapter.cjs') + ',bundle.launch-adapter.cjs'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32manifest:$PSScriptRoot\app.manifest" "/out:$buildFolder\RuneLite-Optimizer.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Security.dll /reference:System.Xml.dll /reference:System.Web.Extensions.dll @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$expectedVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim() + '.0'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildFolder 'RuneLite-Optimizer.exe')).FileVersion -ne $expectedVersion) { throw 'VERSION and executable version differ.' }
Write-Output "Built single portable EXE: $buildFolder\RuneLite-Optimizer.exe"
