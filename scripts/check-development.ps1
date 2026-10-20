param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-common.ps1')

# Offline safety checks: these mocks exist only in this check script's scope.
# No Revit, installed registration, certificate store, or native CLI is addressed.
$checkFolder = Join-Path (Join-Path $script:DevRuns 'checks') ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $checkFolder -Force | Out-Null
$script:Checks = [Collections.Generic.List[object]]::new()
$script:TestRuns = [Collections.Generic.List[string]]::new()
$script:CliCalls = [Collections.Generic.List[object]]::new()
$script:ProcessCalls = [Collections.Generic.List[int]]::new()
$script:StartCalls = 0
$script:CloseCalls = 0
$script:MockExpectedPid = 2147000123
$script:MockProcess = $null
$script:MockCli = $null
$script:MockInstalled = $null

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        $null = & $Body
        $script:Checks.Add(@{name=$Name;passed=$true})
        Write-Output "PASS $Name"
    } catch {
        $script:Checks.Add(@{name=$Name;passed=$false;error=$_.Exception.Message})
        Write-Output "FAIL $Name : $($_.Exception.Message)"
    }
}
function Assert-Refused([scriptblock]$Body, [string]$Pattern) {
    $failure = $null
    try { $null = & $Body } catch { $failure = $_.Exception.Message }
    Assert-Check ([bool]$failure) 'Expected an explicit refusal, but the action succeeded.'
    Assert-Check ($failure -match $Pattern) "Unexpected refusal: $failure"
}
function Get-Process {
    [CmdletBinding()]
    param([int]$Id)
    $script:ProcessCalls.Add($Id)
    if ($Id -eq $script:MockExpectedPid) { return $script:MockProcess }
    return $null
}
function Start-Process {
    $script:StartCalls++
    throw 'Offline check attempted process launch.'
}
function Stop-Process { throw 'Offline check attempted forced process termination.' }
function Get-DevInstalled { return $script:MockInstalled }
function Assert-DevSigned { throw 'Offline check unexpectedly reached signing verification.' }
function Invoke-DevCli($Launch, [string[]]$Arguments, [string]$OutputPath) {
    $script:CliCalls.Add(@{arguments=@($Arguments);output=$OutputPath})
    if (-not $script:MockCli) { throw 'Offline check has no CLI response configured.' }
    $result = & $script:MockCli $Launch $Arguments $OutputPath
    if ($result) { Write-DevJson $OutputPath $result.value }
    return $result
}
function Get-ProductionAction([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-Check ($errors.Count -eq 0) "Production script does not parse: $Path"
    # Preserve the production param block and every action statement. Exclude only
    # its helper import, which would overwrite the scoped dependency mocks above.
    $imports = @($ast.EndBlock.Statements | Where-Object { $_.Extent.Text -eq ". (Join-Path `$PSScriptRoot 'dev-common.ps1')" })
    Assert-Check ($imports.Count -eq 1) 'Production action helper import changed; update this check deliberately.'
    $statements = @($ast.EndBlock.Statements | Where-Object { $_ -ne $imports[0] } | ForEach-Object { $_.Extent.Text })
    return [scriptblock]::Create($ast.ParamBlock.Extent.Text + "`n" + ($statements -join "`n"))
}
$executeAction = Get-ProductionAction (Join-Path $PSScriptRoot 'dev-execute.ps1')
$sessionAction = Get-ProductionAction (Join-Path $PSScriptRoot 'dev-session.ps1')

function New-CheckRun {
    $folder = Join-Path $script:DevRuns ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $folder | Out-Null
    $script:TestRuns.Add($folder)
    foreach ($name in @('sessions','results','installed','staged')) { New-Item -ItemType Directory -Path (Join-Path $folder $name) | Out-Null }
    $manifestText = '{"offline_safety_check":true}'
    $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifestText)))
    $bundle = Join-Path (Join-Path $folder 'installed') $manifestHash.ToLowerInvariant()
    New-Item -ItemType Directory -Path $bundle | Out-Null
    [IO.File]::WriteAllText((Join-Path $bundle 'manifest.json'), $manifestText)
    $fixture = Join-Path $folder 'Fixture.rvt'
    [IO.File]::WriteAllText($fixture, 'Offline check placeholder. This is not a Revit model and must never be opened.')
    $plan = [pscustomobject]@{schema=1;folder=$folder;fixture=$fixture;bundle=(Join-Path $folder 'staged');
        manifest_hash=$manifestHash;fixture_sha256=(Get-FileHash -LiteralPath $fixture).Hash;signing_config=(Join-Path $folder 'unused-signing.json');signer='OFFLINE'}
    $start = [DateTime]::SpecifyKind([DateTime]'2026-01-01T00:00:00', [DateTimeKind]::Utc)
    $launch = [pscustomobject]@{folder=$folder;fixture=$fixture;bundle=$bundle;manifest_hash=$manifestHash;pid=$script:MockExpectedPid;
        start=$start.Ticks.ToString();revit='C:\Offline-Development-Checks\Revit.exe';session_id=[Guid]::NewGuid().ToString();document_id=[Guid]::NewGuid().ToString()}
    $script:MockProcess = [pscustomobject]@{Id=$launch.pid;ProcessName='Revit';StartTime=$start;Path=$launch.revit}
    $script:MockProcess | Add-Member -MemberType ScriptMethod -Name CloseMainWindow -Value { $script:CloseCalls++; return $true }
    Write-DevJson (Join-Path $folder 'plan.json') $plan
    Write-DevJson (Join-Path $folder 'launch.json') $launch
    Write-DevJson (Join-Path $folder 'offline-check.json') @{check_folder=$checkFolder;native_model=$false}
    return @{plan=$plan;launch=$launch}
}
function New-CheckBinding($Run, [string]$Name='binding.json') {
    $binding = [pscustomobject]@{protocolVersion=10;processId=$Run.launch.pid;processStart=$Run.launch.start;
        sessionId=$Run.launch.session_id;pipeName="RevitThyme-Foundation-$($Run.launch.pid)-$($Run.launch.session_id)";revitVersion='2027'}
    $path = Join-Path (Join-Path $Run.plan.folder 'sessions') $Name
    Write-DevJson $path $binding
    return @{path=$path;value=$binding}
}
function New-CheckInspection($Run) {
    return @{exit_code=0;value=[pscustomobject]@{id=[Guid]::NewGuid().ToString();succeeded=$true;outcome='Inspected';
        snapshot=[pscustomobject]@{documentPath=$Run.plan.fixture;isWorkshared=$false;target=[pscustomobject]@{
            processId=$Run.launch.pid;processStart=$Run.launch.start;sessionId=$Run.launch.session_id;documentId=$Run.launch.document_id}}}}
}

