param([string]$In, [string]$Out, [int]$MaxWidth = 1280, [int]$Quality = 85)
# Saves a screenshot as a JPEG at most MaxWidth pixels wide (README images).
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($In)
$scale = [Math]::Min(1.0, $MaxWidth / $src.Width)
$w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
$dst = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src, 0, 0, $w, $h)
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
$params = New-Object System.Drawing.Imaging.EncoderParameters 1
$params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), ([long]$Quality)
$dst.Save($Out, $codec, $params); $src.Dispose()
