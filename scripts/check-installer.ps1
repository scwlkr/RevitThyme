param([Parameter(Mandatory)][string]$PackageDirectory, [string]$PreviousPackageDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path (Join-Path $root 'artifacts\foundation\install-test') ([Guid]::NewGuid().ToString())
$installed = Join-Path $run 'installed'; $addins = Join-Path $run 'addins'
New-Item -ItemType Directory -Path $installed,$addins -Force | Out-Null
$revit = Join-Path $run 'revit\2027'
New-Item -ItemType Directory -Path $revit -Force | Out-Null
foreach ($file in @('Revit.exe','RevitAPI.dll')) { [IO.File]::WriteAllText((Join-Path $revit $file),'isolated discovery fixture') }
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (-not $Passed) { throw "Install test failed: $Name" } }
$preflight = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated -CheckOnly | ConvertFrom-Json
Check 'preflight detects only supported Revit without installing' ($preflight.detected_revit[0].year -eq '2027' -and -not (Test-Path $preflight.installed) -and -not (Test-Path $preflight.registration))
if ($PreviousPackageDirectory) {
    $previous = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PreviousPackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
    $oldRegistration = (Get-FileHash (Join-Path $addins 'RevitThyme.Foundation.addin')).Hash
}
$first = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
if ($PreviousPackageDirectory) {
    Check 'Foundation upgrade retains original bundle' ((Test-Path $previous.installed) -and $previous.installed -ne $first.installed)
    Check 'Foundation upgrade preserves restorable registration' ((Get-FileHash $first.previous_registration_backup).Hash -eq $oldRegistration)
}
$registration = Join-Path $addins 'RevitThyme.Foundation.addin'
Check 'registers the exact native assembly' (([xml](Get-Content $registration -Raw)).RevitAddIns.AddIn.Assembly -eq (Join-Path $first.installed 'RevitThyme.Addin.dll'))
$packageManifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'manifest.json') -Raw | ConvertFrom-Json
if ($packageManifest.version -in @('0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1','0.10.0-timberfold.2','0.10.0-timberfold.3')) {
    $runtimeVersion = & (Join-Path $first.installed 'codex\codex.exe') --version
    Check 'installed official Codex runtime launches at pinned version' ($LASTEXITCODE -eq 0 -and $runtimeVersion -eq 'codex-cli 0.162.1')
    Check 'Agent client and ordinary bundled skills installed' ((Test-Path (Join-Path $first.installed 'RevitThyme.Codex.dll')) -and (Test-Path (Join-Path $first.installed 'skills\edit-revit\SKILL.md')))
    $agentDll = Join-Path $first.installed 'RevitThyme.Codex.dll'; $agentBytes = [IO.File]::ReadAllBytes($agentDll)
    try {
        [IO.File]::WriteAllBytes($agentDll,[byte[]](1,2,3)); $refused = $false
        try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | Out-Null }
        catch { $refused = $_.Exception.Message -like '*modified*' }
        Check 'modified Agent client rejected with registration preserved' ($refused -and (Test-Path $registration))
    } finally { [IO.File]::WriteAllBytes($agentDll,$agentBytes) }
}
if ($first.mcp) {
    $version = & $first.mcp --version
    $expectedVersion = (Get-Content -LiteralPath (Join-Path $PackageDirectory 'manifest.json') -Raw | ConvertFrom-Json).version
    Check 'installed MCP executable launches' ($LASTEXITCODE -eq 0 -and $version -eq $expectedVersion)
    $sdk = Join-Path $first.installed 'mcp\ModelContextProtocol.dll'
    $sdkBytes = [IO.File]::ReadAllBytes($sdk)
    try {
        [IO.File]::WriteAllBytes($sdk,[byte[]](1,2,3))
        $rejected = $false
        try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | Out-Null }
        catch { $rejected = $_.Exception.Message -like '*modified*' }
        Check 'modified MCP dependency refused' ($rejected -and (Test-Path $registration))
    } finally { [IO.File]::WriteAllBytes($sdk,$sdkBytes) }
}
$before = (Get-FileHash $registration).Hash
$second = & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
Check 'idempotent immutable install' ($first.installed -eq $second.installed -and (Get-FileHash $registration).Hash -eq $before)
Check 'owned registration backup' (Test-Path -LiteralPath $second.previous_registration_backup)
$receipt = Join-Path $run 'receipt.json'
& (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated -ReceiptPath $receipt | Out-Null
Check 'receipt and registration identify same bundle' ((Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json).installed -eq $first.installed)
$receiptBytes = [IO.File]::ReadAllText($receipt)
(Get-Item -LiteralPath $receipt).IsReadOnly = $true
try {
    $rejected = $false
    try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated -ReceiptPath $receipt | Out-Null }
    catch { $rejected = $_.Exception.Message -like '*receipt is read-only*' }
    Check 'read-only receipt preserves registration and receipt' ($rejected -and (Get-FileHash $registration).Hash -eq $before -and [IO.File]::ReadAllText($receipt) -eq $receiptBytes)
} finally { (Get-Item -LiteralPath $receipt).IsReadOnly = $false }
$rejected = $false
try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated -ReceiptPath (Join-Path $run 'absent\receipt.json') | Out-Null }
catch { $rejected = $true }
Check 'receipt persistence failure restores prior registration' ($rejected -and (Get-FileHash $registration).Hash -eq $before -and [IO.File]::ReadAllText($receipt) -eq $receiptBytes)
$wrongRoot = Join-Path $run 'different'
$rejected = $false
try { & (Join-Path $root 'installer\uninstall.ps1') -InstallDirectory $wrongRoot -AddinsDirectory $addins -CheckOnly | Out-Null }
catch { $rejected = $true }
Check 'different installation cannot unregister current bundle' ($rejected -and (Test-Path $registration))
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
Remove-Item -LiteralPath $registration
Remove-Item -LiteralPath (Join-Path $revit 'Revit.exe')
$rejected = $false
try { & (Join-Path $root 'installer\install.ps1') -PackageDirectory $PackageDirectory -InstallDirectory $installed -AddinsDirectory $addins -Isolated | Out-Null }
catch { $rejected = $_.Exception.Message -like '*No supported Revit*' }
Check 'unsupported or absent Revit cannot register' ($rejected -and -not (Test-Path $registration))
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output "Passed $($checks.Count) installer checks in $run; native registrations untouched."