Test-Case 'valid plan, installed content hash, process and binding are accepted' {
    $run = New-CheckRun
    $binding = New-CheckBinding $run
    $null = Get-DevPlan $run.plan.folder
    $null = Get-DevLaunch $run.plan
    Assert-Check ((Get-DevProcess $run.launch).Id -eq $run.launch.pid) 'Expected exact mocked process.'
    Assert-Check ((Get-DevBinding $run.plan $run.launch) -eq $binding.path) 'Expected exact binding path.'
}
foreach ($variant in @('relative','non-uuid','nested','outside')) {
    Test-Case "run path rejects $variant" {
        $run = New-CheckRun
        $bad = switch ($variant) {
            'relative' { 'artifacts\dev\relative' }
            'non-uuid' { Join-Path $script:DevRuns 'not-a-run' }
            'nested' { Join-Path $run.plan.folder ([Guid]::NewGuid().ToString()) }
            'outside' { Join-Path $checkFolder ([Guid]::NewGuid().ToString()) }
        }
        Assert-Refused { Get-DevPlan $bad } 'absolute|exact UUID'
    }
}
foreach ($field in @('schema','folder','fixture')) {
    Test-Case "corrupted plan rejects changed $field" {
        $run = New-CheckRun
        $run.plan.$field = if ($field -eq 'schema') { 2 } else { Join-Path $checkFolder 'unexpected' }
        Write-DevJson (Join-Path $run.launch.folder 'plan.json') $run.plan
        Assert-Refused { Get-DevPlan $run.launch.folder } 'plan identity changed'
    }
}
foreach ($field in @('folder','fixture','pid','start','manifest_hash','bundle')) {
    Test-Case "corrupted launch rejects changed $field" {
        $run = New-CheckRun
        $run.launch.$field = switch ($field) {
            'pid' { 0 }
            'start' { '' }
            'manifest_hash' { '0' * 64 }
            default { Join-Path $checkFolder 'unexpected' }
        }
        Write-DevJson (Join-Path $run.plan.folder 'launch.json') $run.launch
        Assert-Refused { Get-DevLaunch $run.plan } 'launch identity|launched bundle'
    }
}
Test-Case 'changed installed manifest is rejected' {
    $run = New-CheckRun
    [IO.File]::WriteAllText((Join-Path $run.launch.bundle 'manifest.json'), '{}')
    Assert-Refused { Get-DevLaunch $run.plan } 'manifest changed'
}
Test-Case 'wrong PID cannot discover a development binding' {
    $run = New-CheckRun; $null = New-CheckBinding $run
    $run.launch.pid--
    Assert-Refused { Get-DevBinding $run.plan $run.launch } 'has exited'
    Assert-Check ($script:ProcessCalls[-1] -eq $run.launch.pid) 'Requested PID was not pinned.'
}
foreach ($field in @('StartTime','ProcessName','Path')) {
    Test-Case "process identity rejects changed $field" {
        $run = New-CheckRun
        $script:MockProcess.$field = switch ($field) { 'StartTime' { [DateTime]::UtcNow } 'ProcessName' { 'Other' } 'Path' { 'C:\Other\Revit.exe' } }
        Assert-Refused { Get-DevProcess $run.launch } 'PID was reused or changed'
    }
}
foreach ($variant in @('missing','wrong-pid','stale-start','ambiguous','invalid-protocol','previous-protocol','invalid-pipe','changed-session')) {
    Test-Case "session discovery rejects $variant binding" {
        $run = New-CheckRun
        if ($variant -ne 'missing') {
            $binding = New-CheckBinding $run
            switch ($variant) {
                'wrong-pid' { $binding.value.processId-- }
                'stale-start' { $binding.value.processStart='1' }
                'ambiguous' { $null = New-CheckBinding $run 'second.json' }
                'invalid-protocol' { $binding.value.protocolVersion=0 }
                'previous-protocol' { $binding.value.protocolVersion=9 }
                'invalid-pipe' { $binding.value.pipeName='Wrong-Pipe' }
                'changed-session' { $run.launch.session_id=[Guid]::NewGuid().ToString() }
            }
            Write-DevJson $binding.path $binding.value
        }
        Assert-Refused { Get-DevBinding $run.plan $run.launch } 'not ready or is ambiguous|Invalid development session|session changed'
    }
}
Test-Case 'initial binding and document identity are pinned and persisted' {
    $run = New-CheckRun; $binding = New-CheckBinding $run
    $sessionId=$run.launch.session_id; $documentId=$run.launch.document_id
    $script:Inspection = New-CheckInspection $run
    $script:MockCli = { param($l,$a,$o) return $script:Inspection }
    $run.launch.session_id=''; $run.launch.document_id=''
    $null = Get-DevBinding $run.plan $run.launch
    $null = Get-DevContext $run.plan $run.launch $binding.path (Join-Path $run.plan.folder 'capture.json')
    $saved = Get-Content -LiteralPath (Join-Path $run.plan.folder 'launch.json') -Raw | ConvertFrom-Json
    Assert-Check ($saved.session_id -eq $sessionId -and $saved.document_id -eq $documentId) 'Initial native identities were not persisted.'
}
foreach ($field in @('documentPath','isWorkshared','processId','processStart','sessionId','documentId')) {
    Test-Case "native context rejects changed $field" {
        $run = New-CheckRun
        $script:Inspection = New-CheckInspection $run
        if ($field -in @('documentPath','isWorkshared')) {
            $script:Inspection.value.snapshot.$field = if ($field -eq 'isWorkshared') { $true } else { Join-Path $checkFolder 'Working-Model.rvt' }
        } else {
            $script:Inspection.value.snapshot.target.$field = switch ($field) { 'processId' { 42 } 'processStart' { '1' } default { [Guid]::NewGuid().ToString() } }
        }
        $script:MockCli = { param($l,$a,$o) return $script:Inspection }
        Assert-Refused { Get-DevContext $run.plan $run.launch 'offline-binding' (Join-Path $run.plan.folder 'capture.json') } 'does not match|fixture was reopened'
    }
}
Test-Case 'run lock prevents concurrent commands' {
    $run = New-CheckRun
    $held = Get-DevLock $run.plan
    try { Assert-Refused { Get-DevLock $run.plan } 'Another development command' } finally { $held.Dispose() }
}
Test-Case 'prepared package mismatch rejects start before signing or process launch' {
    $run = New-CheckRun
    Remove-Item -LiteralPath (Join-Path $run.plan.folder 'launch.json')
    $script:MockInstalled = @{manifest_hash=('F' * 64);bundle=$run.launch.bundle}
    $before = $script:StartCalls
    Assert-Refused { & $sessionAction -Action start -RunFolder $run.plan.folder -SigningConfigPath $run.plan.signing_config } 'Install the prepared signed base package'
    Assert-Check ($script:StartCalls -eq $before) 'A mismatched package reached process launch.'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $run.plan.folder 'launch.json'))) 'Failed package validation reserved a launch.'
}
Test-Case 'stop asks only the exact development window to close' {
    $run = New-CheckRun
    $before = $script:CloseCalls
    $result = (& $sessionAction -Action stop -RunFolder $run.plan.folder -SigningConfigPath $run.plan.signing_config) -join "`n" | ConvertFrom-Json
    Assert-Check ($result.state -eq 'close_requested' -and $result.requested -and $script:CloseCalls -eq ($before+1)) 'Expected one graceful window-close request.'
    Assert-Check (Test-Path -LiteralPath $run.plan.fixture) 'Stop deleted the fixture.'
}
Test-Case 'an outstanding submission prevents a new request before any CLI call' {
    $run = New-CheckRun
    Write-DevJson (Join-Path $run.plan.folder 'pending.json') @{request_id=[Guid]::NewGuid().ToString()}
    $before = $script:CliCalls.Count
    Assert-Refused { & $executeAction -Action query -Plan $run.plan -Launch $run.launch -Binding 'offline' -RequestId ([Guid]::Empty) } 'earlier submission needs outcome recovery'
    Assert-Check ($script:CliCalls.Count -eq $before) 'Pending refusal reached the CLI.'
}
foreach ($variant in @('pending','not_found','unconfirmed','different-completed-id','completed')) {
    Test-Case "outcome recovery handles $variant without resubmission" {
        $run = New-CheckRun
        $script:PendingId = [Guid]::NewGuid().ToString()
        Write-DevJson (Join-Path $run.plan.folder 'pending.json') @{request_id=$script:PendingId}
        $script:RecoveryVariant = $variant
        $script:MockCli = {
            param($l,$a,$o)
            Assert-Check ($a[0] -eq 'get-operation') 'Recovery attempted a new submission.'
            Assert-Check ($a[4] -eq $script:PendingId) 'Recovery did not poll the original request ID.'
            switch ($script:RecoveryVariant) {
                'completed' { return @{exit_code=0;value=@{id=$script:PendingId;outcome='Applied';code='applied';succeeded=$true}} }
                'different-completed-id' { return @{exit_code=0;value=@{id=[Guid]::NewGuid().ToString();outcome='Applied';code='applied';succeeded=$true}} }
                'unconfirmed' { return @{exit_code=1;value=@{id=$script:PendingId;outcome='Unconfirmed';code='unconfirmed';succeeded=$false}} }
                default { return @{exit_code=1;value=@{id=[Guid]::NewGuid().ToString();outcome='Rejected';code=$script:RecoveryVariant;succeeded=$false}} }
            }
        }
        $before=$script:CliCalls.Count
        if ($variant -in @('completed','different-completed-id')) {
            $null = & $executeAction -Action outcome -Plan $run.plan -Launch $run.launch -Binding 'offline' -RequestId ([Guid]::Empty)
        } else {
            Assert-Refused { & $executeAction -Action outcome -Plan $run.plan -Launch $run.launch -Binding 'offline' -RequestId ([Guid]::Empty) } 'unresolved or rejected'
        }
        Assert-Check ($script:CliCalls.Count -eq ($before+1)) 'Recovery made more than one CLI call.'
        Assert-Check ((Test-Path -LiteralPath (Join-Path $run.plan.folder 'pending.json')) -eq ($variant -ne 'completed')) 'Recovery incorrectly cleared or retained the outstanding request.'
    }
}
Test-Case 'lost submission response preserves request ID and blocks another edit' {
    $run = New-CheckRun
    $source = Join-Path $run.plan.folder 'input.csx'
    [IO.File]::WriteAllText($source, 'return new { Name = doc.Title };')
    $script:Inspection = New-CheckInspection $run
    $script:MockCli = {
        param($l,$a,$o)
        if ($a[0] -eq 'inspect') { return $script:Inspection }
        Assert-Check ($a[0] -eq 'run-modify') 'Expected a single modification submission.'
        $saved = Get-Content -LiteralPath (Join-Path $l.folder 'pending.json') -Raw | ConvertFrom-Json
        $requestIndex = [Array]::IndexOf($a,'--request-id')
        Assert-Check ($requestIndex -ge 0 -and $saved.request_id -eq $a[$requestIndex+1]) 'Request ID was not saved before submission.'
        throw 'Simulated lost transport response.'
    }
    Assert-Refused { & $executeAction -Action modify -Plan $run.plan -Launch $run.launch -Binding 'offline' -Source $source -RequestId ([Guid]::Empty) -BudgetMilliseconds 1000 } 'Simulated lost transport'
    $pending = Get-Content -LiteralPath (Join-Path $run.plan.folder 'pending.json') -Raw | ConvertFrom-Json
    Assert-Check ((Get-FileHash -LiteralPath $pending.source).Hash -eq $pending.source_sha256) 'Preserved source/hash do not match.'
    Assert-Check (Test-Path -LiteralPath $pending.capture) 'Inspection evidence was not retained.'
    $before=$script:CliCalls.Count
    Assert-Refused { & $executeAction -Action modify -Plan $run.plan -Launch $run.launch -Binding 'offline' -Source $source -RequestId ([Guid]::Empty) } 'earlier submission needs outcome recovery'
    Assert-Check ($script:CliCalls.Count -eq $before) 'A lost response caused a second submission.'
}

. (Join-Path $PSScriptRoot 'check-development-defaults.ps1')

$failed = @($script:Checks | Where-Object { -not $_.passed })
$report = @{passed=($failed.Count -eq 0);checks=$script:Checks.Count;failed=$failed.Count;native_revit_tested=$false;
    scope='Offline production safety helpers and action control flow with check-local process/CLI mocks';
    completed_utc=[DateTime]::UtcNow.ToString('o');results=@($script:Checks);retained_runs=@($script:TestRuns);process_launches=$script:StartCalls}
Write-DevJson (Join-Path $checkFolder 'result.json') $report
Write-Output "Development safety: $($script:Checks.Count - $failed.Count)/$($script:Checks.Count) passed. Evidence: $checkFolder\result.json"
if ($failed.Count) { exit 1 }
