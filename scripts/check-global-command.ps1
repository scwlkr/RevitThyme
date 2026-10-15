param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'installer\common.ps1')
. (Join-Path $root 'installer\global-command.ps1')
$run = Join-Path (Join-Path $root 'artifacts\foundation\install-test') ([Guid]::NewGuid().ToString())
$installed=Join-Path $run 'installed'; $addins=Join-Path $run 'addins'
New-Item -ItemType Directory -Path $installed,$addins,(Join-Path $run 'revit\2027') -Force | Out-Null
$null = Assert-IsolatedInstallTest $installed $addins
foreach ($file in @('Revit.exe','RevitAPI.dll')) { [IO.File]::WriteAllText((Join-Path $run ('revit\2027\'+$file)),'isolated discovery fixture') }
$realPath = [Environment]::GetEnvironmentVariable('Path','User')
$realPathSnapshot=Get-CommandPathSnapshot
$pathFile=Join-Path $run 'user-path.txt'; $stateFile=Join-Path $installed 'command.json'
$baseline='C:\Other Tools;%USERPROFILE%\.dotnet\tools;%LOCALAPPDATA%\Keep Me'
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (-not $Passed) { throw "Global command check failed: $Name" } }
function Install-Package([string]$Package,[switch]$CheckOnly,[string]$Receipt) {
    & (Join-Path $root 'installer\install.ps1') -PackageDirectory $Package -InstallDirectory $installed -AddinsDirectory $addins -Isolated -CheckOnly:$CheckOnly -ReceiptPath $Receipt | ConvertFrom-Json
}
function Uninstall-Package {
    & (Join-Path $root 'installer\uninstall.ps1') -InstallDirectory $installed -AddinsDirectory $addins -Isolated | ConvertFrom-Json
}
try {
    # Only this fresh disposable key is written; HKCU\Environment is read-only in these checks.
    $registryFixture='Software\RevitThyme\InstallTests\'+[Guid]::NewGuid().ToString()
    if ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryFixture)) { throw 'Registry fixture already exists.' }
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryFixture)
    try {
        $key.SetValue('Unrelated','preserved')
        $absent=Read-CommandRegistryPath $key
        Check 'registry adapter preserves absence of PATH' (-not $absent.exists)
        $raw='%USERPROFILE%\.dotnet\tools;%LOCALAPPDATA%\Untouched'
        Write-CommandRegistryPath $key @{value=$raw;kind='ExpandString';exists=$true}
        $snapshot=Read-CommandRegistryPath $key
        Check 'registry adapter reads REG_EXPAND_SZ without expanding unrelated entries' ($snapshot.value -ceq $raw -and $snapshot.kind -ceq 'ExpandString')
        Write-CommandRegistryPath $key @{value=('C:\Bundle\cli;'+$snapshot.value);kind=$snapshot.kind;exists=$true}
        $changed=Read-CommandRegistryPath $key
        Check 'registry adapter retains REG_EXPAND_SZ while prepending command' ($changed.value -ceq ('C:\Bundle\cli;'+$raw) -and $changed.kind -ceq 'ExpandString')
        Write-CommandRegistryPath $key $snapshot
        Check 'registry adapter rolls raw value and kind back exactly' (Test-CommandPathSnapshot (Read-CommandRegistryPath $key) $snapshot)
        Write-CommandRegistryPath $key @{value=$raw;kind='String';exists=$true}
        $plain=Read-CommandRegistryPath $key
        Check 'registry adapter preserves a preexisting REG_SZ kind' ($plain.value -ceq $raw -and $plain.kind -ceq 'String')
        Write-CommandRegistryPath $key $absent
        Check 'registry rollback restores absent value and preserves unrelated values' (-not (Read-CommandRegistryPath $key).exists -and $key.GetValue('Unrelated') -ceq 'preserved')
        $key.DeleteValue('Unrelated')
    } finally {
        $key.Dispose()
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryFixture,$false)
    }
    $manifest=Assert-FoundationPackage $PackageDirectory
    if (-not $manifest.files.PSObject.Properties['cli/rthyme.exe']) { throw 'This check requires a package with the rthyme alias.' }
    $preflight=Install-Package $PackageDirectory -CheckOnly
    Check 'preflight creates no command state or simulated PATH' (-not (Test-Path -LiteralPath $pathFile) -and -not (Test-Path -LiteralPath $stateFile) -and -not (Test-Path -LiteralPath $preflight.installed))
    [IO.File]::WriteAllText($pathFile,$baseline)
    $first=Install-Package $PackageDirectory
    $firstDirectory=Split-Path $first.global_command -Parent
    $firstPath=[IO.File]::ReadAllText($pathFile); $firstState=[IO.File]::ReadAllText($stateFile)
    Check 'install prepends only its immutable CLI directory' ($firstPath -ceq ($firstDirectory+';'+$baseline) -and ($firstState|ConvertFrom-Json).path_added)
    Check 'alias is the exact packaged apphost' ((Get-FileHash -LiteralPath $first.global_command).Hash -ceq (Get-FileHash -LiteralPath $first.cli).Hash)
    $version=& $first.global_command --version
    Check 'installer alias launches without a launcher or SDK command' ($LASTEXITCODE -eq 0 -and ($version -join "`n") -match 'Installed RevitThyme')
    $second=Install-Package $PackageDirectory
    Check 'same-version installation is idempotent' ($first.installed -ceq $second.installed -and [IO.File]::ReadAllText($pathFile) -ceq $firstPath -and [IO.File]::ReadAllText($stateFile) -ceq $firstState)
    [IO.File]::AppendAllText($pathFile,(';'+$firstDirectory))
    $null=Install-Package $PackageDirectory
    Check 'reinstall preserves a duplicate PATH entry added by the user' ([IO.File]::ReadAllText($pathFile) -ceq ($firstPath+';'+$firstDirectory))
    [IO.File]::WriteAllText($pathFile,$firstPath)
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'))
    $start.Arguments='-NoProfile -NonInteractive -Command "(Get-Command rthyme -CommandType Application | Select-Object -First 1).Source"'
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.WorkingDirectory=$run; $start.EnvironmentVariables['PATH']=$firstPath+';'+$env:PATH
    $process=[Diagnostics.Process]::Start($start)
    try { $resolved=$process.StandardOutput.ReadToEnd(); $errorText=$process.StandardError.ReadToEnd(); $process.WaitForExit(); Check 'fresh shell resolves installer rthyme before existing developer tool' ($process.ExitCode -eq 0 -and $resolved.Trim() -ceq $first.global_command) } finally { $process.Dispose() }
    # A different manifest identity exercises immutable upgrades with identical signed-off files.
    $upgradePackage=Join-Path $run 'next-package'
    Copy-Item -LiteralPath $PackageDirectory -Destination $upgradePackage -Recurse
    $nextManifest=Join-Path $upgradePackage 'manifest.json'
    [IO.File]::AppendAllText($nextManifest,[Environment]::NewLine)
    $registration=$first.registration; $registrationBefore=[IO.File]::ReadAllText($registration)
    $oldExe=[IO.File]::Open($first.global_command,'Open','Read','Read')
    $oldDll=[IO.File]::Open((Join-Path $first.installed 'cli\revitthyme.dll'),'Open','Read','Read')
    $oldExeHash=(Get-FileHash -LiteralPath $first.global_command).Hash; $oldDllHash=(Get-FileHash -LiteralPath (Join-Path $first.installed 'cli\revitthyme.dll')).Hash
    try {
        $rejected=$false
        try { Install-Package $upgradePackage -Receipt (Join-Path $run 'missing\receipt.json') | Out-Null } catch { $rejected=$true }
        Check 'receipt failure rolls registration PATH and command state back together' ($rejected -and [IO.File]::ReadAllText($registration) -ceq $registrationBefore -and [IO.File]::ReadAllText($pathFile) -ceq $firstPath -and [IO.File]::ReadAllText($stateFile) -ceq $firstState)
        $receiptPath=Join-Path $run 'receipt.json'
        $upgraded=Install-Package $upgradePackage -Receipt $receiptPath
        $upgradedDirectory=Split-Path $upgraded.global_command -Parent
        Check 'upgrade replaces only installer-owned PATH entry while old files are open' ([IO.File]::ReadAllText($pathFile) -ceq ($upgradedDirectory+';'+$baseline) -and $upgraded.installed -cne $first.installed)
        Check 'old immutable command bytes remain available and unchanged' ((Get-FileHash -LiteralPath $first.global_command).Hash -ceq $oldExeHash -and (Get-FileHash -LiteralPath (Join-Path $first.installed 'cli\revitthyme.dll')).Hash -ceq $oldDllHash)
        Check 'receipt records the installed global command' ((Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json).global_command -ceq $upgraded.global_command)
    } finally { $oldExe.Dispose(); $oldDll.Dispose() }
    $upgradedPath=[IO.File]::ReadAllText($pathFile); $upgradedState=[IO.File]::ReadAllText($stateFile)
    $plan=New-CommandPlan $installed $first.installed $manifest $run $upgraded.installed
    Apply-CommandPlan $plan
    [IO.File]::AppendAllText($pathFile,';C:\Concurrent Addition')
    Undo-CommandPlan $plan
    Check 'rollback preserves a concurrent unrelated PATH addition' ([IO.File]::ReadAllText($pathFile) -ceq ($upgradedPath+';C:\Concurrent Addition') -and [IO.File]::ReadAllText($stateFile) -ceq $upgradedState)
    [IO.File]::WriteAllText($registration,$registrationBefore)
    $recovered=Install-Package $upgradePackage
    Check 'reinstall recovers verified command ownership after an older installer restored registration' ($recovered.installed -ceq $upgraded.installed -and (Read-FoundationRegistration $registration) -ceq $upgraded.installed -and [IO.File]::ReadAllText($pathFile) -ceq ($upgradedPath+';C:\Concurrent Addition') -and [IO.File]::ReadAllText($stateFile) -ceq $upgradedState -and (Test-Path -LiteralPath $first.global_command))
    $otherRoot=Join-Path $run 'alt'
    New-Item -ItemType Directory -Path $otherRoot | Out-Null
    $otherBundle=Join-Path $otherRoot (Split-Path $upgraded.installed -Leaf)
    Copy-Item -LiteralPath $upgraded.installed -Destination $otherBundle -Recurse
    $null=Assert-FoundationPackage $otherBundle
    $registeredBefore=[IO.File]::ReadAllText($registration)
    $crossRootRegistration=$registeredBefore.Replace([Security.SecurityElement]::Escape($upgraded.installed),[Security.SecurityElement]::Escape($otherBundle))
    [IO.File]::WriteAllText($registration,$crossRootRegistration)
    $refused=$false
    try { Install-Package $upgradePackage | Out-Null } catch { $refused=$_.Exception.Message -like '*different installation*' }
    Check 'cross-root owned registration is preserved without PATH or ownership mutation' ($refused -and [IO.File]::ReadAllText($registration) -ceq $crossRootRegistration -and [IO.File]::ReadAllText($pathFile) -ceq ($upgradedPath+';C:\Concurrent Addition') -and [IO.File]::ReadAllText($stateFile) -ceq $upgradedState)
    [IO.File]::WriteAllText($registration,$registeredBefore)
    $foreign='{"kind":"another-tool","schema":1}'
    [IO.File]::WriteAllText($stateFile,$foreign)
    $beforePath=[IO.File]::ReadAllText($pathFile); $beforeRegistration=[IO.File]::ReadAllText($registration)
    $refused=0
    foreach ($action in @('check','install','uninstall')) {
        try {
            if ($action -eq 'uninstall') { Uninstall-Package | Out-Null }
            else { Install-Package $upgradePackage -CheckOnly:($action -eq 'check') | Out-Null }
        } catch { if ($_.Exception.Message -like '*Global command state*') { $refused++ } }
    }
    Check 'foreign command state refuses preflight install and uninstall without mutation' ($refused -eq 3 -and [IO.File]::ReadAllText($stateFile) -ceq $foreign -and [IO.File]::ReadAllText($pathFile) -ceq $beforePath -and [IO.File]::ReadAllText($registration) -ceq $beforeRegistration)
    [IO.File]::WriteAllText($stateFile,$upgradedState)
    $uninstalled=Uninstall-Package
    Check 'uninstall removes only owned PATH entry and state preserving immutable bundles' ([IO.File]::ReadAllText($pathFile) -ceq ($baseline+';C:\Concurrent Addition') -and -not (Test-Path -LiteralPath $stateFile) -and (Test-Path -LiteralPath $first.global_command) -and (Test-Path -LiteralPath $upgraded.global_command) -and -not (Test-Path -LiteralPath $registration))
    $preexisting='C:\Before;'+$upgradedDirectory+';'+$baseline
    [IO.File]::WriteAllText($pathFile,$preexisting)
    $existing=Install-Package $upgradePackage
    Check 'preexisting target PATH entry is recorded as user-owned' (-not (Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json).path_added)
    $null=Uninstall-Package
    Check 'uninstall preserves preexisting target PATH entry and developer tool' ([IO.File]::ReadAllText($pathFile) -ceq ($upgradedDirectory+';C:\Before;'+$baseline) -and -not (Test-Path -LiteralPath $stateFile))
    Check 'real user PATH raw value kind and existence were never changed' ([Environment]::GetEnvironmentVariable('Path','User') -ceq $realPath -and (Test-CommandPathSnapshot (Get-CommandPathSnapshot) $realPathSnapshot))
    $checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'global-command-result.json') -Encoding utf8
    Write-Output "Passed $($checks.Count) installer global command checks in $run; real user PATH untouched."
} finally {
    if ([Environment]::GetEnvironmentVariable('Path','User') -cne $realPath -or -not (Test-CommandPathSnapshot (Get-CommandPathSnapshot) $realPathSnapshot)) { throw 'Real user PATH changed during isolated checks; preserve and inspect the concurrent edit.' }
}
