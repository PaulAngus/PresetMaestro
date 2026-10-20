param(
    [string]$InputPort, [string]$OutputPort, [switch]$FullScan,
    [switch]$AllowNavigation,
    [ValidateRange(1,16)][int]$MidiChannel = 1,
    [ValidateRange(0,127)][Nullable[int]]$SceneCc
)
$ErrorActionPreference = 'Stop'
if ($AllowNavigation -and $null -eq $SceneCc) { throw 'Specify -SceneCc matching the FM9 Scene Select assignment before enabling navigation.' }
$root = Split-Path $PSScriptRoot -Parent
$names = @('PRESET_MAESTRO_HARDWARE', 'PRESET_MAESTRO_MIDI_IN', 'PRESET_MAESTRO_MIDI_OUT', 'PRESET_MAESTRO_FULL_SCAN', 'PRESET_MAESTRO_HARDWARE_RESULTS', 'PRESET_MAESTRO_ALLOW_NAVIGATION', 'PRESET_MAESTRO_MIDI_CHANNEL', 'PRESET_MAESTRO_SCENE_CC')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:PRESET_MAESTRO_HARDWARE = 'FM9'
    $env:PRESET_MAESTRO_MIDI_IN = $InputPort
    $env:PRESET_MAESTRO_MIDI_OUT = $OutputPort
    $env:PRESET_MAESTRO_FULL_SCAN = if ($FullScan) { '1' } else { '0' }
    $env:PRESET_MAESTRO_ALLOW_NAVIGATION = if ($AllowNavigation) { '1' } else { '0' }
    $env:PRESET_MAESTRO_MIDI_CHANNEL = [string]$MidiChannel
    $env:PRESET_MAESTRO_SCENE_CC = [string]$SceneCc
    if ($AllowNavigation) { Write-Host 'FM9 application acceptance enabled: presets and scenes WILL change. Save edits first. Tests refuse an edited starting preset and attempt to restore the starting selection. No preset stores or parameter writes are permitted.' }
    $env:PRESET_MAESTRO_HARDWARE_RESULTS = Join-Path $root 'build_test/hardware'
    dotnet test (Join-Path $root 'PresetMaestro.slnx') --configuration Release --filter 'Category=Hardware' --logger 'console;verbosity=normal' --logger trx --results-directory $env:PRESET_MAESTRO_HARDWARE_RESULTS
    if ($LASTEXITCODE -ne 0) { throw 'Hardware tests failed. See the test results and MIDI traces.' }
}
finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
