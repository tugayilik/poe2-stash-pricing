# Draws src\app.ico (a gold crystal on a dark rounded square) at 16-256 px. Run once; the .ico is committed.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\app.ico'

function Draw([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $n / 256.0

    # Rounded dark square with a gold rim.
    $r = 56 * $s; $m = 8 * $s; $w = $n - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $n, $n), ([System.Drawing.Color]::FromArgb(38, 32, 22)), ([System.Drawing.Color]::FromArgb(14, 15, 19)), 60
    $g.FillPath($bg, $path)
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(214, 176, 104)), ([Math]::Max(1, 7 * $s))
    $g.DrawPath($rim, $path)

    # Crystal: four facets around the centre.
    $cx = 128 * $s; $top = 44 * $s; $bot = 212 * $s; $l = 70 * $s; $rt = 186 * $s; $mid = 112 * $s
    function P($x, $y) { New-Object System.Drawing.PointF $x, $y }
    $facets = @(
        @((P $cx $top), (P $l $mid), (P $cx $mid), [System.Drawing.Color]::FromArgb(248, 222, 160)),
        @((P $cx $top), (P $rt $mid), (P $cx $mid), [System.Drawing.Color]::FromArgb(222, 184, 110)),
        @((P $l $mid), (P $cx $bot), (P $cx $mid), [System.Drawing.Color]::FromArgb(176, 136, 70)),
        @((P $rt $mid), (P $cx $bot), (P $cx $mid), [System.Drawing.Color]::FromArgb(128, 96, 46))
    )
    foreach ($f in $facets) {
        $b = New-Object System.Drawing.SolidBrush $f[3]
        $g.FillPolygon($b, [System.Drawing.PointF[]]@($f[0], $f[1], $f[2]))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($n in $sizes) { ,(Draw $n) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($n % 256)); $bw.Write([byte]($n % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "OK -> $out"
