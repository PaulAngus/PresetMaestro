param([string]$InputPort, [string]$OutputPort, [switch]$FullScan)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$names = @('PRESET_MAESTRO_HARDWARE', 'PRESET_MAESTRO_MIDI_IN', 'PRESET_MAESTRO_MIDI_OUT', 'PRESET_MAESTRO_FULL_SCAN', 'PRESET_MAESTRO_HARDWARE_RESULTS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:PRESET_MAESTRO_HARDWARE = 'FM9'
    $env:PRESET_MAESTRO_MIDI_IN = $InputPort
    $env:PRESET_MAESTRO_MIDI_OUT = $OutputPort
    $env:PRESET_MAESTRO_FULL_SCAN = if ($FullScan) { '1' } else { '0' }
    $env:PRESET_MAESTRO_HARDWARE_RESULTS = Join-Path $root 'build_test/hardware'
    dotnet test (Join-Path $root 'PresetMaestro.slnx') --configuration Release --filter 'Category=Hardware' --logger 'console;verbosity=normal' --logger trx --results-directory $env:PRESET_MAESTRO_HARDWARE_RESULTS
    if ($LASTEXITCODE -ne 0) { throw 'Hardware tests failed. See the test results and MIDI traces.' }
}
finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
