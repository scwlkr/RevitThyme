param([string]$Action, $Plan, $Launch, [string]$Binding, [string]$Source, [Guid]$RequestId=[Guid]::Empty, [int]$BudgetMilliseconds)
. (Join-Path $PSScriptRoot 'dev-common.ps1')
$pendingPath = Join-Path $Plan.folder 'pending.json'
if ($Action -eq 'outcome') {
    $pending = if (Test-Path -LiteralPath $pendingPath) { Get-Content -LiteralPath $pendingPath -Raw | ConvertFrom-Json } else { $null }
    if ($RequestId -eq [Guid]::Empty) {
        if (-not $pending) { throw 'Supply -RequestId, or recover the outstanding pending request.' }
        $RequestId = [Guid]$pending.request_id
    }
    $output = Join-Path $Plan.folder ('results\outcome-'+$RequestId+'-'+[Guid]::NewGuid().ToString('N')+'.json')
    $result = Invoke-DevCli $Launch @('get-operation','--session',$Binding,'--request-id',$RequestId.ToString(),'--json') $output
    if ($pending -and $pending.request_id -eq $RequestId.ToString() -and $result.value.id -eq $RequestId.ToString() -and
        $result.value.outcome -in @('Inspected','Applied','Unchanged','Rejected','RolledBack')) {
        Remove-Item -LiteralPath $pendingPath
    }
    $result.value | ConvertTo-Json -Depth 16
    if ($result.exit_code -ne 0) { throw "Outcome is unresolved or rejected; retained evidence: $output" }
    return
}
if (Test-Path -LiteralPath $pendingPath) { throw 'An earlier submission needs outcome recovery. Use dev-outcome before submitting another operation.' }
if (-not $Source -or -not [IO.Path]::IsPathFullyQualified($Source) -or -not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw 'Supply an absolute -Source file containing a trusted C# method body.' }
if ((Get-Item -LiteralPath $Source).Length -gt 24576) { throw 'Source exceeds the existing 24 KiB limit.' }
if ($RequestId -ne [Guid]::Empty) { throw 'New submissions generate their request IDs. Use dev-outcome -RequestId to recover an existing operation.' }
$RequestId = [Guid]::NewGuid()
$requestFolder = Join-Path $Plan.folder ('results\'+$RequestId)
New-Item -ItemType Directory -Path $requestFolder | Out-Null
$savedSource = Join-Path $requestFolder 'source.csx'
Copy-Item -LiteralPath $Source -Destination $savedSource
$capture = Join-Path $requestFolder 'capture.json'
$null = Get-DevContext $Plan $Launch $Binding $capture
$request = @{request_id=$RequestId.ToString();action=$Action;source=$savedSource;source_sha256=(Get-FileHash -LiteralPath $savedSource).Hash;
    capture=$capture;budget_ms=$BudgetMilliseconds;started_utc=[DateTime]::UtcNow.ToString('o')}
Write-DevJson (Join-Path $requestFolder 'request.json') $request
# Written before submitting: a lost response or interrupted wrapper cannot silently submit again.
Write-DevJson $pendingPath $request
$output = Join-Path $requestFolder 'result.json'
$result = Invoke-DevCli $Launch @(('run-'+$Action),'--session',$Binding,'--capture',$capture,'--source',$savedSource,'--trusted','true',
    '--request-id',$RequestId.ToString(),'--budget-ms',$BudgetMilliseconds.ToString(),'--json') $output
if ($result.value.id -ne $RequestId.ToString()) { throw 'Submission result ID changed. Recover the original request before continuing.' }
if ($result.value.outcome -in @('Inspected','Applied','Unchanged','Rejected','RolledBack')) { Remove-Item -LiteralPath $pendingPath }
if ($result.value.succeeded) {
    $null = Get-DevContext $Plan $Launch $Binding (Join-Path $requestFolder 'after.json')
}
$result.value | ConvertTo-Json -Depth 16
if ($result.exit_code -ne 0) { throw "Development operation failed or is unconfirmed. Request $RequestId; evidence: $requestFolder" }
