#requires -Version 7.0
[CmdletBinding()]
param([string]$ResultsDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) {
    throw 'Run this readiness check on the M2 Mac. Windows can prepare a bundle with Publish-Mac.ps1.'
}
if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64) {
    throw 'Use the Apple Silicon/Arm64 PowerShell installer and a native Terminal session on the M2.'
}
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $root ('build_test/mac-readiness-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$results = (New-Item -ItemType Directory -Path $ResultsDirectory -Force).FullName

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE. See the readiness log in $results." }
}

foreach ($tool in @('dotnet', '/usr/bin/sw_vers', '/usr/bin/xcode-select', '/usr/bin/codesign', '/usr/bin/plutil', '/usr/bin/lipo', '/usr/bin/ditto')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool. Install the Arm64 .NET 10 SDK and Apple's Command Line Tools (xcode-select --install)." }
}
$macVersion = & /usr/bin/sw_vers -productVersion
if ($LASTEXITCODE -ne 0 -or [version]$macVersion -lt [version]'15.0') { throw 'Preset Maestro requires macOS 15 or newer.' }
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or [version]($sdkVersion -split '-')[0] -lt [version]'10.0') { throw 'Install the .NET 10 Arm64 SDK.' }
$sdkInfo = & dotnet --info
if ($LASTEXITCODE -ne 0 -or ($sdkInfo -join "`n") -notmatch 'osx-arm64') { throw 'The selected .NET SDK must run natively on Apple Silicon. Install/select the Arm64 SDK.' }

$sourceVersion = Join-Path $root 'PresetMaestro/Version.txt'
$originalVersion = Get-Content -LiteralPath $sourceVersion -Raw
$version = $originalVersion.Trim()
$bundle = Join-Path $root "publish-macos/osx-arm64/$version/Preset Maestro.app"
$archive = Join-Path $root "publish-macos/osx-arm64/$version/PresetMaestro-$version-osx-arm64.zip"
$transcriptStarted = $false
Push-Location $root
try {
    Start-Transcript -LiteralPath (Join-Path $results 'readiness.log') | Out-Null
    $transcriptStarted = $true
    Write-Host "Preset Maestro $version — macOS $macVersion, .NET SDK $sdkVersion, PowerShell $($PSVersionTable.PSVersion)"
    $sdkInfo | Out-Host
    Invoke-Checked '/usr/bin/xcode-select' @('-p')
    Invoke-Checked 'dotnet' @('build', 'PresetMaestro.slnx', '--configuration', 'Release')
    # Exclude physical tests even if a previous diagnostic session left opt-in environment variables set.
    # The native CoreMIDI smoke test only loads the library and enumerates available endpoints.
    Invoke-Checked 'dotnet' @('test', 'PresetMaestro.slnx', '--configuration', 'Release', '--no-build',
        '--filter', 'Category!=Hardware&Category!=Compatibility', '--collect', 'XPlat Code Coverage',
        '--logger', 'trx', '--results-directory', $results)
    Invoke-Checked 'dotnet' @('format', 'PresetMaestro.slnx', '--verify-no-changes', '--no-restore')
    & (Join-Path $root 'Publish-Mac.ps1') -Architecture arm64
    if ((Get-Content -LiteralPath $sourceVersion -Raw) -ne $originalVersion) { throw 'The readiness check changed the source version.' }
    $report = [ordered]@{
        Version = $version; MacOS = $macVersion; Sdk = $sdkVersion; Architecture = 'arm64'
        Bundle = $bundle; Archive = $archive; ArchiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        BuildTestsFormattingAndLocalSigning = 'Passed'
        NativeUiAndPhysicalMidi = 'Pending manual acceptance'
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'readiness.json')
    Write-Host "PASS: native build, regression tests, CoreMIDI loading, formatting, packaging and local signature checks."
    Write-Host "Next, launch from Finder or Terminal: open `"$bundle`""
    Write-Host "Then follow docs/mac-first-day.md for visual and hardware acceptance. Results: $results"
}
finally {
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    Pop-Location
}
