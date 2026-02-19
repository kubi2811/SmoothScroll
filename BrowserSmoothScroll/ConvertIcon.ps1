Add-Type -AssemblyName System.Drawing

$src = Convert-Path "favicon\Gemini_Generated_Image_raf4s0raf4s0raf4.png"
$dst = "app.ico"

if (-not (Test-Path $src)) {
    Write-Error "Source image not found: $src"
    exit 1
}

# 1. Load Source
$img = [System.Drawing.Bitmap]::FromFile($src)

# 2. Resize to 256x256 (Highest Quality)
$w = 256
$h = 256
$resize = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($resize)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($img, 0, 0, $w, $h)
$g.Dispose()

# 3. Save as PNG to Memory (Vista+ ICO supports PNG)
$ms = New-Object System.IO.MemoryStream
$resize.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()

# 4. Write ICO File (Header + Directory + PNG Data)
$fs = [System.IO.File]::Create($dst)
$bw = New-Object System.IO.BinaryWriter($fs)

# Header (6 bytes)
$bw.Write([int16]0)   # Reserved
$bw.Write([int16]1)   # Type (1=ICON)
$bw.Write([int16]1)   # Count (1 image)

# Directory Entry (16 bytes)
$bw.Write([byte]0)    # Width (0=256)
$bw.Write([byte]0)    # Height (0=256)
$bw.Write([byte]0)    # ColorCount
$bw.Write([byte]0)    # Reserved
$bw.Write([int16]1)   # Planes
$bw.Write([int16]32)  # BitCount
$bw.Write([int32]$pngBytes.Length) # SizeInBytes
$bw.Write([int32]22)  # FileOffset (6 header + 16 entry)

# Image Data
$bw.Write($pngBytes)

$bw.Close()
$fs.Close()

$resize.Dispose()
$img.Dispose()
$ms.Dispose()

Write-Host "Created high-quality 256x256 ICO: $dst"
