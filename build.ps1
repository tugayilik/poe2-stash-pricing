# Builds dist\PoeStashPricer.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# Nothing needs to be installed.
param([string]$Name = 'PoeStashPricer')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    "/out:$out\$Name.exe" "/win32manifest:$root\src\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "OK -> $out\$Name.exe"


