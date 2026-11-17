param(
    [Parameter(Mandatory = $true)][string]$SourceImage,
    [Parameter(Mandatory = $true)][string]$AssetsDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedIconImage([System.Drawing.Image]$source, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $radius = [single]($size * 0.145)
        $diameter = [single]($radius * 2)
        $edge = [single]$size
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
            $path.AddArc($edge - $diameter, 0, $diameter, $diameter, 270, 90)
            $path.AddArc($edge - $diameter, $edge - $diameter, $diameter, $diameter, 0, 90)
            $path.AddArc(0, $edge - $diameter, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            $graphics.SetClip($path)
            # Crop the JPEG's thin white scan edge before scaling so the transparent
            # rounded boundary has a clean charcoal antialias instead of a white halo.
            $sourceInset = [int][Math]::Max(4, [Math]::Round([Math]::Min($source.Width, $source.Height) * 0.008))
            $sourceRect = [System.Drawing.Rectangle]::new(
                $sourceInset,
                $sourceInset,
                $source.Width - ($sourceInset * 2),
                $source.Height - ($sourceInset * 2))
            $graphics.DrawImage(
                $source,
                [System.Drawing.Rectangle]::new(0, 0, $size, $size),
                $sourceRect,
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $path.Dispose() }
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

function Convert-ToPngBytes([System.Drawing.Image]$image) {
    $stream = [System.IO.MemoryStream]::new()
    $image.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return $bytes
}

$sourcePath = [System.IO.Path]::GetFullPath($SourceImage)
$assetsPath = [System.IO.Path]::GetFullPath($AssetsDirectory)
if (-not [System.IO.File]::Exists($sourcePath)) { throw "Source image not found: $sourcePath" }
[System.IO.Directory]::CreateDirectory($assetsPath) | Out-Null

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $master = New-RoundedIconImage $source 1024
    try {
        $master.Save([System.IO.Path]::Combine($assetsPath, 'LumenMarkdown.png'), [System.Drawing.Imaging.ImageFormat]::Png)

        $preview = New-RoundedIconImage $source 128
        try { $preview.Save([System.IO.Path]::Combine($assetsPath, 'LumenMarkdown-128.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $preview.Dispose() }

        $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
        $frames = foreach ($size in $sizes) {
            $frame = New-RoundedIconImage $source $size
            try { [PSCustomObject]@{ Size = $size; Bytes = Convert-ToPngBytes $frame } }
            finally { $frame.Dispose() }
        }

        $iconPath = [System.IO.Path]::Combine($assetsPath, 'LumenMarkdown.ico')
        $file = [System.IO.File]::Create($iconPath)
        $writer = [System.IO.BinaryWriter]::new($file)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$frames.Count)
            $offset = 6 + (16 * $frames.Count)
            foreach ($frame in $frames) {
                $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
                $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frame.Bytes.Length)
                $writer.Write([uint32]$offset)
                $offset += $frame.Bytes.Length
            }
            foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
        }
        finally { $writer.Dispose(); $file.Dispose() }
    }
    finally { $master.Dispose() }
}
finally { $source.Dispose() }

Write-Host "Generated app icon assets in $assetsPath"
