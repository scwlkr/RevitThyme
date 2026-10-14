param([string]$AddinsDirectory, [string]$InstallDirectory, [switch]$Isolated)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Isolated) { $null = Assert-IsolatedInstallTest $InstallDirectory $AddinsDirectory }
elseif (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before uninstalling. Preserve unsaved models.' }
$registration = Get-FoundationRegistration $AddinsDirectory
if (-not (Test-Path -LiteralPath $registration)) { Write-Output 'Foundation is not registered.'; return }
$folder = Read-FoundationRegistration $registration
$null = Assert-FoundationPackage $folder
$backup = Join-Path (Split-Path $folder -Parent) ('uninstalled-'+[Guid]::NewGuid().ToString('N')+'.addin')
Copy-Item -LiteralPath $registration -Destination $backup
if ((Get-FileHash -LiteralPath $registration).Hash -ne (Get-FileHash -LiteralPath $backup).Hash) { throw 'Uninstall backup failed.' }
Remove-Item -LiteralPath $registration
@{unregistered=$registration;recoverable_registration=$backup;preserved_bundle=$folder} | ConvertTo-Json
