param(
    [ValidateSet('Setup','Status','Sign','Verify')][string]$Action = 'Status',
    [string]$ConfigPath,
    [string]$PackageDirectory
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Development signing requires 64-bit Windows PowerShell.' }
. (Join-Path $PSScriptRoot 'host-path.ps1')
. (Join-Path $PSScriptRoot 'dev-signing-package.ps1')
if (-not $ConfigPath) { $ConfigPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\development\signing.json' }
$configFile = Assert-NativeHostPath $ConfigPath
$subject = 'CN=WLKR LABS RevitThyme Development'
$signTool = Find-DevelopmentSignTool

function Get-ConfiguredDevelopmentCertificate($Config) {
    if ($Config.schema -ne 1 -or $Config.subject -cne $subject -or $Config.store -cne 'CurrentUser/My' -or $Config.thumbprint -notmatch '^[A-Fa-f0-9]{40}$') {
        throw 'Development signing configuration is invalid. Preserve it and select the intended persistent identity explicitly.'
    }
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$Config.thumbprint) -ErrorAction SilentlyContinue
    if (-not $certificate -or $certificate.Subject -cne $subject) { throw 'The pinned development certificate is missing. No replacement will be created automatically.' }
    return $certificate
}

function Test-PrivateKeyNonexportable($Certificate) {
    if (-not $Certificate.HasPrivateKey) { return $false }
    $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    try { return ($key -is [Security.Cryptography.RSACng] -and $key.Key.ExportPolicy -eq [Security.Cryptography.CngExportPolicies]::None) }
    finally { if ($key) { $key.Dispose() } }
}

function Assert-DevelopmentCertificate($Certificate) {
    $now = Get-Date
    if ($Certificate.NotBefore -gt $now -or $Certificate.NotAfter -le $now) { throw 'The pinned development certificate is not currently valid. No renewal or replacement is automatic.' }
    if ($Certificate.PublicKey.Oid.Value -ne '1.2.840.113549.1.1.1' -or $Certificate.SignatureAlgorithm.Value -ne '1.2.840.113549.1.1.11' -or
        @($Certificate.EnhancedKeyUsageList | Where-Object ObjectId -eq '1.3.6.1.5.5.7.3.3').Count -ne 1) { throw 'The pinned identity must be an RSA/SHA-256 code-signing certificate.' }
    if (-not (Test-PrivateKeyNonexportable $Certificate)) { throw 'The pinned signing key must be present and nonexportable in CurrentUser/My.' }
}

function Test-DevelopmentPublicTrust($Certificate, [ValidateSet('Root','TrustedPublisher')][string]$StoreName) {
    # The PowerShell certificate provider can cache absence across certutil imports.
    # Reopen the native store for every trust check, including this setup's receipt.
    $store = [Security.Cryptography.X509Certificates.X509Store]::new($StoreName,'CurrentUser')
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly -bor [Security.Cryptography.X509Certificates.OpenFlags]::OpenExistingOnly)
        $matches = @($store.Certificates | Where-Object Thumbprint -eq $Certificate.Thumbprint)
        if (-not $matches.Count) { return $false }
        foreach ($stored in $matches) {
            if ([Convert]::ToBase64String($stored.RawData) -cne [Convert]::ToBase64String($Certificate.RawData)) {
                throw "Public trust readback differs from the pinned signing identity in CurrentUser/$StoreName."
            }
        }
        return $true
    } finally { $store.Close() }
}

function Add-DevelopmentPublicTrust($Certificate, [ValidateSet('Root','TrustedPublisher')][string]$StoreName) {
    if (Test-DevelopmentPublicTrust $Certificate $StoreName) { return }
    $certutil = Join-Path $env:windir 'System32\certutil.exe'
    if (-not (Test-Path -LiteralPath $certutil -PathType Leaf)) { throw 'Windows certutil.exe is required for explicit development public trust setup.' }
    $publicFile = Assert-NativeHostPath (Join-Path (Split-Path $configFile -Parent) ('development-public-'+[Guid]::NewGuid().ToString('N')+'.cer'))
    try {
        # Export only public DER bytes. No private key material enters this file.
        $stream = [IO.File]::Open($publicFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $stream.Write($Certificate.RawData,0,$Certificate.RawData.Length) } finally { $stream.Dispose() }
        $null = Assert-NativeHostPath $publicFile
        $public = [Security.Cryptography.X509Certificates.X509Certificate2]::new($publicFile)
        try {
            if ($public.HasPrivateKey -or $public.Thumbprint -ne $Certificate.Thumbprint) { throw 'Temporary public certificate differs from the pinned signing identity.' }
        } finally { $public.Dispose() }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $certutil
        $start.Arguments = '-user -f -addstore '+$StoreName+' "'+$publicFile+'"'
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) {
                # Only the helper process created above is eligible for termination.
                $process.Kill(); $process.WaitForExit()
                throw 'Development public trust import did not finish within 30 seconds; the owned certutil helper was stopped. The pinned identity is preserved.'
            }
            $output = $stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw "Development public trust import failed for $StoreName`: $output" }
        } finally { $process.Dispose() }
        if (-not (Test-DevelopmentPublicTrust $Certificate $StoreName)) {
            throw "Pinned public certificate is missing after import to CurrentUser/$StoreName."
        }
        if (-not (Test-PrivateKeyNonexportable $Certificate)) { throw 'Signing key nonexportability changed during public trust setup.' }
    } finally { if (Test-Path -LiteralPath $publicFile) { Remove-Item -LiteralPath $publicFile } }
}

