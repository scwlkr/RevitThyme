# Build-time provenance check only. Installed bundles use their immutable manifest hashes.
function Assert-Clipper2Payload([string]$ArchivePath, [string]$LicensePath, [string[]]$AssemblyPaths) {
    $expected = 'f07dfcf6adccd035fde042f0ff6fd6d746b989e9bb361cda38216f185304172b'
    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        throw 'Pinned Clipper2 2.0.0 package checksum mismatch; restore the official package and preserve the mismatched archive.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $assembly = $archive.GetEntry('lib/netstandard2.0/Clipper2Lib.dll')
        $license = $archive.GetEntry('License.txt')
        if (-not $assembly -or -not $license) { throw 'Pinned Clipper2 package payload is incomplete.' }
        $stream = $assembly.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $assemblyHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        $reader = [IO.StreamReader]::new($license.Open())
        try { $upstreamLicense = $reader.ReadToEnd() } finally { $reader.Dispose() }
        # Git may convert LF to CRLF on checkout; every license character otherwise must match upstream.
        if (-not (Test-Path -LiteralPath $LicensePath -PathType Leaf) -or
            [IO.File]::ReadAllText($LicensePath).Replace("`r`n", "`n") -cne $upstreamLicense.Replace("`r`n", "`n")) {
            throw 'Clipper2 license notice differs from the pinned upstream package.'
        }
        if (-not $AssemblyPaths -or $AssemblyPaths.Count -eq 0) { throw 'Supply the managed Clipper2 payload paths to verify.' }
        foreach ($path in $AssemblyPaths) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $assemblyHash) {
                throw "Clipper2 managed payload differs from the pinned upstream package: $path"
            }
        }
        return @{version='2.0.0';package_sha256=$expected;assembly_sha256=$assemblyHash}
    } finally { $archive.Dispose() }
}
