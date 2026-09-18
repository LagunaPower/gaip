# Windows-only developer utility; published apps use the generated assets directly.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/GAIP.Desktop/Assets'
$source = [System.Drawing.Bitmap]::new((Join-Path $assetRoot 'gaip-logo.png'))
$frames = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256, 512, 1024)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $memory = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $ratio = [Math]::Min($size / $source.Width, $size / $source.Height)
            $width = [int][Math]::Round($source.Width * $ratio)
            $height = [int][Math]::Round($source.Height * $ratio)
            $graphics.DrawImage($source, [int](($size-$width)/2), [int](($size-$height)/2), $width, $height)
            $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $memory.ToArray()
            $directory = Join-Path $assetRoot "icons/${size}x${size}"
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
            [System.IO.File]::WriteAllBytes((Join-Path $directory 'GAIP.png'), $bytes)
            if ($size -le 256) { $frames.Add([pscustomobject]@{ Size = $size; Bytes = $bytes }) }
        } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $stream = [System.IO.File]::Create((Join-Path $assetRoot 'GAIP.ico'))
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
} finally { $source.Dispose() }
