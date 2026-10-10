param([string]$ResultsDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'PresetMaestro\PresetMaestro.csproj'
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $projectRoot ("build_test\publish-version-" + [guid]::NewGuid().ToString('N'))
}
$results = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$versionFile = Join-Path $results.FullName 'Version.txt'
$outputFolder = Join-Path $results.FullName 'published app'
$realVersionFile = Join-Path $projectRoot 'PresetMaestro\Version.txt'
$originalVersion = Get-Content -LiteralPath $realVersionFile -Raw
Set-Content -LiteralPath $versionFile -Value '1.2' -NoNewline

$commonArguments = @($project, '--configuration', 'Release', '--no-restore', '--verbosity', 'quiet',
    "-p:AppVersionFile=$versionFile", '-p:PublishReadyToRun=false', '-p:PublishSingleFile=false',
    '-p:EnableCompressionInSingleFile=false', '-p:SelfContained=false')

function Invoke-DotNetCheck {
    param([string[]]$Arguments, [string]$ExpectedError)
    $commandOutput = & dotnet @Arguments 2>&1
    $commandExitCode = $LASTEXITCODE
    $outputText = $commandOutput -join [Environment]::NewLine
    if ($ExpectedError) {
        if ($commandExitCode -eq 0 -or $outputText -notmatch [regex]::Escape($ExpectedError)) {
            throw "Expected failure containing '$ExpectedError', got exit code $commandExitCode.`n$outputText"
        }
    }
    elseif ($commandExitCode -ne 0) { throw "dotnet failed ($commandExitCode).`n$outputText" }
}

function Assert-SavedVersion {
    param([string]$Expected)
    $savedVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ($savedVersion -ne $Expected) { throw "Expected saved version $Expected, got $savedVersion." }
}

function Assert-PublishedVersion {
    param([string]$Expected)
    Assert-SavedVersion $Expected
    foreach ($artifact in @('PresetMaestro.dll', 'PresetMaestro.exe')) {
        $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $outputFolder $artifact))
        if ($metadata.ProductVersion -ne $Expected -or $metadata.FileVersion -ne "$Expected.0") {
            throw "$artifact metadata differs: product=$($metadata.ProductVersion), file=$($metadata.FileVersion), expected=$Expected."
        }
    }
}

try {
    Invoke-DotNetCheck (@('build') + $commonArguments)
    Assert-SavedVersion '1.2'
    Write-Host 'PASS: ordinary build does not increment the version.'

    foreach ($expected in @('1.2.1', '1.2.2')) {
        Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder))
        Assert-PublishedVersion $expected
    }
    Write-Host 'PASS: successive publishes increment the patch and stamp matching metadata.'

    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '-p:PublishVersion=2.0.7'))
    Assert-PublishedVersion '2.0.7'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder))
    Assert-PublishedVersion '2.0.8'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '-p:Version=2.1.0'))
    Assert-PublishedVersion '2.1.0'
    Write-Host 'PASS: explicit versions are used exactly and become the next increment baseline.'

    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder,
        '-p:PublishSingleFile=true', '-p:UseAppHost=false')) 'NETSDK1098'
    Assert-SavedVersion '2.1.0'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '--no-build')) 'Remove --no-build'
    Assert-SavedVersion '2.1.0'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '-p:PublishVersion=bad')) 'Invalid version'
    Assert-SavedVersion '2.1.0'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '-p:PublishVersion=2.2')) 'three components'
    Assert-SavedVersion '2.1.0'
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder, '-p:FileVersion=9.9.9.0')) 'Conflicting version overrides'
    Assert-SavedVersion '2.1.0'
    Write-Host 'PASS: failed publishing, no-build and invalid overrides do not change the saved version.'

    $msbuildArguments = @('msbuild', $project, '-target:Publish', '-verbosity:quiet', '-p:Configuration=Release',
        "-p:AppVersionFile=$versionFile", "-p:PublishDir=$outputFolder", '-p:PublishReadyToRun=false',
        '-p:PublishSingleFile=false', '-p:EnableCompressionInSingleFile=false', '-p:SelfContained=false')
    Invoke-DotNetCheck $msbuildArguments 'Publish through dotnet publish'
    Assert-SavedVersion '2.1.0'
    Set-Content -LiteralPath $versionFile -Value '1.2.65534' -NoNewline
    Invoke-DotNetCheck (@('publish') + $commonArguments + @('--output', $outputFolder)) 'at most 65534'
    Assert-SavedVersion '1.2.65534'
    Write-Host 'PASS: unprepared publishes and assembly version overflow are rejected.'

    # Isolate the wrapper to verify command forwarding and failure handling without copying a real release.
    $helperFolder = New-Item -ItemType Directory -Path (Join-Path $results.FullName 'helper') -Force
    $helper = Join-Path $helperFolder.FullName 'Publish-App.ps1'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Publish-App.ps1') -Destination $helper
    $copyMarker = Join-Path $helperFolder.FullName 'distributed.txt'
    if (Test-Path -LiteralPath $copyMarker) { Remove-Item -LiteralPath $copyMarker }
    Set-Content -LiteralPath (Join-Path $helperFolder.FullName 'Copy-PublishedExe.ps1') -Value 'Set-Content -LiteralPath (Join-Path $PSScriptRoot "distributed.txt") -Value "copied"'
    $dotnetStubState = [pscustomobject]@{ ExitCode = 42; Arguments = @() }
    $dotnetStub = {
        $dotnetStubState.Arguments = $args
        $global:LASTEXITCODE = $dotnetStubState.ExitCode
    }.GetNewClosure()
    Set-Item -LiteralPath Function:\dotnet -Value $dotnetStub
    try {
        $failedAsExpected = $false
        try { & $helper -Version '2.2.0' -Distribute }
        catch {
            if ($_.Exception.Message -notmatch 'Publishing failed') { throw }
            $failedAsExpected = $true
        }
        if (-not $failedAsExpected -or (Test-Path -LiteralPath $copyMarker)) { throw 'A failed publish reached distribution.' }
        if ($dotnetStubState.Arguments -notcontains '-p:PublishVersion=2.2.0') { throw 'Explicit version was not forwarded.' }
        $dotnetStubState.ExitCode = 0
        & $helper -NoDistribute
        if (Test-Path -LiteralPath $copyMarker) { throw '-NoDistribute invoked the copy helper.' }
        if ($dotnetStubState.Arguments -match 'PublishVersion') { throw 'Automatic publishing passed an explicit version.' }
        & $helper -Version '2.2.0' -Distribute
        if (-not (Test-Path -LiteralPath $copyMarker)) { throw 'Successful publishing did not invoke distribution.' }
    }
    finally { Remove-Item -LiteralPath Function:\dotnet }
    Write-Host 'PASS: helper forwards overrides and distributes only after successful publishing.'
}
finally {
    if ((Get-Content -LiteralPath $realVersionFile -Raw) -ne $originalVersion) { throw 'The real version file changed during isolated checks.' }
    # Restore ordinary Release build metadata after the alternate-version publishes.
    & dotnet build $project --configuration Release --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the ordinary Release build.' }
}
Write-Host "Publish version checks passed. Isolated artifacts: $($results.FullName)"
