param([switch]$WithSigningIdentity, [string]$ConfigPath, [string]$BinaryPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'host-path.ps1')
. (Join-Path $PSScriptRoot 'dev-signing-package.ps1')
$run = Join-Path $root ('artifacts\development\signing-check-'+[Guid]::NewGuid().ToString('N'))
$stage = Join-Path $run ('RevitThyme-signing-check-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) {
    $checks.Add(@{name=$Name;passed=$Passed})
    if (-not $Passed) { throw "Development signing check failed: $Name" }
}
function Refuses([scriptblock]$Operation, [string]$Pattern) {
    try { & $Operation | Out-Null; return $false } catch { return $_.Exception.Message -like $Pattern }
}
try {
    # The default check creates files only under this UUID; it never creates a key
    # or changes any certificate store, add-in registration, or installed bundle.
    $absent = Join-Path $run 'absent-signing.json'
    $status = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Status -ConfigPath $absent | ConvertFrom-Json
    Check 'status is read-only before explicit setup' (-not $status.configured -and -not $status.ready -and -not (Test-Path -LiteralPath $absent))
    Check 'missing package binaries rejected before signing' (Refuses { Get-DevelopmentPackagePaths $stage -ForSigning } '*Required first-party package binary*')
    foreach ($relative in Get-DevelopmentSigningFiles) {
        $path = Join-Path $stage $relative
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path,'path-guard fixture')
    }
    $thirdParty = Join-Path $stage 'Microsoft.CodeAnalysis.dll'
    [IO.File]::WriteAllText($thirdParty,'untouched third-party fixture')
    $paths = @(Get-DevelopmentPackagePaths $stage -ForSigning)
    Check 'allowlist excludes third-party PE files' ($paths.Count -eq 11 -and $thirdParty -notin @($paths.path))
    $manifest = Join-Path $stage 'manifest.json'
    [IO.File]::WriteAllText($manifest,'{}')
    Check 'hashed packages cannot be signed in place' (Refuses { Get-DevelopmentPackagePaths $stage -ForSigning } '*unhashed staged*')
    Check 'verification permits an immutable package' (@(Get-DevelopmentPackagePaths $stage).Count -eq 11)
    Remove-Item -LiteralPath $manifest
    Check 'installed-style directories cannot be signed' (Refuses { Get-DevelopmentPackagePaths $run -ForSigning } '*unhashed staged*')
    if ($WithSigningIdentity) {
        # Explicit opt-in: the identity must already be configured by the operator.
        $status = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Status -ConfigPath $ConfigPath | ConvertFrom-Json
        Check 'opt-in signing uses an already ready identity' $status.ready
        $beforeConfig = [IO.File]::ReadAllText($status.config_path)
        $beforeStores = @(Get-ChildItem Cert:\CurrentUser\My,Cert:\CurrentUser\Root,Cert:\CurrentUser\TrustedPublisher | ForEach-Object { $_.PSPath } | Sort-Object) -join '|'
        $setup = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Setup -ConfigPath $ConfigPath | ConvertFrom-Json
        Check 'setup itself reports ready using freshly reopened trust stores' ($setup.ready -and $setup.trusted_root -and $setup.trusted_publisher)
        $afterStores = @(Get-ChildItem Cert:\CurrentUser\My,Cert:\CurrentUser\Root,Cert:\CurrentUser\TrustedPublisher | ForEach-Object { $_.PSPath } | Sort-Object) -join '|'
        Check 'repeated setup preserves identity, config, and certificate stores' ($setup.thumbprint -eq $status.thumbprint -and [IO.File]::ReadAllText($status.config_path) -ceq $beforeConfig -and $beforeStores -ceq $afterStores)
        if (-not $BinaryPath) { $BinaryPath = Join-Path $root 'src\RevitThyme.Addin\bin\Release\net10.0-windows\RevitThyme.Core.dll' }
        $binary = Assert-NativeHostPath $BinaryPath
        Check 'portable test payload is a first-party build output' ((Split-Path $binary -Leaf) -eq 'RevitThyme.Core.dll' -and (Test-Path -LiteralPath $binary -PathType Leaf))
        $sourceHash = (Get-FileHash -LiteralPath $binary).Hash
        $thirdPartyHash = (Get-FileHash -LiteralPath $thirdParty).Hash
        foreach ($file in $paths) { Copy-Item -LiteralPath $binary -Destination $file.path }
        $signed = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Sign -ConfigPath $ConfigPath -PackageDirectory $stage | ConvertFrom-Json
        Check 'every allowlisted staged binary has the pinned signer' (@($signed.files | Where-Object verified).Count -eq 11)
        Check 'signed CLI alias retains byte-identical contract' ((Get-FileHash -LiteralPath (Join-Path $stage 'cli\rthyme.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $stage 'cli\revitthyme.exe')).Hash)
        $second = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Sign -ConfigPath $ConfigPath -PackageDirectory $stage | ConvertFrom-Json
        Check 'repeated signing preserves valid signed bytes' (($signed.files.sha256 -join '|') -ceq ($second.files.sha256 -join '|'))
        Check 'source build and third-party bytes are preserved' ((Get-FileHash -LiteralPath $binary).Hash -eq $sourceHash -and (Get-FileHash -LiteralPath $thirdParty).Hash -eq $thirdPartyHash)
        [IO.File]::WriteAllText($manifest,'{}')
        $verified = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Verify -ConfigPath $ConfigPath -PackageDirectory $stage | ConvertFrom-Json
        Check 'verification succeeds after package hashing' (@($verified.files | Where-Object verified).Count -eq 11)
        $tampered = Join-Path $stage 'RevitThyme.Core.dll'
        $bytes = [IO.File]::ReadAllBytes($tampered); $bytes[100] = $bytes[100] -bxor 1
        [IO.File]::WriteAllBytes($tampered,$bytes)
        Check 'tampered signed PE is rejected' (Refuses { & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Verify -ConfigPath $ConfigPath -PackageDirectory $stage } '*Signature is invalid*')
    }
    $report = [ordered]@{signing_identity_opt_in=[bool]$WithSigningIdentity;checks=@($checks.ToArray())}
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
    $report | ConvertTo-Json -Depth 5
} finally {
    # Delete only this check's explicitly resolved disposable package, preserving its report.
    $target = [IO.Path]::GetFullPath($stage)
    if ((Split-Path $target -Parent) -eq [IO.Path]::GetFullPath($run) -and (Split-Path $target -Leaf) -match '^RevitThyme-signing-check-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $target)) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
