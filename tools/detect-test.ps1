# Runs item detection on every screenshot in samples\ and writes annotated images to samples\out\.
# The stash area comes from samples\region.txt ("x,y,w,h", full-screen pixel coordinates).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $env:TEMP 'PoeDetectTest.exe'
$src = @('SlotDetector.cs', 'Scanner.cs', 'StashLocator.cs', 'TabLibrary.cs', 'Settings.cs', 'ItemParser.cs', 'PriceService.cs', 'Native.cs', 'Log.cs', 'DigitReader.cs') | ForEach-Object { Join-Path $root "src\$_" }

& $csc /nologo /codepage:65001 "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    (Join-Path $PSScriptRoot 'DetectTest.cs') $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

& $exe (Join-Path $root 'samples')
