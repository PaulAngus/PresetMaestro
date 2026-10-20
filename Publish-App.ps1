param(
    [string]$Version,
    [switch]$Sign,
    [string]$SigningCertificateThumbprint = $env:MBU_WINDOWS_SIGNING_THUMBPRINT,
    [string]$SignToolPath = $env:MBU_SIGNTOOL_PATH,
    [uri]$TimestampServer = 'http://time.certum.pl'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Publish-App.ps1 produces the Windows release. Use Publish-Mac.ps1 on macOS.'
}

if ($PSBoundParameters.ContainsKey('Version') -and [string]::IsNullOrWhiteSpace($Version)) {
    throw 'An explicit version cannot be empty. Omit -Version to increment automatically.'
}

$signScript = Join-Path $PSScriptRoot 'dependencies/midi/Sign-WindowsExecutable.ps1'
$signing = @{ Sign = $Sign; SigningCertificateThumbprint = $SigningCertificateThumbprint; SignToolPath = $SignToolPath; TimestampServer = $TimestampServer }
& $signScript @signing -CheckOnly

$csproj = Join-Path $PSScriptRoot 'PresetMaestro\PresetMaestro.csproj'
$buildRoot = Join-Path $PSScriptRoot ('artifacts/publish-' + [guid]::NewGuid().ToString('N'))
$publishArguments = @('publish', $csproj, '--configuration', 'Release', '--runtime', 'win-x64', '--artifacts-path', $buildRoot)
if ($PSBoundParameters.ContainsKey('Version')) { $publishArguments += "-p:PublishVersion=$($Version.Trim())" }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }

& $signScript @signing -ExecutablePath (Join-Path $PSScriptRoot 'publish/PresetMaestro.exe')
