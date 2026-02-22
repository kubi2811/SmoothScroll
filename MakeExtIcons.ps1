Add-Type -AssemblyName System.Drawing

$src = "BrowserSmoothScroll\favicon\Gemini_Generated_Image_raf4s0raf4s0raf4.png"
$img = [System.Drawing.Bitmap]::FromFile($src)

New-Item -ItemType Directory -Force -Path "SmoothScrollExtension\icons" | Out-Null

foreach ($size in @(16, 48, 128)) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save("SmoothScrollExtension\icons\icon$size.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created icon$size.png"
}

$img.Dispose()
Write-Host "All icons created."
