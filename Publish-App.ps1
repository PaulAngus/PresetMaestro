param(
    [string]$Version,
    [switch]$Distribute,
    [switch]$NoDistribute
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Publish-App.ps1 produces the Windows release. Use Publish-Mac.ps1 on macOS.'
}

if ($Distribute -and $NoDistribute) { throw 'Choose either -Distribute or -NoDistribute.' }
if ($PSBoundParameters.ContainsKey('Version') -and [string]::IsNullOrWhiteSpace($Version)) {
    throw 'An explicit version cannot be empty. Omit -Version to increment automatically.'
}

$csproj = Join-Path $PSScriptRoot 'PresetMaestro\PresetMaestro.csproj'
$publishArguments = @('publish', $csproj, '--configuration', 'Release', '--runtime', 'win-x64')
if ($PSBoundParameters.ContainsKey('Version')) { $publishArguments += "-p:PublishVersion=$($Version.Trim())" }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; nothing was distributed.' }

$copyScript = Join-Path $PSScriptRoot 'Copy-PublishedExe.ps1'
if (-not $NoDistribute) {
    if (-not (Test-Path -LiteralPath $copyScript)) {
        if ($Distribute) { throw "Distribution helper not found: $copyScript" }
        return
    }
    $copy = if ($Distribute) { 'Y' } else { Read-Host 'Copy published exe to distribution folders now? (Y/n)' }
    if ($copy -notmatch '^[Nn]') {
        & $copyScript
    }
}
