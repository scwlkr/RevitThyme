param([string]$AddinsDirectory, [string]$InstallDirectory, [switch]$Isolated, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Isolated) { $null = Assert-IsolatedInstallTest $InstallDirectory $AddinsDirectory }
elseif (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit normally before uninstalling. Preserve unsaved models.' }
$registration = Get-FoundationRegistration $AddinsDirectory
if (-not (Test-Path -LiteralPath $registration)) { Write-Output 'Foundation is not registered.'; return }
$folder = Read-FoundationRegistration $registration
$null = Assert-FoundationPackage $folder
if (-not $InstallDirectory) { $InstallDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\foundation' }
$install = Assert-NativeHostPath $InstallDirectory
if ((Split-Path $folder -Parent) -ne $install -or (Split-Path $folder -Leaf) -notmatch '^[a-f0-9]{64}$') { throw 'Registration belongs to a different installation. Preserve it.' }
if ($CheckOnly) { @{registration=$registration;preserved_bundle=$folder} | ConvertTo-Json; return }
$backup = Join-Path (Split-Path $folder -Parent) ('uninstalled-'+[Guid]::NewGuid().ToString('N')+'.addin')
Copy-Item -LiteralPath $registration -Destination $backup
if ((Get-FileHash -LiteralPath $registration).Hash -ne (Get-FileHash -LiteralPath $backup).Hash) { throw 'Uninstall backup failed.' }
if (-not $Isolated -and (Get-Process Revit -ErrorAction SilentlyContinue)) { throw 'Revit opened during uninstall. Close it normally, preserving unsaved work, then retry.' }
if ((Get-FileHash -LiteralPath $registration).Hash -ne (Get-FileHash -LiteralPath $backup).Hash) { throw 'Registration changed during uninstall. Preserve it.' }
Remove-Item -LiteralPath $registration
@{unregistered=$registration;recoverable_registration=$backup;preserved_bundle=$folder} | ConvertTo-Json
