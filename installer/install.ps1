param([string]$PackageDirectory, [string]$InstallDirectory, [string]$AddinsDirectory, [switch]$Isolated, [switch]$CheckOnly, [string]$ReceiptPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'global-command.ps1')
if ($Isolated) { $isolatedRoot = Assert-IsolatedInstallTest $InstallDirectory $AddinsDirectory }
elseif (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before installing. Preserve unsaved models.' }
if (-not $PackageDirectory) { $PackageDirectory = $PSScriptRoot }
$package = Assert-NativeHostPath $PackageDirectory
$manifest = Assert-FoundationPackage $package
if (-not $InstallDirectory) { $InstallDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\foundation' }
$install = Assert-NativeHostPath $InstallDirectory
$registration = Get-FoundationRegistration $AddinsDirectory
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell to install RevitThyme.' }
$detected = @(Get-SupportedRevitInstallations -Years @([string]$manifest.revit_year) -IsolatedRoot $isolatedRoot)
if (-not $detected.Count) { throw 'No supported Revit installation found. This package supports full Revit 2027 only.' }
Assert-ToolkitRuntime
$old = $null
if (Test-Path -LiteralPath $registration) {
    $oldFolder = Read-FoundationRegistration $registration
    if ((Split-Path $oldFolder -Parent) -ne $install -or (Split-Path $oldFolder -Leaf) -cnotmatch '^[a-f0-9]{64}$') { throw 'Registration belongs to a different installation. Preserve it.' }
    $null = Assert-FoundationPackage $oldFolder
    if ((Get-FileHash -LiteralPath (Join-Path $oldFolder 'manifest.json')).Hash.ToLowerInvariant() -cne (Split-Path $oldFolder -Leaf)) { throw 'Registered immutable bundle identity differs. Preserve it.' }
    $old = [IO.File]::ReadAllText($registration)
}
$oldReceipt = $null
if ($ReceiptPath) {
    $ReceiptPath = Assert-NativeHostPath $ReceiptPath
    if (Test-Path -LiteralPath $ReceiptPath) {
        if ((Get-Item -LiteralPath $ReceiptPath).IsReadOnly) { throw 'Installation receipt is read-only. Preserve it and its registration.' }
        $oldReceipt = [IO.File]::ReadAllText($ReceiptPath)
        if (($oldReceipt | ConvertFrom-Json).installed -ne $oldFolder) { throw 'Installation receipt belongs to a different bundle. Preserve it.' }
    }
}
# Immutable versioned folders make rollback independent of future build outputs.
$identity = (Get-FileHash -LiteralPath (Join-Path $package 'manifest.json')).Hash.ToLowerInvariant()
$target = Join-Path $install $identity
if (Test-Path -LiteralPath $target) {
    $null = Assert-FoundationPackage $target
    if ((Get-FileHash -LiteralPath (Join-Path $target 'manifest.json')).Hash.ToLowerInvariant() -ne $identity) { throw 'Existing immutable bundle differs.' }
}
$result = @{installed=$target;registration=$registration;cli=(Join-Path $target 'cli\revitthyme.exe');mcp=$(if($manifest.version -ne '0.4.0-foundation'){Join-Path $target 'mcp\revitthyme-mcp.exe'}else{$null});detected_revit=$detected;version=$manifest.version}
$commandPlan = New-CommandPlan $install $target $manifest $isolatedRoot $oldFolder
$result.global_command = $(if($commandPlan.result){$commandPlan.result.path}else{$null})
if ($CheckOnly) { $result | ConvertTo-Json -Depth 4; return }
if (-not (Test-Path -LiteralPath $target)) {
    $staging = Join-Path $install ('.pending-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    try {
        foreach ($entry in $manifest.files.PSObject.Properties) {
            $destination = Join-Path $staging $entry.Name
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $package $entry.Name) -Destination $destination
        }
        Copy-Item -LiteralPath (Join-Path $package 'manifest.json') -Destination (Join-Path $staging 'manifest.json')
        $null = Assert-FoundationPackage $staging
        [IO.Directory]::Move($staging,$target)
    } finally {
        $pending = [IO.Path]::GetFullPath($staging)
        if ((Split-Path $pending -Parent) -eq $install -and (Split-Path $pending -Leaf) -match '^\.pending-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $pending)) {
            Remove-Item -LiteralPath $pending -Recurse -Force
        }
    }
    $null = Assert-FoundationPackage $target
}
$assembly = [Security.SecurityElement]::Escape((Join-Path $target 'RevitThyme.Addin.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>RevitThyme Foundation</Name><Assembly>$assembly</Assembly><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme native C# toolkit</VendorDescription></AddIn></RevitAddIns>
"@
$backup = Join-Path $install ('registration-backup-'+[Guid]::NewGuid().ToString('N')+'.addin')
if ($old) { [IO.File]::WriteAllText($backup, $old) }
$result.previous_registration_backup = $(if($old){$backup}else{$null})
$json = $result | ConvertTo-Json -Depth 4
New-Item -ItemType Directory -Path (Split-Path $registration -Parent) -Force | Out-Null
$written = $false
$receiptWritten = $false
$temporaryReceipt = $null
try {
    if (-not $Isolated -and (Get-Process Revit -ErrorAction SilentlyContinue)) { throw 'Revit opened during installation. Close it normally, preserving unsaved work, then retry.' }
    if ($old -and [IO.File]::ReadAllText($registration) -ne $old) { throw 'Registration changed during installation.' }
    if (-not $old -and (Test-Path -LiteralPath $registration)) { throw 'A registration appeared during installation. Preserve it.' }
    [IO.File]::WriteAllText($registration, $xml)
    $written = $true
    $null = Assert-NativeHostPath $registration
    if ([IO.File]::ReadAllText($registration) -ne $xml) { throw 'Registration readback failed.' }
    Apply-CommandPlan $commandPlan
    if ($ReceiptPath) {
        $temporaryReceipt = Join-Path (Split-Path $ReceiptPath -Parent) ('.receipt-'+[Guid]::NewGuid().ToString('N')+'.tmp')
        [IO.File]::WriteAllText($temporaryReceipt,$json)
        if ($oldReceipt) { [IO.File]::Replace($temporaryReceipt,$ReceiptPath,($backup+'.receipt.json')) }
        else { [IO.File]::Move($temporaryReceipt,$ReceiptPath) }
        $receiptWritten = $true
        if ([IO.File]::ReadAllText($ReceiptPath) -ne $json) { throw 'Receipt readback failed.' }
    }
} catch {
    try { Undo-CommandPlan $commandPlan } catch { Write-Warning ('Global command rollback failed: '+$_.Exception.Message) }
    if ($written -and (Test-Path -LiteralPath $registration) -and [IO.File]::ReadAllText($registration) -eq $xml) {
        if ($old) { [IO.File]::WriteAllText($registration, $old) }
        else { Remove-Item -LiteralPath $registration }
    }
    if ($receiptWritten -and [IO.File]::ReadAllText($ReceiptPath) -eq $json) {
        if ($oldReceipt) { [IO.File]::WriteAllText($ReceiptPath,$oldReceipt) }
        else { Remove-Item -LiteralPath $ReceiptPath }
    }
    throw
} finally {
    if ($temporaryReceipt -and (Test-Path -LiteralPath $temporaryReceipt)) { Remove-Item -LiteralPath $temporaryReceipt }
}
$json
