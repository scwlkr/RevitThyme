# Build-time provenance check only. Installed bundles use their immutable manifest hashes.
function Assert-IxMiliaDxfPayload([string]$ArchivePath, [string]$LicensePath, [string[]]$AssemblyPaths) {
    $expected = '3b08f5b604c958ea6c29f107f751d75abdf3328713ec586bc4fdbf0302e24c81'
    $expectedAssembly = '70460eae6fd10ca6ca35653cb52eb03c2cbba2240b0afbda98afc744ad5dc67f'
    # NuGet omits the notice. Pin the complete v0.8.4 upstream LICENSE.txt, normalized to LF.
    $expectedLicense = '3ed8c81ca8fead8d8d7d79b45b3196083c79f7f7c4a5842c4ce305c4b2a9a9f7'
    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        throw 'Pinned IxMilia.Dxf 0.8.4 package checksum mismatch; restore the official package and preserve the mismatched archive.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $assembly = $archive.GetEntry('lib/netstandard2.0/IxMilia.Dxf.dll')
        if (-not $assembly) { throw 'Pinned IxMilia.Dxf package payload is incomplete.' }
        $stream = $assembly.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $assemblyHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($assemblyHash -ne $expectedAssembly) { throw 'Pinned IxMilia.Dxf archive assembly checksum mismatch.' }
        if (-not (Test-Path -LiteralPath $LicensePath -PathType Leaf)) { throw 'IxMilia.Dxf license notice is missing.' }
        $license = [IO.File]::ReadAllText($LicensePath).Replace("`r`n", "`n")
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $licenseHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($license))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($licenseHash -ne $expectedLicense) { throw 'IxMilia.Dxf license notice differs from the pinned upstream source.' }
        if (-not $AssemblyPaths -or $AssemblyPaths.Count -eq 0) { throw 'Supply the managed IxMilia.Dxf payload paths to verify.' }
        foreach ($path in $AssemblyPaths) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $assemblyHash) {
                throw "IxMilia.Dxf managed payload differs from the pinned upstream package: $path"
            }
        }
        return @{version='0.8.4';package_sha256=$expected;assembly_sha256=$assemblyHash;license_lf_sha256=$licenseHash}
    } finally { $archive.Dispose() }
}
