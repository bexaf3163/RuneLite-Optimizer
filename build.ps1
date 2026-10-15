$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework compiler not found.' }
$buildFolder = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $buildFolder -Force | Out-Null
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32manifest:$PSScriptRoot\app.manifest" "/out:$buildFolder\RuneLite-Optimizer.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Security.dll /reference:System.Xml.dll "$PSScriptRoot\src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $buildFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $buildFolder
Write-Output "Built $buildFolder\RuneLite-Optimizer.exe"
