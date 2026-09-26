# Generates the original FileCat icon (folder with cat ears) as PNGs and a multi-size .ico.
# Windows PowerShell / pwsh on Windows (System.Drawing). Re-run only when the artwork changes.
param([string]$OutDir = "$PSScriptRoot\..\src\FileCat.App\Assets")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $k = $s / 256.0
  function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($x * $k)), ([single]($y * $k)) }
  # Cat ears (behind the folder tab)
  $ear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 222, 140, 24))
  $g.FillPolygon($ear, [System.Drawing.PointF[]]@((P 40 104), (P 64 30), (P 102 92)))
  $g.FillPolygon($ear, [System.Drawing.PointF[]]@((P 154 92), (P 192 30), (P 216 104)))
  $inner = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 196, 120))
  $g.FillPolygon($inner, [System.Drawing.PointF[]]@((P 58 96), (P 68 58), (P 88 90)))
  $g.FillPolygon($inner, [System.Drawing.PointF[]]@((P 168 90), (P 188 58), (P 198 96)))
  # Folder body
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $r = 22 * $k
  $rect = New-Object System.Drawing.RectangleF ([single](20 * $k)), ([single](84 * $k)), ([single](216 * $k)), ([single](150 * $k))
  $path.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
  $path.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
  $path.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
  $path.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
  $path.CloseFigure()
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 255, 190, 60)), ([System.Drawing.Color]::FromArgb(255, 236, 142, 24)), 90.0
  $g.FillPath($grad, $path)
  # Eyes and nose: two dense-list "rows" read as a face at small sizes
  $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 40, 44, 52))
  $g.FillEllipse($dark, [single](78 * $k), [single](136 * $k), [single](28 * $k), [single](34 * $k))
  $g.FillEllipse($dark, [single](150 * $k), [single](136 * $k), [single](28 * $k), [single](34 * $k))
  $g.FillPolygon($dark, [System.Drawing.PointF[]]@((P 116 184), (P 140 184), (P 128 198)))
  if ($s -ge 48) {
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 40, 44, 52)), ([single](5 * $k))
    $g.DrawLine($pen, (P 60 196), (P 100 190)); $g.DrawLine($pen, (P 156 190), (P 196 196))
  }
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs[$s] = $ms.ToArray()
  if ($s -eq 256) { [IO.File]::WriteAllBytes("$OutDir\filecat.png", $pngs[$s]) }
  $bmp.Dispose()
}
# ICO container with PNG-compressed images (supported since Windows Vista)
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
  $d = $pngs[$s]
  $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$d.Length); $w.Write([uint32]$offset)
  $offset += $d.Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()
[IO.File]::WriteAllBytes("$OutDir\filecat.ico", $out.ToArray())
"icon written to $OutDir"
