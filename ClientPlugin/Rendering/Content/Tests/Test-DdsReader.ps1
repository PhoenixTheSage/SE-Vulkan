$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\DdsImage.cs'))

function Assert-True([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function New-Header([uint32]$width, [uint32]$height, [uint32]$fourCC, [uint32]$mips = 1, [uint32]$caps2 = 0) {
    [uint32[]]$header = @(0x20534444, 124, 0x1007, $height, $width, ($width * 4), 0, $mips)
    $header += [uint32[]]::new(11)
    $header += @(32, $(if ($fourCC) { 4 } else { 0x41 }), $fourCC, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000u)
    $header += @(0x1000, $caps2, 0, 0, 0)
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    foreach ($word in $header) { $writer.Write([uint32]$word) }
    return @{ Stream = $stream; Writer = $writer }
}

$flat = New-Header 2 2 0
[byte[]]$rgba = @(255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, 255, 255, 255, 0)
$flat.Writer.Write($rgba); $flat.Stream.Position = 0
$image = [ClientPlugin.Rendering.Content.DdsImage]::Read($flat.Stream)
Assert-True ($image.Width -eq 2 -and $image.Height -eq 2 -and $image.Format -eq 0) 'RGBA DDS dimensions/format failed.'
Assert-True ([Convert]::ToBase64String($image.Data) -eq [Convert]::ToBase64String($rgba)) 'RGBA DDS channel order/alpha failed.'
$flat.Stream.Dispose()

$bc7 = New-Header 4 4 0x30315844
foreach ($word in @(99, 3, 0, 1, 0)) { $bc7.Writer.Write([uint32]$word) }
$bc7.Writer.Write([byte[]]::new(16)); $bc7.Stream.Position = 0
$image = [ClientPlugin.Rendering.Content.DdsImage]::Read($bc7.Stream)
Assert-True ($image.Format -eq 8 -and $image.Srgb -and $image.Data.Length -eq 16) 'BC7 sRGB DDS decoding failed.'
$bc7.Stream.Dispose()

foreach($variant in @(@('DXT2',3),@('DXT4',4))) {
    $four=[BitConverter]::ToUInt32([Text.Encoding]::ASCII.GetBytes($variant[0]),0)
    $legacy=New-Header 8 8 $four 2
    [byte[]]$top=0..63;[byte[]]$tail=64..79
    $legacy.Writer.Write($top);$legacy.Writer.Write($tail);$legacy.Stream.Position=0
    $decoded=[ClientPlugin.Rendering.Content.DdsImage]::Read($legacy.Stream,$true)
    Assert-True ($decoded.Format -eq $variant[1] -and $decoded.IsPremultiplied -and !$decoded.IsRawRgba) ($variant[0]+' format/alpha mode failed.')
    Assert-True ($decoded.Mips.Length -eq 2 -and [Convert]::ToBase64String($decoded.Mips[0]) -eq [Convert]::ToBase64String($top) -and [Convert]::ToBase64String($decoded.Mips[1]) -eq [Convert]::ToBase64String($tail)) ($variant[0]+' compressed bytes/mip offsets changed.')
    $legacy.Stream.Dispose()
}

$premulRaw=New-Header 1 1 0x30315844
foreach($word in @(29,3,0,1,2)){$premulRaw.Writer.Write([uint32]$word)}
$premulRaw.Writer.Write([byte[]]@(64,32,16,128));$premulRaw.Stream.Position=0
$decoded=[ClientPlugin.Rendering.Content.DdsImage]::Read($premulRaw.Stream)
Assert-True ($decoded.IsPremultiplied -and $decoded.IsRawRgba -and $decoded.Srgb) 'DX10 premultiplied alpha mode was lost.'
$premulRaw.Stream.Dispose()

$cube = New-Header 4 4 0x30315844 2
foreach ($word in @(98, 3, 4, 1, 0)) { $cube.Writer.Write([uint32]$word) }
for ($face = 0; $face -lt 6; ++$face) {
    $cube.Writer.Write([byte[]](@($face) * 16))
    $cube.Writer.Write([byte[]](@(99) * 16))
}
$cube.Stream.Position = 0
$faces = [ClientPlugin.Rendering.Content.DdsImage]::ReadCube($cube.Stream)
Assert-True ($faces.Length -eq 6) 'Cube DDS face count failed.'
for ($face = 0; $face -lt 6; ++$face) {
    Assert-True ($faces[$face].Data[0] -eq $face -and $faces[$face].Data.Length -eq 16) 'Cube DDS face order/mip-chain skipping failed.'
}
$cube.Stream.Position = 0
$rejected = $false
try { [void][ClientPlugin.Rendering.Content.DdsImage]::Read($cube.Stream) } catch { $rejected = $true }
Assert-True $rejected 'Flat DDS reader accepted a cube without the cube path.'
$cube.Stream.Dispose()

$truncated = New-Header 4 4 0x30315844
foreach ($word in @(98, 3, 0, 1, 0)) { $truncated.Writer.Write([uint32]$word) }
$truncated.Writer.Write([byte[]]::new(15)); $truncated.Stream.Position = 0
$rejected = $false
try { [void][ClientPlugin.Rendering.Content.DdsImage]::Read($truncated.Stream) } catch { $rejected = $true }
Assert-True $rejected 'Truncated compressed DDS was accepted.'
$truncated.Stream.Dispose()
Write-Host 'Passed DDS RGBA channels, BC7 sRGB, cube faces/mip offsets, flat-cube rejection and truncation checks.'
