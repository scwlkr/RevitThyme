param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$SetupExecutable,
    [string]$ProductionSetupExecutable
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ((Split-Path $SetupExecutable -Leaf) -notmatch '-test\.exe$') { throw 'Use a separately compiled isolated setup executable.' }
$run = Join-Path $root ('artifacts\foundation\install-test\'+[Guid]::NewGuid())
$installed = Join-Path $run 'installed'; $addins = Join-Path $run 'addins'
$setup = Join-Path $run 'setup'; $registration = Join-Path $addins 'RevitThyme.Foundation.addin'
New-Item -ItemType Directory -Path $installed,$addins,(Join-Path $run 'revit\2027') -Force | Out-Null
foreach ($file in @('Revit.exe','RevitAPI.dll')) { [IO.File]::WriteAllText((Join-Path $run ('revit\2027\'+$file)),'isolated fixture') }
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}); if (-not $Passed) { throw "Setup check failed: $Name ($run)" } }
function Setup([string]$Name,[string]$Extra) {
    $log = Join-Path $run ($Name+'.log')
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/TESTROOT="'+$run+'"'),('/LOG="'+$log+'"'))
    if ($Extra) { $arguments += $Extra }
    $process = Start-Process -FilePath $SetupExecutable -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(60000)) { throw "Setup exceeded one minute; inspect owned process $($process.Id) and $log before retrying." }
    $process.Refresh(); return $process.ExitCode
}
function Uninstall([string]$Name) {
    $process = Start-Process -FilePath (Join-Path $setup 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $run ($Name+'.log'))+'"')) -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(60000)) { throw "Uninstall exceeded one minute; inspect owned process $($process.Id)." }
    $process.Refresh(); return $process.ExitCode
}
$native = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.Foundation.addin'
$nativeHash = if(Test-Path -LiteralPath $native){(Get-FileHash -LiteralPath $native).Hash}else{''}
$processes = @(Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
Check 'setup executable installs using Windows PowerShell' ((Setup 'first-install') -eq 0 -and (Test-Path $registration))
$receipt = Get-Content -LiteralPath (Join-Path $setup 'receipt.json') -Raw | ConvertFrom-Json
$packageHash = (Get-FileHash -LiteralPath (Join-Path $PackageDirectory 'manifest.json')).Hash.ToLowerInvariant()
Check 'setup installs exact package with add-in CLI MCP' ((Split-Path $receipt.installed -Leaf) -eq $packageHash -and (Test-Path $receipt.cli) -and (Test-Path $receipt.mcp))
$cliHelp = & $receipt.cli --help
Check 'installed CLI executes' ($LASTEXITCODE -eq 0 -and ($cliHelp -join '') -match 'revitthyme sessions')
$mcpVersion = & $receipt.mcp --version
Check 'installed MCP executes' ($LASTEXITCODE -eq 0 -and $mcpVersion -eq $receipt.version)
$registrationHash = (Get-FileHash $registration).Hash
Check 'setup reinstall preserves registration' ((Setup 'repeat-install') -eq 0 -and (Get-FileHash $registration).Hash -eq $registrationHash)
$foreign = '<RevitAddIns><AddIn Type="Application"><AddInId>unknown</AddInId></AddIn></RevitAddIns>'
$owned = [IO.File]::ReadAllText($registration)
[IO.File]::WriteAllText($registration,$foreign)
Check 'setup refuses foreign registration and retains it' ((Setup 'foreign-registration') -ne 0 -and [IO.File]::ReadAllText($registration) -eq $foreign)
Check 'uninstaller refuses foreign registration and preserves recovery files' ((Uninstall 'foreign-uninstall') -ne 0 -and (Test-Path (Join-Path $setup 'unins000.exe')) -and [IO.File]::ReadAllText($registration) -eq $foreign)
[IO.File]::WriteAllText($registration,$owned)
Check 'uninstaller unregisters only fixture and preserves bundle' ((Uninstall 'uninstall') -eq 0 -and -not (Test-Path $registration) -and (Test-Path $receipt.installed))
Check 'uninstall creates recoverable registration' (@(Get-ChildItem -LiteralPath $installed -Filter 'uninstalled-*.addin').Count -eq 1)
Remove-Item -LiteralPath (Join-Path $run 'revit\2027\Revit.exe')
Check 'setup refuses absent supported Revit' ((Setup 'no-revit') -ne 0 -and -not (Test-Path $registration))
[IO.File]::WriteAllText((Join-Path $run 'revit\2027\Revit.exe'),'isolated fixture')
Check 'child failure after preflight returns failure and rolls back setup files' ((Setup 'child-failure' '/FAILINSTALL=1') -ne 0 -and -not (Test-Path $registration) -and -not (Test-Path (Join-Path $setup 'unins000.exe')))
if ($ProductionSetupExecutable) {
    $process = Start-Process -FilePath $ProductionSetupExecutable -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/TESTROOT="'+$run+'"')) -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(60000)) { throw 'Production setup test parameter refusal timed out.' }
    $process.Refresh()
    Check 'production setup cannot enter isolated test mode' ($process.ExitCode -ne 0)
    if ($processes.Count) {
        $process = Start-Process -FilePath $ProductionSetupExecutable -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG="'+(Join-Path $run 'production-revit-open.log')+'"')) -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(60000)) { throw 'Production Revit-open refusal timed out.' }
        $process.Refresh()
        Check 'production setup refuses changes while Revit is running' ($process.ExitCode -ne 0)
    }
}
$afterHash = if(Test-Path -LiteralPath $native){(Get-FileHash -LiteralPath $native).Hash}else{''}
Check 'native registration and Revit processes preserved' ($afterHash -eq $nativeHash -and (@(Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -join ',') -eq ($processes -join ','))
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'setup-result.json') -Encoding utf8
Write-Output "Passed $($checks.Count) setup executable checks: $run"
