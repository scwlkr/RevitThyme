param([string]$Path)
$ErrorActionPreference = 'Stop'

# MSIX AppData redirection can make a successful write invisible to native Revit.
function Assert-NativeHostPath([string]$Target) {
    $full = [IO.Path]::GetFullPath($Target).TrimEnd('\')
    if ($full -match '\\AppData\\Local\\Packages\\[^\\]+\\LocalCache\\(Roaming|Local)(\\|$)') {
        throw 'Package-redirected AppData is not the native Revit installation. Use an ordinary PowerShell session or an explicitly verified native path.'
    }
    $ancestor = $full
    while (-not (Test-Path -LiteralPath $ancestor)) {
        $next = Split-Path $ancestor -Parent
        if (-not $next -or $next -eq $ancestor) { throw "Cannot resolve installation parent: $full" }
        $ancestor = $next
    }
    if (-not ('RevitThyme.HostPath' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace RevitThyme {
    public static class HostPath {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
        public static string Resolve(string path, bool probe) {
            // A directory handle can resolve natively even when writes below it redirect.
            // CREATE_NEW plus DELETE_ON_CLOSE confines the probe to its own temporary file.
            using (var handle = CreateFileW(path, probe ? 0x10000u : 0, 7, IntPtr.Zero,
                                           probe ? 1u : 3u, probe ? 0x04000100u : 0x02000000u, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer = new StringBuilder(32768);
                uint size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (size >= buffer.Capacity) throw new InvalidOperationException("Physical path exceeds buffer");
                return buffer.ToString();
            }
        }
    }
}
'@
    }
    $probe = Test-Path -LiteralPath $ancestor -PathType Container
    $expected = $ancestor
    if ($probe) { $expected = Join-Path $ancestor ('RevitThyme-path-check-' + [Guid]::NewGuid().ToString('N') + '.tmp') }
    $physical = [RevitThyme.HostPath]::Resolve($expected, $probe)
    if ($physical.StartsWith('\\?\UNC\')) { $physical = '\\' + $physical.Substring(8) }
    elseif ($physical.StartsWith('\\?\')) { $physical = $physical.Substring(4) }
    if (-not [string]::Equals($physical.TrimEnd('\'), $expected.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Host path redirects to a different physical location: $expected -> $physical. Use an ordinary PowerShell session or the verified native path."
    }
    return $full
}

if ($Path) { Assert-NativeHostPath $Path }
