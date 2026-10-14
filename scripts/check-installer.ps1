param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path (Join-Path $root 'artifacts\foundation\install-test') ([Guid]::NewGuid().ToString())
$installed = Join-Path $run 'installed'; $addins = Join-Path $run 'addins'
New-Item -ItemType Directory -Path $installed,$addins -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (-not $Passed) { throw "Install test failed: $Name" } }
$first = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
$registration = Join-Path $addins 'RevitThyme.Foundation.addin'
Check 'registers the exact native assembly' (([xml](Get-Content $registration -Raw)).RevitAddIns.AddIn.Assembly -eq (Join-Path $first.installed 'RevitThyme.Addin.dll'))
$before = (Get-FileHash $registration).Hash
$second = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
Check 'idempotent immutable install' ($first.installed -eq $second.installed -and (Get-FileHash $registration).Hash -eq $before)
Check 'owned registration backup' (Test-Path -LiteralPath $second.previous_registration_backup)
$uninstall = & (Join-Path $root 'installer\uninstall.ps1') -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
Check 'unregister only owned file' (-not (Test-Path $registration) -and (Test-Path $uninstall.preserved_bundle))
Check 'recoverable registration' ((Get-FileHash $uninstall.recoverable_registration).Hash -eq $before)
Copy-Item -LiteralPath $uninstall.recoverable_registration -Destination $registration
$assembly = Join-Path $first.installed 'RevitThyme.Addin.dll'
$original = [IO.File]::ReadAllBytes($assembly)
try {
    [IO.File]::WriteAllBytes($assembly, [byte[]](1,2,3))
    $rejected = $false
    try { & (Join-Path $root 'installer\uninstall.ps1') -InstallDirectory $installed -AddinsDirectory $addins -Isolated | Out-Null }
    catch { $rejected = $_.Exception.Message -like '*modified*' }
    Check 'modified installed bundle refused' ($rejected -and (Test-Path $registration))
} finally { [IO.File]::WriteAllBytes($assembly,$original) }
[IO.File]::WriteAllText($registration,'<RevitAddIns><AddIn Type="Application"><AddInId>unknown</AddInId></AddIn></RevitAddIns>')
$rejected = $false
try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | Out-Null }
catch { $rejected = $_.Exception.Message -like '*not owned*' }
Check 'unknown registration preserved' ($rejected -and [IO.File]::ReadAllText($registration).Contains('unknown'))
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output "Passed $($checks.Count) installer checks in $run; native registrations untouched."
