# Draws the Zelda-style life hearts used by the HUD (mod/Lanppart/Assets/heart_full.png, heart_empty.png).
param([string]$OutDir = "$PSScriptRoot\..\mod\Lanppart\Assets", [int]$Scale = 6)
Add-Type -AssemblyName System.Drawing
$art = @(
  "..XXX...XXX..",
  ".XRRRX.XRRRX.",
  "XRWWRRXRRRRRX",
  "XRWRRRRRRRRRX",
  "XRRRRRRRRRRRX",
  "XRRRRRRRRRRSX",
  ".XRRRRRRRRSX.",
  "..XRRRRRRSX..",
  "...XRRRRSX...",
  "....XRRSX....",
  ".....XSX.....",
  "......X......")
function Draw($name, $colors) {
  $w = $art[0].Length; $h = $art.Count
  $b = New-Object System.Drawing.Bitmap ($w * $Scale), ($h * $Scale), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.Clear([System.Drawing.Color]::Transparent)
  for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) {
    $c = $art[$y][$x]
    if ($colors.ContainsKey([string]$c)) {
      $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($colors[[string]$c]))
      $g.FillRectangle($brush, $x * $Scale, $y * $Scale, $Scale, $Scale)
    }
  } }
  $b.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
}
Draw "heart_full.png"  @{ X = "#1C0606"; R = "#D8262A"; W = "#FFE0D8"; S = "#9C1418" }
Draw "heart_empty.png" @{ X = "#B9A898"; R = "#2E2422"; W = "#3E3230"; S = "#2A201E" }  # light outline: readable on the dark room
