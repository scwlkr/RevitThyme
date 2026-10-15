# Only these first-party PE files are signed. Third-party DLLs and the bundled
# Codex runtime keep their upstream signatures and hashes.
function Get-DevelopmentSigningFiles {
    @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','RevitThyme.Codex.dll',
      'cli/revitthyme.exe','cli/rthyme.exe','cli/revitthyme.dll','cli/RevitThyme.Core.dll',
      'mcp/revitthyme-mcp.exe','mcp/revitthyme-mcp.dll','mcp/RevitThyme.Core.dll')
}

function Find-DevelopmentSignTool {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $sdk)) { return $null }
    foreach ($version in @(Get-ChildItem -LiteralPath $sdk -Directory | Where-Object Name -Match '^\d+\.\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending)) {
        $candidate = Join-Path $version.FullName 'x64\signtool.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return Assert-NativeHostPath $candidate }
    }
    $candidate = Join-Path $sdk 'x64\signtool.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return Assert-NativeHostPath $candidate }
    return $null
}

function Get-DevelopmentPackagePaths([string]$Directory, [switch]$ForSigning) {
    if (-not $Directory) { throw 'PackageDirectory is required for Sign and Verify.' }
    $folder = Assert-NativeHostPath $Directory
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "Package directory is missing: $folder" }
    if ($ForSigning) {
        if ((Split-Path $folder -Leaf) -notmatch '^RevitThyme-[\w.-]+-[a-f0-9]{32}$' -or
            (Test-Path -LiteralPath (Join-Path $folder 'manifest.json'))) {
            throw 'Sign requires an unhashed staged RevitThyme-<name>-<UUID> package, before manifest.json is generated. Installed and immutable packages cannot be signed.'
        }
        $install = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\foundation'
        if ($folder.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Cannot sign an installed bundle.' }
        $registration = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.Foundation.addin'
        if (Test-Path -LiteralPath $registration) {
            [xml]$xml = Get-Content -LiteralPath (Assert-NativeHostPath $registration) -Raw
            foreach ($entry in @($xml.RevitAddIns.AddIn)) {
                if ($entry.Assembly) {
                    $registered = Split-Path ([IO.Path]::GetFullPath([string]$entry.Assembly)) -Parent
                    if ($folder -eq $registered -or $folder.StartsWith($registered+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Cannot sign the registered bundle.' }
                }
            }
        }
    }
    $paths = @()
    foreach ($relative in Get-DevelopmentSigningFiles) {
        $path = Assert-NativeHostPath (Join-Path $folder $relative)
        if (-not $path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required first-party package binary is missing or outside the package: $relative"
        }
        $paths += [pscustomobject]@{relative=$relative;path=$path}
    }
    return $paths
}

function Assert-DevelopmentSignature([string]$Path, [string]$Thumbprint, [string]$SignTool) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "Signature is invalid or has an unexpected signer: $Path ($($signature.Status))"
    }
    $output = & $SignTool verify /pa /all /q $Path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed: $Path`n$($output -join [Environment]::NewLine)" }
}

function Invoke-DevelopmentPackageSigning([string]$Directory, $Certificate, [string]$SignTool, [switch]$Sign) {
    $paths = @(Get-DevelopmentPackagePaths $Directory -ForSigning:$Sign)
    $files = @()
    foreach ($file in $paths) {
        if ($Sign) {
            if ($file.relative -eq 'cli/rthyme.exe') {
                # Keep the existing byte-identical CLI alias contract; independent
                # signatures can encode different signing times.
                $primary = @($paths | Where-Object relative -eq 'cli/revitthyme.exe')[0]
                Copy-Item -LiteralPath $primary.path -Destination $file.path
            } else {
                $signature = Get-AuthenticodeSignature -LiteralPath $file.path
                if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
                    $output = & $SignTool sign /s My /sha1 $Certificate.Thumbprint /fd SHA256 /q $file.path 2>&1
                    if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed: $($file.path)`n$($output -join [Environment]::NewLine)" }
                }
            }
        }
        Assert-DevelopmentSignature $file.path $Certificate.Thumbprint $SignTool
        $files += [pscustomobject]@{path=$file.relative;sha256=(Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash.ToLowerInvariant();verified=$true}
    }
    $primaryHash = @($files | Where-Object path -eq 'cli/revitthyme.exe')[0].sha256
    $aliasHash = @($files | Where-Object path -eq 'cli/rthyme.exe')[0].sha256
    if ($primaryHash -ne $aliasHash) { throw 'The signed CLI alias must be byte-identical to revitthyme.exe.' }
    return $files
}
