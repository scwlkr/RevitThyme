param([string]$AddinsDirectory, [string]$InstallDirectory, [switch]$Isolated, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'global-command.ps1')
if ($Isolated) { $isolatedRoot = Assert-IsolatedInstallTest $InstallDirectory $AddinsDirectory }
elseif (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before uninstalling. Preserve unsaved models.' }
$registration = Get-FoundationRegistration $AddinsDirectory
if (-not $InstallDirectory) { $InstallDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\foundation' }
$install = Assert-NativeHostPath $InstallDirectory
$folder = $null; $old = $null; $backup = $null
if (Test-Path -LiteralPath $registration) {
    $folder = Read-FoundationRegistration $registration
    $null = Assert-FoundationPackage $folder
    if ((Split-Path $folder -Parent) -ne $install -or (Split-Path $folder -Leaf) -notmatch '^[a-f0-9]{64}$') { throw 'Registration belongs to a different installation. Preserve it.' }
    if ((Get-FileHash -LiteralPath (Join-Path $folder 'manifest.json')).Hash.ToLowerInvariant() -cne (Split-Path $folder -Leaf)) { throw 'Registered immutable bundle identity differs. Preserve it.' }
    $old = [IO.File]::ReadAllText($registration)
}
$commandPlan = New-CommandPlan $install $null $null $isolatedRoot $folder
if (-not $old -and -not $commandPlan.beforeState) { Write-Output 'Foundation is not registered.'; return }
if ($CheckOnly) { @{registration=$registration;preserved_bundle=$folder;global_command_removed=($null -ne $commandPlan.beforeState)} | ConvertTo-Json; return }
if ($old) {
    $backup = Join-Path $install ('uninstalled-'+[Guid]::NewGuid().ToString('N')+'.addin')
    [IO.File]::WriteAllText($backup,$old)
    if ([IO.File]::ReadAllText($backup) -cne $old) { throw 'Uninstall backup failed.' }
}
if (-not $Isolated -and (Get-Process Revit -ErrorAction SilentlyContinue)) { throw 'Revit opened during uninstall. Close it normally, preserving unsaved work, then retry.' }
$removed = $false
try {
    if ($old) {
        if ([IO.File]::ReadAllText($registration) -cne $old) { throw 'Registration changed during uninstall. Preserve it.' }
        Remove-Item -LiteralPath $registration
        $removed = $true
    }
    Apply-CommandPlan $commandPlan
} catch {
    try { Undo-CommandPlan $commandPlan } catch { Write-Warning ('Global command rollback failed: '+$_.Exception.Message) }
    if ($removed -and -not (Test-Path -LiteralPath $registration)) { [IO.File]::WriteAllText($registration,$old) }
    throw
}
@{unregistered=$registration;recoverable_registration=$backup;preserved_bundle=$folder;global_command_removed=($null -ne $commandPlan.beforeState)} | ConvertTo-Json
