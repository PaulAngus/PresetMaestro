#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [ValidateSet('arm64', 'x64')][string]$Architecture = 'arm64',
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$binaryDirectory = Join-Path $bundle 'Contents/MacOS'
$expectedCpu = if ($Architecture -eq 'arm64') { 0x0100000c } else { 0x01000007 }
$required = @('PresetMaestro', 'PresetMaestro.dll', 'PresetMaestro.deps.json', 'PresetMaestro.runtimeconfig.json',
    'libhostfxr.dylib', 'libcoreclr.dylib', 'libAvaloniaNative.dylib', 'libSkiaSharp.dylib',
    'Melanchall.DryWetMidi.dll', 'Melanchall_DryWetMidi_Native64.dylib')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $binaryDirectory $name) -PathType Leaf)) { throw "Mac bundle is missing $name" }
}
[xml]$plist = Get-Content -LiteralPath (Join-Path $bundle 'Contents/Info.plist') -Raw
$nodes = @($plist.DocumentElement.SelectSingleNode('dict').ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element })
$values = @{}
for ($i = 0; $i -lt $nodes.Count; $i += 2) {
    if ($nodes[$i].Name -ne 'key' -or $values.ContainsKey($nodes[$i].InnerText)) { throw 'Malformed Info.plist dictionary.' }
    $values[$nodes[$i].InnerText] = $nodes[$i + 1].InnerText
}
if ($values.CFBundleExecutable -ne 'PresetMaestro' -or $values.CFBundlePackageType -ne 'APPL' -or
    $values.CFBundleVersion -ne $ExpectedVersion -or $values.CFBundleShortVersionString -ne $ExpectedVersion) {
    throw 'Bundle identity/version does not agree with the requested release.'
}
$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $binaryDirectory 'PresetMaestro.dll')).ProductVersion
if ($assemblyVersion -ne $ExpectedVersion) { throw "Assembly version $assemblyVersion does not match $ExpectedVersion." }
$icon = [IO.File]::ReadAllBytes((Join-Path $bundle 'Contents/Resources/PresetMaestro.icns'))
if ($icon.Length -lt 8 -or [Text.Encoding]::ASCII.GetString($icon, 0, 4) -ne 'icns') { throw 'Missing or invalid Mac icon.' }
$deps = Get-Content -LiteralPath (Join-Path $binaryDirectory 'PresetMaestro.deps.json') -Raw | ConvertFrom-Json
$unexpectedPackages = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^(NAudio|Avalonia\.(Win32|X11|LinuxFramebuffer|Desktop)|Microsoft.Windows.SDK|WinRT.Runtime)/' })
if ($unexpectedPackages.Count) { throw "Unexpected platform dependencies: $unexpectedPackages" }
$forbidden = '(\.so(\.|$)|\.exe$|Avalonia\.(Win32|X11|LinuxFramebuffer|Desktop)\.dll$|^NAudio|^WinRT\.Runtime|^Microsoft\.Windows\.SDK|Melanchall_DryWetMidi_Native[0-9]+\.dll$|lib(SkiaSharp|HarfBuzzSharp)\.dll$)'
$machOFiles = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $binaryDirectory -File -Recurse) {
    if ($file.Name -match $forbidden) { throw "Wrong-platform file in Mac bundle: $($file.Name)" }
    $stream = [IO.File]::OpenRead($file.FullName)
    try {
        $header = [byte[]]::new(4096)
        $read = $stream.Read($header, 0, $header.Length)
        if ($read -lt 8) { continue }
        $magic = [Convert]::ToHexString($header[0..3])
        if ($magic -eq 'CFFAEDFE') {
            if ([BitConverter]::ToInt32($header, 4) -ne $expectedCpu) { throw "Wrong CPU architecture in $($file.Name)" }
            $machOFiles.Add($file.FullName)
        }
        elseif ($magic -eq 'CAFEBABE') {
            $countBytes = $header[4..7]; [Array]::Reverse($countBytes)
            $count = [BitConverter]::ToInt32($countBytes, 0)
            if ($count -lt 1 -or 8 + $count * 20 -gt $read) { throw 'Invalid universal Mach-O header.' }
            $found = $false
            for ($i = 0; $i -lt $count; $i++) {
                $start = 8 + 20 * $i
                $cpuBytes = $header[$start..($start + 3)]; [Array]::Reverse($cpuBytes)
                if ([BitConverter]::ToInt32($cpuBytes, 0) -eq $expectedCpu) { $found = $true }
            }
            if (-not $found) { throw "Universal library lacks $Architecture : $($file.Name)" }
            $machOFiles.Add($file.FullName)
        }
        elseif ($file.Extension -eq '.dylib' -or $file.Name -eq 'PresetMaestro') { throw "Invalid Mac binary: $($file.Name)" }
    }
    finally { $stream.Dispose() }
}
if ($ArchivePath) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
    try {
        $parent = Split-Path $bundle -Parent
        foreach ($file in $machOFiles) {
            $name = [IO.Path]::GetRelativePath($parent, $file).Replace('\', '/')
            $entry = $zip.GetEntry($name)
            if ($null -eq $entry -or (($entry.ExternalAttributes -shr 16) -band 64) -eq 0) {
                throw "Archive is missing the executable permission for $name"
            }
        }
    }
    finally { $zip.Dispose() }
}
Write-Host "PASS: $Architecture self-contained Mac bundle, version $ExpectedVersion, native architectures and platform dependencies verified."
