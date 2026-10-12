# Generates assets\app.ico: an amber/orange rounded square with the white TrayDeck sun.
# Each size is drawn separately (not resized) so it stays sharp.
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "assets\app.ico"
New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    # Background: rounded square with a diagonal gradient
    $m = [Math]::Max(0.5, $s * 0.03)
    $r = $s * 0.24
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 200, 61)), ([System.Drawing.Color]::FromArgb(247, 99, 12)), 45.0
    $g.FillPath($grad, $path)

    # Sun: central disc and 8 rounded rays (thicker rays at small sizes)
    $c = $s / 2
    $white = [System.Drawing.Color]::White
    $core = $s * 0.17
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $white), ($c - $core), ($c - $core), (2 * $core), (2 * $core))
    $w = if ($s -le 24) { $s * 0.1 } else { $s * 0.065 }
    $pen = New-Object System.Drawing.Pen $white, $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $r0 = $s * 0.27; $r1 = $s * 0.36
    for ($i = 0; $i -lt 8; $i++) {
        $a = $i * [Math]::PI / 4
        $g.DrawLine($pen, [float]($c + [Math]::Cos($a) * $r0), [float]($c + [Math]::Sin($a) * $r0), [float]($c + [Math]::Cos($a) * $r1), [float]($c + [Math]::Sin($a) * $r1))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 32-bit DIB (BGRA, bottom-up rows) + empty AND mask: the classic icon format
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32](2 * $s)); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]0); $bw.Write([UInt32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $p = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A)
            }
        }
        $maskRow = [Math]::Ceiling($s / 32) * 4
        $bw.Write((New-Object byte[] ($maskRow * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO format: bitmaps for small sizes, PNG for 256 px
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) { , (Draw-Icon $s) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Output "Created $out"
