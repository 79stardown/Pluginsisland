# Builds a multi-size .ico for the .cipx file association from a source icon.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -Source assets\cipx-icon.ico -Destination Pluginsisland.Helper\App.ico
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

# --- Load the largest frame of the source .ico as a Bitmap -------------------
$bytes = [System.IO.File]::ReadAllBytes($Source)
$count = [BitConverter]::ToUInt16($bytes, 4)
$bestOffset = 0; $bestSize = 0; $bestBytes = 0
for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + $i * 16
    $w = $bytes[$o]; if ($w -eq 0) { $w = 256 }
    if ($w -gt $bestSize) {
        $bestSize = $w
        $bestBytes = [BitConverter]::ToUInt32($bytes, $o + 8)
        $bestOffset = [BitConverter]::ToUInt32($bytes, $o + 12)
    }
}

$frame = New-Object byte[] $bestBytes
[Array]::Copy($bytes, $bestOffset, $frame, 0, $bestBytes)

# PNG-in-ICO (Vista+ format) starts with the PNG signature; otherwise it is a BMP frame.
$isPng = ($frame[0] -eq 0x89 -and $frame[1] -eq 0x50 -and $frame[2] -eq 0x4E -and $frame[3] -eq 0x47)
if ($isPng) {
    $ms = New-Object System.IO.MemoryStream(, $frame)
    $src = [System.Drawing.Image]::FromStream($ms)
} else {
    $ico = New-Object System.Drawing.Icon($Source, $bestSize, $bestSize)
    $src = $ico.ToBitmap()
}

# --- Render each target size and assemble the .ico ---------------------------
$images = @()
foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $g.Dispose()
    $png = New-Object System.IO.MemoryStream
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @{ Size = $size; Data = $png.ToArray() }
    $png.Dispose(); $bmp.Dispose()
}

$out = [System.IO.File]::Create($Destination)
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim)
    $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$img.Data.Length)
    $w.Write([UInt32]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write($img.Data) }
$w.Close(); $out.Close()

Write-Host ("Wrote {0} ({1} bytes, sizes: {2})" -f $Destination, (Get-Item $Destination).Length, ($Sizes -join ','))
