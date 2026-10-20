#requires -Version 7.0
[CmdletBinding()]
param([string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $root ('build_test/platforms-' + [guid]::NewGuid().ToString('N')) }
$results = (New-Item -ItemType Directory -Path $ResultsDirectory -Force).FullName
$project = Join-Path $root 'PresetMaestro/PresetMaestro.csproj'
$sourceVersion = Join-Path $root 'PresetMaestro/Version.txt'
$original = Get-Content -LiteralPath $sourceVersion -Raw
$version = $original.Trim()
$isolatedVersion = Join-Path $results 'Version.txt'
Set-Content -LiteralPath $isolatedVersion -Value $version -NoNewline

function Invoke-DotnetCheck {
    param([string[]]$Arguments, [string]$ExpectedError)
    $output = & dotnet @Arguments 2>&1
    $code = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    if ($ExpectedError) {
        if ($code -eq 0 -or -not $text.Contains($ExpectedError, [StringComparison]::Ordinal)) {
            throw "Expected '$ExpectedError' and a failed command. Exit code $code.`n$text"
        }
    }
    elseif ($code -ne 0) { throw "dotnet failed ($code).`n$text" }
}

function Assert-BundleRejected {
    param([scriptblock]$Check, [string]$Message)
    try { & $Check }
    catch { if ($_.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) { return }; throw }
    throw "Bundle validation did not reject: $Message"
}

try {
    $common = @('publish', $project, '--configuration', 'Release', "-p:AppVersionFile=$isolatedVersion", "-p:PublishVersion=$version")
    Invoke-DotnetCheck ($common + @('--runtime', 'win-x64', '--output', (Join-Path $results 'windows')))
    Write-Host 'PASS: Windows production publish and platform payload guard.'
    & (Join-Path $root 'Publish-Mac.ps1') -Version $version -OutputDirectory (Join-Path $results 'macOS')

    Invoke-DotnetCheck @('build', $project, '--runtime', 'linux-x64') 'Windows and macOS only'
    Invoke-DotnetCheck @('build', $project, '--runtime', 'win-x64', '-p:MaestroPlatform=macOS') 'same operating system'
    Write-Host 'PASS: Linux and mismatched platform/runtime settings are rejected.'

    # Inject content through a normal SDK extension point. Both guards must reject it before bundling/version saving.
    $injection = Join-Path $results 'WrongPlatform.targets'
    Set-Content -LiteralPath (Join-Path $results 'wrong-native') -Value 'test fixture'
    $targets = @'
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'PresetMaestro'">
    <Content Include="$(MSBuildThisFileDirectory)wrong-native" CopyToPublishDirectory="Always">
      <Link Condition="'$(MaestroPlatform)' == 'Windows'">libAvaloniaNative.dylib</Link>
      <Link Condition="'$(MaestroPlatform)' == 'macOS'">Avalonia.Win32.dll</Link>
    </Content>
  </ItemGroup>
</Project>
'@
    Set-Content -LiteralPath $injection -Value $targets
    foreach ($rid in @('win-x64', 'osx-arm64')) {
        Invoke-DotnetCheck ($common + @('--runtime', $rid, '--output', (Join-Path $results "rejected-$rid"),
            '-p:PublishReadyToRun=false', "-p:CustomAfterMicrosoftCommonTargets=$injection")) 'forbidden platform payload'
    }
    Write-Host 'PASS: real publishes reject injected binaries from the other OS.'

    $bundle = Join-Path $results "macOS/osx-arm64/$version/Preset Maestro.app"
    $archive = Join-Path $results "macOS/osx-arm64/$version/PresetMaestro-$version-osx-arm64.zip"
    $expectedChecksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
    if ((Get-Content -LiteralPath ($archive + '.sha256') -Raw).Trim() -ne $expectedChecksum) { throw 'Mac archive checksum does not match the published ZIP.' }
    $validator = Join-Path $PSScriptRoot 'Test-MacBundle.ps1'
    if ($IsMacOS) {
        if (@(Get-ChildItem -LiteralPath (Join-Path $results 'macOS') -Directory -Force | Where-Object Name -Like '.build-*').Count) {
            throw 'Packaging left a hidden staging directory.'
        }
        # A managed resource stays valid JSON but must fail its sealed resource hash.
        $metadata = Join-Path $bundle 'Contents/Resources/Managed/PresetMaestro.runtimeconfig.json'
        $originalMetadata = [IO.File]::ReadAllBytes($metadata)
        $tamperedArchive = Join-Path $results 'tampered-resource.zip'
        try {
            [IO.File]::AppendAllText($metadata, "`n")
            Assert-BundleRejected { & $validator -BundlePath $bundle -ExpectedVersion $version -VerifySignature } 'Code signature verification failed'
            & /usr/bin/ditto -c -k --sequesterRsrc --keepParent $bundle $tamperedArchive
            if ($LASTEXITCODE -ne 0) { throw 'Cannot create the modified-resource ZIP fixture.' }
        }
        finally { [IO.File]::WriteAllBytes($metadata, $originalMetadata) }
        Assert-BundleRejected { & $validator -BundlePath $bundle -ArchivePath $tamperedArchive -ExpectedVersion $version -VerifySignature } 'Code signature verification failed'
        Write-Host 'PASS: signatures reject modified managed resources and modified ZIP contents; hidden staging is cleaned.'
    }
    Assert-BundleRejected { & $validator -BundlePath $bundle -ExpectedVersion $version -Architecture x64 } 'Wrong CPU architecture'
    $wrongLibrary = Join-Path $bundle 'Contents/MacOS/Melanchall_DryWetMidi_Native64.dll'
    try {
        Set-Content -LiteralPath $wrongLibrary -Value 'test fixture'
        Assert-BundleRejected { & $validator -BundlePath $bundle -ExpectedVersion $version } 'Wrong-platform file'
    }
    finally { if (Test-Path -LiteralPath $wrongLibrary) { Remove-Item -LiteralPath $wrongLibrary } }
    $badArchive = Join-Path $results 'missing-execute-bit.zip'
    Copy-Item -LiteralPath $archive -Destination $badArchive -Force
    $zip = [IO.Compression.ZipFile]::Open($badArchive, [IO.Compression.ZipArchiveMode]::Update)
    try { $zip.GetEntry('Preset Maestro.app/Contents/MacOS/PresetMaestro').ExternalAttributes = 33188 -shl 16 }
    finally { $zip.Dispose() }
    Assert-BundleRejected { & $validator -BundlePath $bundle -ArchivePath $badArchive -ExpectedVersion $version } 'executable permission'
    Write-Host 'PASS: bundle checks detect wrong native architectures, Windows libraries and lost executable permissions.'

    # App-only publishing must replace a prior archived release without leaving its stale ZIP.
    & (Join-Path $root 'Publish-Mac.ps1') -Version $version -OutputDirectory (Join-Path $results 'macOS') -SkipArchive
    if (Test-Path -LiteralPath $archive) { throw 'App-only publishing left the prior release ZIP.' }
    if (Test-Path -LiteralPath ($archive + '.sha256')) { throw 'App-only publishing left the prior release checksum.' }
    if (-not (Test-Path -LiteralPath $bundle)) { throw 'App-only publishing did not produce the runnable app.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $results 'macOS') -Directory -Force | Where-Object Name -Like '.build-*').Count) {
        throw 'App-only publishing left a hidden staging directory.'
    }
    Write-Host 'PASS: app-only publishing retains the runnable app, removes the prior release ZIP and cleans staging.'
}
finally {
    if ((Get-Content -LiteralPath $sourceVersion -Raw) -ne $original) { throw 'Platform checks changed the source version file.' }
}
