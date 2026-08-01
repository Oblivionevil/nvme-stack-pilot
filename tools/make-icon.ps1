# Erzeugt app.ico: blaue Kachel mit Kippschalter-Glyphe.
# Kleine Groessen als BMP/DIB (maximal kompatibel), 256er als PNG (sonst 256 KB).
Add-Type -AssemblyName System.Drawing

$OutPath = $args[0]
if (-not $OutPath) { throw "Zielpfad fehlt" }

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x,           $y,           $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y,           $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d,   0, 90)
    $p.AddArc($x,           $y + $h - $d, $d, $d,  90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Kachel
    $tile = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 96, 154, 255),
        [System.Drawing.Color]::FromArgb(255, 44, 110, 226),
        90.0)
    $g.FillPath($grad, $tile)

    # Kippschalter: weisse Bahn
    $tw = $s * 0.62
    $th = $s * 0.34
    $tx = ($s - $tw) / 2
    $ty = ($s - $th) / 2
    $track = New-RoundedPath $tx $ty $tw $th ($th / 2)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($white, $track)

    # Knauf rechts, in der Kachelfarbe ausgestanzt
    $kd = $th * 0.62
    $kx = $tx + $tw - ($th - $kd) / 2 - $kd
    $ky = $ty + ($th - $kd) / 2
    $knob = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 58, 123, 238))
    $g.FillEllipse($knob, $kx, $ky, $kd, $kd)

    $grad.Dispose(); $white.Dispose(); $knob.Dispose()
    $tile.Dispose(); $track.Dispose(); $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms

    # BITMAPINFOHEADER - Hoehe doppelt, weil XOR- und AND-Flaeche zusammen gezaehlt werden
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1);  $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($w * $h * 4)); $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    # XOR-Flaeche: BGRA, von unten nach oben
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    # AND-Maske: 1 bpp, Zeilen auf 4 Byte aufgefuellt, durchgehend 0 (Alpha regelt die Transparenz)
    $stride = [int][math]::Floor(($w + 31) / 32) * 4
    $zeros = [byte[]]::new($stride * $h)
    $bw.Write($zeros)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    # Komma verhindert, dass PowerShell das Array beim Zurueckgeben entrollt
    return ,$bytes
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $data = if ($s -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $images += [pscustomobject]@{ Size = $s; Data = $data }
    $bmp.Dispose()
}

$fs = New-Object System.IO.FileStream $OutPath, ([System.IO.FileMode]::Create)
$w = New-Object System.IO.BinaryWriter $fs

# ICONDIR
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Data.Length)
    $w.Write([uint32]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Data) }

$w.Flush(); $w.Dispose(); $fs.Dispose()

"{0} geschrieben - {1} Groessen, {2} Bytes" -f $OutPath, $images.Count, (Get-Item $OutPath).Length
