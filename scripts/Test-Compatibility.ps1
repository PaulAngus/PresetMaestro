param(
    [Parameter(Mandatory)][ValidateSet('FM9', 'FM3', 'AxeFxIIIOriginal', 'AxeFxIIIMarkII', 'AxeFxIIIMarkIITurbo')][string]$Model,
    [string]$InputPort, [string]$OutputPort,
    [switch]$AllowNavigation, [switch]$FullScan,
    [int[]]$Slots, [ValidateRange(1,16)][int]$MidiChannel = 1,
    [ValidateRange(0,16383)][Nullable[int]]$AmpTypeParameter
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$values = @{
    PRESET_MAESTRO_COMPATIBILITY = $Model
    PRESET_MAESTRO_MIDI_IN = $InputPort
    PRESET_MAESTRO_MIDI_OUT = $OutputPort
    PRESET_MAESTRO_ALLOW_NAVIGATION = $(if ($AllowNavigation) { '1' } else { '0' })
    PRESET_MAESTRO_FULL_SCAN = $(if ($FullScan) { '1' } else { '0' })
    PRESET_MAESTRO_TEST_SLOTS = $Slots -join ','
    PRESET_MAESTRO_MIDI_CHANNEL = [string]$MidiChannel
    PRESET_MAESTRO_AMP_TYPE_PARAMETER = [string]$AmpTypeParameter
    PRESET_MAESTRO_HARDWARE_RESULTS = Join-Path $root 'build_test/compatibility'
}
$previous = @{}
foreach ($name in $values.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $values[$name]) }
    if ($AllowNavigation) { Write-Host 'Navigation enabled: active presets and scenes WILL change. Unsaved edits cannot be restored. No preset saves or parameter writes are allowed.' }
    dotnet test (Join-Path $root 'PresetMaestro.slnx') --configuration Release --filter 'Category=Compatibility' --logger 'console;verbosity=normal' --logger trx --results-directory $values.PRESET_MAESTRO_HARDWARE_RESULTS
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility tests found gaps or failures. See JSON reports and MIDI traces.' }
}
finally { foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
