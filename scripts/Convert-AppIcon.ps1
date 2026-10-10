# Converts the existing PNG-backed ICO sizes to an ICNS container without changing any pixels.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllBytes((Join-Path $root 'PresetMaestro/preset_maestro.ico'))
if ([BitConverter]::ToUInt16($source, 2) -ne 1) { throw 'Expected an ICO source.' }
$types = @{ 16 = 'icp4'; 32 = 'icp5'; 64 = 'icp6'; 128 = 'ic07'; 256 = 'ic08' }
$chunks = [Collections.Generic.List[byte[]]]::new()
for ($i = 0; $i -lt [BitConverter]::ToUInt16($source, 4); $i++) {
    $entry = 6 + 16 * $i
    $size = if ($source[$entry] -eq 0) { 256 } else { [int]$source[$entry] }
    if (-not $types.ContainsKey($size)) { continue }
    $length = [BitConverter]::ToInt32($source, $entry + 8)
    $offset = [BitConverter]::ToInt32($source, $entry + 12)
    if ([Convert]::ToHexString($source[$offset..($offset + 7)]) -ne '89504E470D0A1A0A') { throw 'Expected PNG-backed icon entries.' }
    $chunk = [byte[]]::new($length + 8)
    [Text.Encoding]::ASCII.GetBytes($types[$size]).CopyTo($chunk, 0)
    $lengthBytes = [BitConverter]::GetBytes([int]$chunk.Length)
    [Array]::Reverse($lengthBytes)
    $lengthBytes.CopyTo($chunk, 4)
    [Array]::Copy($source, $offset, $chunk, 8, $length)
    $chunks.Add($chunk)
}
if ($chunks.Count -ne 5) { throw 'Expected all five supported icon sizes.' }
$total = 8
foreach ($chunk in $chunks) { $total += $chunk.Length }
$result = [byte[]]::new($total)
[Text.Encoding]::ASCII.GetBytes('icns').CopyTo($result, 0)
$lengthBytes = [BitConverter]::GetBytes([int]$total)
[Array]::Reverse($lengthBytes)
$lengthBytes.CopyTo($result, 4)
$offset = 8
foreach ($chunk in $chunks) { $chunk.CopyTo($result, $offset); $offset += $chunk.Length }
$target = Join-Path $root 'packaging/macos/PresetMaestro.icns'
[IO.File]::WriteAllBytes($target, $result)
Write-Host "Created $target from the existing Windows icon ($total bytes)."
