# Included only by check-development.ps1. Exercise the real parent script boundary
# with omitted optional arguments, in addition to the action-level safety checks.
$wrapperFolder = Join-Path $checkFolder 'wrapper-defaults'
New-Item -ItemType Directory -Path $wrapperFolder | Out-Null
$wrapperPath = Join-Path $wrapperFolder 'dev-session.ps1'
# Each real file boundary has its own script scope. Seed only check-local mock
# state there; shared call/evidence collections remain the same object instances.
$seed = 'foreach ($key in $offlineState.Keys) { Set-Variable -Name $key -Value $offlineState[$key] -Scope Script }'
$parentAst=$sessionAction.Ast
$parentBody=($parentAst.EndBlock.Statements | ForEach-Object { $_.Extent.Text }) -join "`n"
[IO.File]::WriteAllText($wrapperPath, $parentAst.ParamBlock.Extent.Text + "`n" + $seed + "`n" + $parentBody)
$tokens=$null; $errors=$null
$childAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'dev-execute.ps1'), [ref]$tokens, [ref]$errors)
Assert-Check ($errors.Count -eq 0) 'Development child script does not parse.'
# The unchanged parent invokes this file through its real $PSScriptRoot path.
# Its parameter block is the production child's; dependency dispatch stays local.
[IO.File]::WriteAllText((Join-Path $wrapperFolder 'dev-execute.ps1'),
    $childAst.ParamBlock.Extent.Text + "`n" + $seed + "`nInvoke-CheckChild -Forwarded `$PSBoundParameters`n")
function Invoke-CheckChild($Forwarded) {
    Assert-Check ($Forwarded.ContainsKey('RequestId')) 'Parent did not forward RequestId.'
    Assert-Check ($Forwarded.RequestId -is [Guid] -and $Forwarded.RequestId -eq [Guid]::Empty) 'Omitted parent RequestId did not become Guid.Empty.'
    $offlineState.ForwardedEmptyRequestId=$true
    & $executeAction @Forwarded
}

foreach ($entry in @('parent-query','parent-outcome','child-outcome')) {
    Test-Case "omitted RequestId supports $entry through production parameter binding" {
        $run = New-CheckRun; $null = New-CheckBinding $run
        $registration = Join-Path $run.plan.folder 'offline-registration.addin'
        [IO.File]::WriteAllText($registration, 'Offline placeholder registration; never installed.')
        $run.launch | Add-Member -NotePropertyName registration -NotePropertyValue $registration
        $run.launch | Add-Member -NotePropertyName registration_hash -NotePropertyValue (Get-FileHash -LiteralPath $registration).Hash
        Write-DevJson (Join-Path $run.plan.folder 'launch.json') $run.launch
        $source = Join-Path $run.plan.folder 'query.csx'
        [IO.File]::WriteAllText($source, 'return new { Name = doc.Title };')
        $script:Inspection = New-CheckInspection $run
        $script:DefaultCase=$entry
        $script:PendingId=[Guid]::NewGuid().ToString()
        if ($entry -ne 'parent-query') { Write-DevJson (Join-Path $run.plan.folder 'pending.json') @{request_id=$script:PendingId} }
        $script:MockCli = {
            param($l,$a,$o)
            if ($a[0] -eq 'inspect') { return $script:Inspection }
            $index=[Array]::IndexOf($a,'--request-id')
            Assert-Check ($index -ge 0) 'Missing native request ID.'
            $id=$a[$index+1]
            if ($script:DefaultCase -eq 'parent-query') {
                Assert-Check ($a[0] -eq 'run-query' -and [Guid]::Parse($id) -ne [Guid]::Empty) 'Omitted parent ID did not reach a fresh query.'
                return @{exit_code=0;value=@{id=$id;outcome='Inspected';succeeded=$true}}
            }
            Assert-Check ($a[0] -eq 'get-operation' -and $id -eq $script:PendingId) 'Omitted outcome ID did not poll the original pending request.'
            return @{exit_code=0;value=@{id=$id;outcome='Applied';succeeded=$true}}
        }
        $offlineState=@{ForwardedEmptyRequestId=$false}
        foreach ($name in @('DevProject','DevRuns','CliCalls','ProcessCalls','MockExpectedPid','MockProcess','MockCli','Inspection','DefaultCase','PendingId')) {
            $offlineState[$name]=Get-Variable -Name $name -ValueOnly -Scope Script
        }
        $before=$script:CliCalls.Count
        if ($entry -eq 'child-outcome') {
            $null = & $executeAction -Action outcome -Plan $run.plan -Launch $run.launch -Binding 'offline'
        } else {
            $action = if ($entry -eq 'parent-query') { 'query' } else { 'outcome' }
            $null = & $wrapperPath -Action $action -RunFolder $run.plan.folder -Source $source -SigningConfigPath $run.plan.signing_config
            Assert-Check $offlineState.ForwardedEmptyRequestId 'Production parent did not reach child parameter binding.'
        }
        $expectedCalls = if ($entry -eq 'parent-query') { 3 } else { 1 }
        Assert-Check (($script:CliCalls.Count-$before) -eq $expectedCalls) 'Unexpected submission or recovery call count.'
        Assert-Check (-not (Test-Path -LiteralPath (Join-Path $run.plan.folder 'pending.json'))) 'Confirmed operation retained pending state.'
    }
}
