# Notify the Windows shell after replacing an executable or shortcut's icon.
# This refreshes those items without restarting Explorer or deleting its caches.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { return }

if (-not ('PresetMaestro.ShellIconRefresh' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PresetMaestro {
    public static class ShellIconRefresh {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
'@
}

foreach ($itemPath in $Path) {
    $resolvedPath = (Resolve-Path -LiteralPath $itemPath -ErrorAction Stop).ProviderPath
    $nativePath = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($resolvedPath)
    try {
        # SHCNE_UPDATEITEM; SHCNF_PATHW | SHCNF_FLUSH.
        [PresetMaestro.ShellIconRefresh]::SHChangeNotify(0x2000, 0x1005, $nativePath, [IntPtr]::Zero)
        Write-Host "Refreshed shell icon: $resolvedPath"
    }
    finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($nativePath) }
}
