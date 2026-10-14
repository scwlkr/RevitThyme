param([string]$PackageDirectory, [string]$InstallDirectory, [string]$AddinsDirectory, [switch]$Isolated)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Isolated) { $null = Assert-IsolatedInstallTest $InstallDirectory $AddinsDirectory }
elseif (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before installing. Preserve unsaved models.' }
if (-not $PackageDirectory) { $PackageDirectory = $PSScriptRoot }
$package = Assert-NativeHostPath $PackageDirectory
$manifest = Assert-FoundationPackage $package
if (-not $InstallDirectory) { $InstallDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\foundation' }
$install = Assert-NativeHostPath $InstallDirectory
$registration = Get-FoundationRegistration $AddinsDirectory
$revit = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
if (-not (Test-Path -LiteralPath $revit)) { throw 'Licensed Revit 2027 is required.' }
$old = $null
if (Test-Path -LiteralPath $registration) {
    $oldFolder = Read-FoundationRegistration $registration
    $null = Assert-FoundationPackage $oldFolder
    $old = [IO.File]::ReadAllText($registration)
}
# Immutable versioned folders make rollback independent of future build outputs.
$identity = (Get-FileHash -LiteralPath (Join-Path $package 'manifest.json')).Hash.ToLowerInvariant()
$target = Join-Path $install $identity
if (Test-Path -LiteralPath $target) {
    $null = Assert-FoundationPackage $target
    if ((Get-FileHash -LiteralPath (Join-Path $target 'manifest.json')).Hash.ToLowerInvariant() -ne $identity) { throw 'Existing immutable bundle differs.' }
} else {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($entry in $manifest.files.PSObject.Properties) {
        $destination = Join-Path $target $entry.Name
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $package $entry.Name) -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $package 'manifest.json') -Destination (Join-Path $target 'manifest.json')
    $null = Assert-FoundationPackage $target
}
$assembly = [Security.SecurityElement]::Escape((Join-Path $target 'RevitThyme.Addin.dll'))
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>RevitThyme Foundation</Name><Assembly>$assembly</Assembly><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme native C# toolkit</VendorDescription></AddIn></RevitAddIns>
"@
$backup = Join-Path $install ('registration-backup-'+[Guid]::NewGuid().ToString('N')+'.addin')
if ($old) { [IO.File]::WriteAllText($backup, $old) }
New-Item -ItemType Directory -Path (Split-Path $registration -Parent) -Force | Out-Null
try {
    if ($old -and [IO.File]::ReadAllText($registration) -ne $old) { throw 'Registration changed during installation.' }
    if (-not $old -and (Test-Path -LiteralPath $registration)) { throw 'A registration appeared during installation. Preserve it.' }
    [IO.File]::WriteAllText($registration, $xml)
    $null = Assert-NativeHostPath $registration
    if ([IO.File]::ReadAllText($registration) -ne $xml) { throw 'Registration readback failed.' }
} catch {
    if ($old) { [IO.File]::WriteAllText($registration, $old) }
    elseif ((Test-Path -LiteralPath $registration) -and [IO.File]::ReadAllText($registration) -eq $xml) { Remove-Item -LiteralPath $registration }
    throw
}
@{installed=$target;registration=$registration;cli=(Join-Path $target 'cli\revitthyme.exe');mcp=$(if($manifest.version -in @('0.5.0-mcp','0.6.0-utilities')){Join-Path $target 'mcp\revitthyme-mcp.exe'}else{$null});previous_registration_backup=$(if($old){$backup}else{$null})} | ConvertTo-Json
