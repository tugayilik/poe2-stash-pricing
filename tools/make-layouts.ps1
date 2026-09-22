# Rebuilds src\Layouts.json, the built-in tab layouts, from screenshots of each special stash tab.
# Screenshots: samples\layouts\NAME.png (2560x1440 or any size the app supports; not in the repo).
# Specs: tools\layouts\NAME.txt (name, kind and corrections; see LayoutTool.cs).
# Review pictures with every slot drawn and numbered go to samples\layouts\review\.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $env:TEMP 'PoeLayoutTool.exe'
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /codepage:65001 "/out:$exe" /main:PoeStashPricer.LayoutTool `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Join-Path $PSScriptRoot 'LayoutTool.cs') $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

& $exe (Join-Path $root 'samples\layouts') (Join-Path $PSScriptRoot 'layouts') (Join-Path $root 'samples\layouts\review') (Join-Path $root 'src\Layouts.json')
if ($LASTEXITCODE -ne 0) { throw "LayoutTool failed" }
