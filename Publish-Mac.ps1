#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('arm64', 'x64')][string]$Architecture = 'arm64',
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'publish-macos'),
    [string]$SigningIdentity = '-',
    [string]$NotaryProfile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$onMac = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)
if (-not $onMac -and ($SigningIdentity -ne '-' -or $NotaryProfile)) { throw 'Signing and notarization require macOS.' }
if ($NotaryProfile -and $SigningIdentity -eq '-') { throw 'Notarization requires a Developer ID Application signing identity.' }
if ($SigningIdentity -ne '-' -and -not $SigningIdentity.StartsWith('Developer ID Application:')) {
    throw 'Use a Developer ID Application identity, or omit -SigningIdentity for a local test build.'
}
$sourceVersion = Join-Path $PSScriptRoot 'PresetMaestro/Version.txt'
if (-not $PSBoundParameters.ContainsKey('Version')) { $Version = (Get-Content -LiteralPath $sourceVersion -Raw).Trim() }
if ($Version -notmatch '^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
    @($Version.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count -gt 0) {
    throw 'Use a numeric major.minor.patch version with components no greater than 65534.'
}
$rid = "osx-$Architecture"
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$releaseRoot = Join-Path $outputRoot "$rid/$Version"
$stage = Join-Path $outputRoot ('.build-' + [guid]::NewGuid().ToString('N'))
$appName = 'Preset Maestro.app'
$stagedApp = Join-Path $stage $appName
$binaryDirectory = Join-Path $stagedApp 'Contents/MacOS'
$resources = Join-Path $stagedApp 'Contents/Resources'
$bundle = Join-Path $releaseRoot $appName
$archive = Join-Path $releaseRoot "PresetMaestro-$Version-$rid.zip"

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

function Remove-GeneratedDirectory {
    param([string]$Path, [string]$Within)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolvedRoot = (Resolve-Path -LiteralPath $Within).Path.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedTarget = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::Ordinal) -or
        ((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove a path outside the generated output directory: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

function Test-MachO {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = [byte[]]::new(4)
        if ($stream.Read($header, 0, 4) -ne 4) { return $false }
        return [Convert]::ToHexString($header) -in @('CFFAEDFE', 'FEEDFACF', 'CAFEBABE', 'BEBAFECA', 'CAFEBABF')
    }
    finally { $stream.Dispose() }
}

function Write-AppArchive {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    if ($onMac) {
        Invoke-Checked '/usr/bin/ditto' @('-c', '-k', '--sequesterRsrc', '--keepParent', $bundle, $archive)
        return
    }
    # ZIP Unix attributes preserve the apphost's execute bit when a Windows-built archive is unpacked on a Mac.
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $bundle -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($releaseRoot, $file.FullName).Replace('\', '/')
            $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
            $mode = if (Test-MachO $file.FullName) { 33261 } else { 33188 } # regular file, 0755 / 0644
            $entry.ExternalAttributes = $mode -shl 16
        }
    }
    finally { $zip.Dispose() }
}

New-Item -ItemType Directory -Path $binaryDirectory, $resources, $releaseRoot -Force | Out-Null
try {
    # Reuse the release version on both OSes. A temporary version file avoids incrementing or modifying the source.
    $temporaryVersion = Join-Path $stage 'Version.txt'
    Set-Content -LiteralPath $temporaryVersion -Value $Version -NoNewline
    Invoke-Checked 'dotnet' @('publish', (Join-Path $PSScriptRoot 'PresetMaestro/PresetMaestro.csproj'),
        '--configuration', 'Release', '--runtime', $rid, '--self-contained', 'true', '--output', $binaryDirectory,
        "-p:PublishVersion=$Version", "-p:AppVersionFile=$temporaryVersion", '-p:DebugType=None', '-p:DebugSymbols=false')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/macos/PresetMaestro.icns') -Destination $resources
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/macos/DryWetMidi.LICENSE.txt') -Destination $resources
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.presetmaestro.app</string>
  <key>CFBundleName</key><string>Preset Maestro</string>
  <key>CFBundleDisplayName</key><string>Preset Maestro</string>
  <key>CFBundleExecutable</key><string>PresetMaestro</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$Version</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>CFBundleIconFile</key><string>PresetMaestro.icns</string>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>CFBundleSupportedPlatforms</key><array><string>MacOSX</string></array>
</dict></plist>
"@
    $plistPath = Join-Path $stagedApp 'Contents/Info.plist'
    [IO.File]::WriteAllText($plistPath, $plist)
    & (Join-Path $PSScriptRoot 'scripts/Test-MacBundle.ps1') -BundlePath $stagedApp -Architecture $Architecture -ExpectedVersion $Version
    if ($onMac) {
        Invoke-Checked '/usr/bin/plutil' @('-lint', $plistPath)
        $entitlements = Join-Path $PSScriptRoot 'packaging/macos/Entitlements.plist'
        $signArgs = @('--force', '--sign', $SigningIdentity)
        if ($SigningIdentity -ne '-') { $signArgs += @('--timestamp', '--options', 'runtime') }
        foreach ($file in Get-ChildItem -LiteralPath $binaryDirectory -File -Recurse) {
            if (-not (Test-MachO $file.FullName)) { continue }
            Invoke-Checked '/bin/chmod' @('755', $file.FullName)
            $architectures = & /usr/bin/lipo -archs $file.FullName
            if ($LASTEXITCODE -ne 0) { throw "Cannot inspect architecture: $($file.FullName)" }
            if ($architectures.Trim().Split(' ').Count -gt 1) {
                Invoke-Checked '/usr/bin/lipo' @($file.FullName, '-thin', $(if ($Architecture -eq 'x64') { 'x86_64' } else { 'arm64' }), '-output', $file.FullName)
            }
            $fileArgs = $signArgs
            if ($file.Name -eq 'PresetMaestro') { $fileArgs += @('--entitlements', $entitlements) }
            Invoke-Checked '/usr/bin/codesign' ($fileArgs + @($file.FullName))
        }
        Invoke-Checked '/usr/bin/codesign' ($signArgs + @('--entitlements', $entitlements, $stagedApp))
        Invoke-Checked '/usr/bin/codesign' @('--verify', '--deep', '--strict', '--verbose=2', $stagedApp)
    }
    $marker = Join-Path $releaseRoot '.presetmaestro-release'
    if ((Test-Path -LiteralPath $bundle) -and -not (Test-Path -LiteralPath $marker)) {
        throw "The destination already contains an unmarked app bundle: $bundle"
    }
    Remove-GeneratedDirectory $bundle $releaseRoot
    Move-Item -LiteralPath $stagedApp -Destination $bundle
    Set-Content -LiteralPath $marker -Value 'Preset Maestro generated release'
    Write-AppArchive
    if ($NotaryProfile) {
        Invoke-Checked '/usr/bin/xcrun' @('notarytool', 'submit', $archive, '--keychain-profile', $NotaryProfile, '--wait')
        Invoke-Checked '/usr/bin/xcrun' @('stapler', 'staple', $bundle)
        Invoke-Checked '/usr/bin/xcrun' @('stapler', 'validate', $bundle)
        Invoke-Checked '/usr/sbin/spctl' @('--assess', '--type', 'execute', '--verbose=2', $bundle)
        Write-AppArchive
    }
    & (Join-Path $PSScriptRoot 'scripts/Test-MacBundle.ps1') -BundlePath $bundle -ArchivePath $archive -Architecture $Architecture -ExpectedVersion $Version
    Write-Host "Mac app: $bundle"
    Write-Host "Archive: $archive"
    if (-not $onMac) { Write-Host 'Cross-built preparation artifact: native launch, MIDI and code signing still require macOS validation.' }
    elseif (-not $NotaryProfile) { Write-Host 'Local test build. Public distribution still requires Developer ID signing and notarization.' }
}
finally { Remove-GeneratedDirectory $stage $outputRoot }