if ($Action -eq 'Setup') {
    $mutex = [Threading.Mutex]::new($false,('Local\RevitThymeDevelopmentSigning-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value))
    $locked = $false
    try {
        try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw 'Development signing setup is already running. Retry after it completes.' }
        if (-not $signTool) { throw 'Install Windows SDK x64 SignTool before development signing setup.' }
        if (-not (Test-Path -LiteralPath $configFile)) {
            $existing = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -CEQ $subject)
            if ($existing.Count -gt 1) { throw 'Multiple development identities exist. Select the intended certificate in signing.json; no identity will be replaced.' }
            if ($existing.Count -eq 1) { $certificate = $existing[0] }
            else {
                $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName 'WLKR LABS RevitThyme Development' `
                    -CertStoreLocation 'Cert:\CurrentUser\My' -Provider 'Microsoft Software Key Storage Provider' -KeyAlgorithm RSA -KeyLength 3072 `
                    -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3)
            }
            Assert-DevelopmentCertificate $certificate
            $config = [ordered]@{schema=1;subject=$subject;thumbprint=$certificate.Thumbprint;store='CurrentUser/My';created_utc=[DateTime]::UtcNow.ToString('o')}
            New-Item -ItemType Directory -Path (Split-Path $configFile -Parent) -Force | Out-Null
            $null = Assert-NativeHostPath $configFile
            # CreateNew preserves an identity selected concurrently by another setup.
            $stream = [IO.File]::Open($configFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            $writer = [IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
            try { $writer.Write(($config | ConvertTo-Json)) } finally { $writer.Dispose() }
            $null = Assert-NativeHostPath $configFile
        }
        $config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
        $certificate = Get-ConfiguredDevelopmentCertificate $config
        Assert-DevelopmentCertificate $certificate
        Add-DevelopmentPublicTrust $certificate 'Root'
        Add-DevelopmentPublicTrust $certificate 'TrustedPublisher'
    } finally {
        if ($locked) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

$result = [ordered]@{schema=1;action=$Action.ToLowerInvariant();config_path=$configFile;configured=$false;ready=$false;thumbprint=$null;subject=$subject;not_after_utc=$null;trusted_root=$false;trusted_publisher=$false;private_key_nonexportable=$false;signtool=$signTool}
if (Test-Path -LiteralPath $configFile) {
    $config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
    $certificate = Get-ConfiguredDevelopmentCertificate $config
    $result.configured = $true
    $result.thumbprint = $certificate.Thumbprint
    $result.not_after_utc = $certificate.NotAfter.ToUniversalTime().ToString('o')
    $result.trusted_root = Test-DevelopmentPublicTrust $certificate 'Root'
    $result.trusted_publisher = Test-DevelopmentPublicTrust $certificate 'TrustedPublisher'
    $result.private_key_nonexportable = Test-PrivateKeyNonexportable $certificate
    try {
        Assert-DevelopmentCertificate $certificate
        $result.ready = [bool]($signTool -and $result.trusted_root -and $result.trusted_publisher)
    } catch {
        if ($Action -ne 'Status') { throw }
        $result.problem = $_.Exception.Message
    }
}
if ($Action -ne 'Status' -and -not $result.ready) { throw 'Development signing is not ready. Run explicit Setup for the persistent identity and public trust first.' }
if ($Action -in @('Sign','Verify')) {
    $result.package_directory = Assert-NativeHostPath $PackageDirectory
    $result.files = @(Invoke-DevelopmentPackageSigning $PackageDirectory $certificate $signTool -Sign:($Action -eq 'Sign'))
}
$result | ConvertTo-Json -Depth 5
