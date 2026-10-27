# 生成 Timline 应用图标：深色圆角方块 + 白色表盘 + 蓝色指针，输出多尺寸 PNG-in-ICO
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\Timline\Assets\app.ico"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$sizes = 256, 48, 32, 16
$blobs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角背景
    $pad = [int]($s * 0.04); $r = [int]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($s - $pad - 2 * $r, $pad, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($s - $pad - 2 * $r, $s - $pad - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($pad, $s - $pad - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 22, 27, 34))
    $g.FillPath($bg, $path)
    $stroke = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(255, 61, 68, 81)), ([single]($s * 0.02))
    $g.DrawPath($stroke, $path)

    # 表盘（10:10 指针）
    $cx = $s / 2.0; $cy = $s / 2.0; $c = $s * 0.30
    $dialPen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(225, 230, 238, 245)), ([single]($s * 0.055))
    $g.DrawEllipse($dialPen, [single]($cx - $c), [single]($cy - $c), [single](2 * $c), [single](2 * $c))

    $handPen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(255, 88, 166, 255)), ([single]($s * 0.075))
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rad = [math]::PI / 180.0
    # 时针 10 点方向（从 12 点顺时针 300 度），分针 2 点方向（60 度）
    $hourLen = $c * 0.50; $minLen = $c * 0.78
    $hx = $cx + $hourLen * [math]::Sin(300 * $rad); $hy = $cy - $hourLen * [math]::Cos(300 * $rad)
    $mx = $cx + $minLen * [math]::Sin(60 * $rad);  $my = $cy - $minLen * [math]::Cos(60 * $rad)
    $g.DrawLine($handPen, [single]$cx, [single]$cy, [single]$hx, [single]$hy)
    $g.DrawLine($handPen, [single]$cx, [single]$cy, [single]$mx, [single]$my)

    $dot = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 230, 238, 245))
    $g.FillEllipse($dot, [single]($cx - $s * 0.035), [single]($cy - $s * 0.035), [single]($s * 0.07), [single]($s * 0.07))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $ms.ToArray()

    $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
}

# 写 ICO 容器：6 字节头 + 每项 16 字节目录 + PNG 数据
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$blobs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write([byte[]]$b) }
$bw.Close()

Write-Output "icon written: $out"
