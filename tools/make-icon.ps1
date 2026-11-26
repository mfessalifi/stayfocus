# Draws the app icon (a yellow sticky note) and writes assets\app.ico.
# Only needed if you want to change the icon; the generated .ico is kept in the project.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-NoteBitmap([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $n / 16.0

    $paper = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xFF, 0xE4, 0x66))
    $strip = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF2, 0xC9, 0x3A))
    $fold  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xD9, 0xAE, 0x2B))
    $ink   = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x7A, 0x62, 0x14)), ([float][math]::Max(1, $u))

    # Paper with the bottom-right corner cut off, then the folded corner.
    $body = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (1 * $u), (1 * $u)),
        (New-Object System.Drawing.PointF (15 * $u), (1 * $u)),
        (New-Object System.Drawing.PointF (15 * $u), (10.5 * $u)),
        (New-Object System.Drawing.PointF (10.5 * $u), (15 * $u)),
        (New-Object System.Drawing.PointF (1 * $u), (15 * $u)))
    $g.FillPolygon($paper, $body)
    $g.FillRectangle($strip, [float](1 * $u), [float](1 * $u), [float](14 * $u), [float](3 * $u))
    $corner = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (15 * $u), (10.5 * $u)),
        (New-Object System.Drawing.PointF (10.5 * $u), (10.5 * $u)),
        (New-Object System.Drawing.PointF (10.5 * $u), (15 * $u)))
    $g.FillPolygon($fold, $corner)

    # Two lines of "writing".
    $g.DrawLine($ink, [float](3.5 * $u), [float](7 * $u), [float](12.5 * $u), [float](7 * $u))
    $g.DrawLine($ink, [float](3.5 * $u), [float](10 * $u), [float](9 * $u), [float](10 * $u))

    $paper.Dispose(); $strip.Dispose(); $fold.Dispose(); $ink.Dispose(); $g.Dispose()
    return $bmp
}

# Small sizes are stored as classic bitmaps (widest compatibility), 256 as PNG.
function Get-DibBytes($bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$n); $bw.Write([int]($n * 2))
    $bw.Write([int16]1); $bw.Write([int16]32)
    1..6 | ForEach-Object { $bw.Write([int]0) }
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRowBytes = [int]([math]::Ceiling($n / 32.0)) * 4
    $bw.Write((New-Object byte[] ($maskRowBytes * $n)))
    $bw.Flush()
    return ,$ms.ToArray()
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 256
$images = foreach ($n in $sizes) {
    $bmp = New-NoteBitmap $n
    $bytes = if ($n -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $n; Bytes = $bytes }
}

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir 'app.ico'

$fs = [System.IO.File]::Create($outFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$img.Bytes.Length); $bw.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
$bw.Dispose(); $fs.Dispose()
"Wrote $outFile"
