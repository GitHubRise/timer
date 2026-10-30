param(
    [string]$OutFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\GameClock.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[object]

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $scale = $size / 256.0
        $cx = 128.0 * $scale
        $cy = 128.0 * $scale
        $outerRadius = 112.0 * $scale
        $faceRadius = 94.0 * $scale

        # Soft blue glow around the dial.
        for ($glow = 7; $glow -ge 1; $glow--) {
            $alpha = [int](8 + (8 - $glow) * 4)
            $glowRadius = $outerRadius + $glow * 3.0 * $scale
            $glowRect = New-Object System.Drawing.RectangleF(($cx - $glowRadius), ($cy - $glowRadius), (2 * $glowRadius), (2 * $glowRadius))
            $glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($alpha, 0, 174, 255), [single](2.0 * $scale))
            try {
                $g.DrawEllipse($glowPen, $glowRect)
            }
            finally {
                $glowPen.Dispose()
            }
        }

        # Dark navy face.
        $faceRect = New-Object System.Drawing.RectangleF(($cx - $outerRadius), ($cy - $outerRadius), (2 * $outerRadius), (2 * $outerRadius))
        $faceGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $faceRect,
            [System.Drawing.Color]::FromArgb(255, 7, 23, 43),
            [System.Drawing.Color]::FromArgb(255, 10, 50, 88),
            55.0)
        try {
            $g.FillEllipse($faceGradient, $faceRect)
        }
        finally {
            $faceGradient.Dispose()
        }

        # Outer luminous rim.
        $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 184, 255), [single]([Math]::Max(2.0, 8.0 * $scale)))
        try {
            $g.DrawEllipse($rimPen, $faceRect)
        }
        finally {
            $rimPen.Dispose()
        }

        $innerRimRect = New-Object System.Drawing.RectangleF(($cx - $faceRadius), ($cy - $faceRadius), (2 * $faceRadius), (2 * $faceRadius))
        $innerRimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 70, 214, 255), [single]([Math]::Max(1.0, 1.6 * $scale)))
        try {
            $g.DrawEllipse($innerRimPen, $innerRimRect)
        }
        finally {
            $innerRimPen.Dispose()
        }

        # Tech tick marks around the dial.
        if ($size -ge 24) {
            for ($tick = 0; $tick -lt 12; $tick++) {
                $angle = ($tick * 30.0 - 90.0) * [Math]::PI / 180.0
                $isMajor = ($tick % 3 -eq 0)
                $outer = 80.0 * $scale
                $inner = if ($isMajor) { 70.0 * $scale } else { 74.0 * $scale }
                $x1 = $cx + [Math]::Cos($angle) * $inner
                $y1 = $cy + [Math]::Sin($angle) * $inner
                $x2 = $cx + [Math]::Cos($angle) * $outer
                $y2 = $cy + [Math]::Sin($angle) * $outer
                $tickColor = if ($isMajor) {
                    [System.Drawing.Color]::FromArgb(235, 105, 232, 255)
                } else {
                    [System.Drawing.Color]::FromArgb(120, 66, 196, 255)
                }
                $tickWidth = if ($isMajor) { 2.4 } else { 1.4 }
                $tickPen = New-Object System.Drawing.Pen($tickColor, [single]([Math]::Max(1.0, $tickWidth * $scale)))
                try {
                    $tickPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                    $tickPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                    $g.DrawLine($tickPen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
                }
                finally {
                    $tickPen.Dispose()
                }
            }
        }

        # Quartz-style two hands, 12 o'clock and 3 o'clock.
        $handGlow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(75, 73, 231, 255), [single]([Math]::Max(4.0, 17.0 * $scale)))
        try {
            $handGlow.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $handGlow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $g.DrawLine($handGlow, [single]$cx, [single]$cy, [single]$cx, [single]($cy - 60.0 * $scale))
            $g.DrawLine($handGlow, [single]$cx, [single]$cy, [single]($cx + 48.0 * $scale), [single]$cy)
        }
        finally {
            $handGlow.Dispose()
        }

        $handPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 230, 251, 255), [single]([Math]::Max(1.8, 9.0 * $scale)))
        try {
            $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $g.DrawLine($handPen, [single]$cx, [single]$cy, [single]$cx, [single]($cy - 58.0 * $scale))
            $g.DrawLine($handPen, [single]$cx, [single]$cy, [single]($cx + 46.0 * $scale), [single]$cy)
        }
        finally {
            $handPen.Dispose()
        }

        # Center hub.
        $hubSize = [Math]::Max(3.0, 17.0 * $scale)
        $hubGlowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(95, 0, 196, 255))
        try {
            $g.FillEllipse(
                $hubGlowBrush,
                [single]($cx - $hubSize),
                [single]($cy - $hubSize),
                [single]($hubSize * 2),
                [single]($hubSize * 2))
        }
        finally {
            $hubGlowBrush.Dispose()
        }

        $hubBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        try {
            $g.FillEllipse(
                $hubBrush,
                [single]($cx - $hubSize / 2),
                [single]($cy - $hubSize / 2),
                [single]$hubSize,
                [single]$hubSize)
        }
        finally {
            $hubBrush.Dispose()
        }

        # A small sci-fi highlight arc on the upper-left rim.
        if ($size -ge 24) {
            $arcRect = New-Object System.Drawing.RectangleF(
                [single]($cx - 86.0 * $scale),
                [single]($cy - 86.0 * $scale),
                [single](172.0 * $scale),
                [single](172.0 * $scale))
            $arcPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 112, 238, 255), [single]([Math]::Max(1.0, 2.2 * $scale)))
            try {
                $arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $g.DrawArc($arcPen, $arcRect, 196.0, 62.0)
            }
            finally {
                $arcPen.Dispose()
            }
        }

        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add([pscustomobject]@{ Size = $size; Data = $ms.ToArray() })
        }
        finally {
            $ms.Dispose()
        }
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$images.Count)

    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$image.Data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $image.Data.Length
    }

    foreach ($image in $images) {
        $bw.Write([Byte[]]$image.Data)
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host ("Icon written: {0} ({1} bytes, {2} sizes)" -f $OutFile, (Get-Item -LiteralPath $OutFile).Length, $images.Count)
