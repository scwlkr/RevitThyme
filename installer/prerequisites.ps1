function Get-SupportedRevitInstallations([string[]]$Years, [string]$IsolatedRoot) {
    $found = @()
    foreach ($year in $Years) {
        $candidates = @()
        if ($IsolatedRoot) {
            $candidates = @(Join-Path $IsolatedRoot ('revit\'+$year))
        } else {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64')
            try {
                $key = $base.OpenSubKey('SOFTWARE\Autodesk\Revit\'+$year)
                if ($key) {
                    try {
                        $candidates += [string]$key.GetValue('InstallationLocation')
                        foreach ($name in $key.GetSubKeyNames()) {
                            $product = $key.OpenSubKey($name)
                            try { $candidates += [string]$product.GetValue('InstallationLocation') }
                            finally { $product.Dispose() }
                        }
                    } finally { $key.Dispose() }
                }
            } finally { $base.Dispose() }
            $candidates += Join-Path ([Environment]::GetFolderPath('ProgramFiles')) ('Autodesk\Revit '+$year)
        }
        foreach ($candidate in @($candidates | Where-Object { $_ } | Select-Object -Unique)) {
            if (-not (Test-Path -LiteralPath (Join-Path $candidate 'Revit.exe') -PathType Leaf) -or
                -not (Test-Path -LiteralPath (Join-Path $candidate 'RevitAPI.dll') -PathType Leaf)) { continue }
            # Program Files is read-only for ordinary users; resolve an existing file without a write probe.
            $directory = Split-Path (Assert-NativeHostPath (Join-Path $candidate 'Revit.exe')) -Parent
            if (-not $IsolatedRoot) {
                $major = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $directory 'RevitAPI.dll')).Version.Major
                if ($major -ne ([int]$year - 2000)) { continue }
            }
            $found += [pscustomobject]@{year=$year;directory=$directory}
            break
        }
    }
    return $found
}

function Assert-ToolkitRuntime {
    # Windows PowerShell and Revit can use different registry views. Check both x64 runtime records.
    $runtimes = @{}
    foreach ($view in @('Registry64','Registry32')) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine',$view)
        try {
            foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
                $key = $base.OpenSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\'+$framework)
                if ($key) {
                    try {
                        if (@($key.GetValueNames() | Where-Object { $_ -match '^10\.0\.\d+$' }).Count) { $runtimes[$framework] = $true }
                    } finally { $key.Dispose() }
                }
            }
        } finally { $base.Dispose() }
    }
    if ($runtimes.Count -ne 2) {
        throw 'The x64 .NET 10 Desktop Runtime is required. Repair the Revit 2027 runtime installation, then retry. No SDK is needed.'
    }
}
