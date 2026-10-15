$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot 'PresentMon.exe'
$expected = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
if (!(Test-Path -LiteralPath $target)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' -OutFile $target
}
if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw 'PresentMon SHA-256 mismatch. Refusing to package or execute.' }
Write-Output 'PresentMon 2.6.0 SHA-256 verified.'
