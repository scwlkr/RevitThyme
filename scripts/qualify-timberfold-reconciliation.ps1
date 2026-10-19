[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunFolder, [Parameter(Mandatory)][string]$GenerationDirectory,
    [switch]$ExpectAbsent)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'dev-common.ps1')
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($GenerationDirectory)) {
    throw 'Use absolute pinned development and original generation directories.'
}
$generationId = [Guid]::Empty
if (-not [Guid]::TryParseExact((Split-Path $GenerationDirectory -Leaf),'D',[ref]$generationId)) { throw 'Generation directory needs its exact run GUID.' }
$owned = Join-Path $env:LOCALAPPDATA ('RevitThyme\TimberFold\runs\' + $generationId.ToString('D'))
if ([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($GenerationDirectory)) -ne $owned) {
    throw 'Use the exact host-owned original generation directory.'
}
function Hash([string]$Path) {
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Inventory([string]$Root) {
    $rows = [Collections.Generic.List[string]]::new()
    $folders = [Collections.Generic.Stack[string]]::new(); $folders.Push($Root)
    while ($folders.Count) {
        $folder = $folders.Pop()
        if (([IO.File]::GetAttributes($folder) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Redirected generation directory.' }
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($folder)) {
            $attributes = [IO.File]::GetAttributes($path)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Redirected generation evidence.' }
            $relative = [IO.Path]::GetRelativePath($Root,$path).Replace('\','/')
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $rows.Add('directory:' + $relative); $folders.Push($path) }
            else { $rows.Add('file:' + $relative + ':' + (Hash $path)) }
            if ($rows.Count -gt 10000) { throw 'Generation evidence exceeds its bounded inventory.' }
        }
    }
    return @($rows | Sort-Object -CaseSensitive)
}
$launch = Get-Content -LiteralPath (Join-Path $RunFolder 'launch.json') -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $RunFolder 'plan.json') -Raw | ConvertFrom-Json
$receipt = Get-Content -LiteralPath (Join-Path $GenerationDirectory 'native-receipt.json') -Raw | ConvertFrom-Json
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or $receipt.state -ne 'Committed') {
    throw 'Read-only reconciliation qualification requires a ready pinned process and a definitive original commit receipt.'
}
foreach ($field in @('processId','processStart','sessionId','documentId')) {
    if ($before.context.snapshot.target.$field -ne $receipt.afterTarget.$field) { throw "Reconciliation target differs: $field" }
}
$original = Inventory $GenerationDirectory
$sourcePaths = @($plan.fixture_source,$plan.fixture)
$sourceHashes = @($sourcePaths | ForEach-Object { Hash $_ })
$cli = Join-Path $launch.bundle 'cli/revitthyme.exe'
if ((Get-FileHash -LiteralPath $cli).Hash -ne $launch.cli_hash) { throw 'Pinned installed CLI changed.' }
$directory = Join-Path $RunFolder ('timberfold-reconciliation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}) }
function Invoke-Cli([string]$Evidence,[string[]]$Arguments) {
    $raw = & $cli @Arguments --session $before.session_file --json | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $directory $Evidence),$raw)
    $value = $raw | ConvertFrom-Json
    if (-not $value.timberRun -or ($code -ne 0 -and $value.timberRun.state -ne 'Unconfirmed')) { throw "Inspect retained reconciliation evidence: $Evidence" }
    return $value
}
$recoveryId = [Guid]::NewGuid().ToString('D')
$accepted = Invoke-Cli 'accepted.json' @('timberfold','reconcile','--generation-run-id',$generationId.ToString('D'),'--run-id',$recoveryId)
$clock = [Diagnostics.Stopwatch]::StartNew()
do {
    $status = (Invoke-Cli 'status.json' @('timberfold','status','--run-id',$recoveryId)).timberRun
    if (-not $status.terminal) { Start-Sleep -Milliseconds 250 }
    if ($clock.Elapsed.TotalSeconds -gt 180) { throw "Reconciliation still unsettled; retain recovery ID $recoveryId." }
} while (-not $status.terminal)
$native = Get-Content -LiteralPath (Join-Path $status.directory 'native-reconciliation.json') -Raw | ConvertFrom-Json
Check 'recovery handle preserves its original generation identity and physical gap' ($status.runId -eq $recoveryId -and
    $status.generationRunId -eq $generationId.ToString('D') -and $status.physicalChecksOutstanding -and -not $status.manufacturingReady)
if ($ExpectAbsent) {
    Check 'actual absent documentation stays unconfirmed and is not recreated' ($status.state -eq 'Unconfirmed' -and $native.state -eq 'Absent' -and
        -not $native.nativeReadbackPassed -and -not (Test-Path -LiteralPath (Join-Path $status.directory 'kit.json')))
} else {
    Check 'installed current native reconciliation verifies the existing complete kit' ($status.state -eq 'KitReady' -and $native.state -eq 'Verified' -and
        $native.sourcePreserved -and $native.nativeReadbackPassed -and $native.observedElements.Count -eq $receipt.createdElements.Count)
}
$retry = Invoke-Cli 'same-id-retry.json' @('timberfold','reconcile','--generation-run-id',$generationId.ToString('D'),'--run-id',$recoveryId)
# Acceptance is retained by dispatch ID; status independently reads the settled recovery.
Check 'same-ID reconciliation replays the exact original acceptance' (
    [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $directory 'accepted.json'))) -ceq
    [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $directory 'same-id-retry.json'))) -and
    $retry.id -eq $accepted.id -and $retry.id -eq $recoveryId -and $retry.timberRun.runId -eq $recoveryId -and
    $retry.timberRun.generationRunId -eq $generationId.ToString('D'))
$retained = (Invoke-Cli 'same-id-retained-status.json' @('timberfold','status','--run-id',$recoveryId)).timberRun
Check 'same-ID reconciliation preserves its settled outcome elapsed time and original generation' ($retained.runId -eq $recoveryId -and
    $retained.terminal -and $retained.state -eq $status.state -and $retained.elapsedMilliseconds -eq $status.elapsedMilliseconds -and
    $retained.generationRunId -eq $status.generationRunId -and $retained.generationRunId -eq $generationId.ToString('D'))
$verified = (Invoke-Cli 'verification.json' @('timberfold','verify','--run-id',$recoveryId)).timberRun
Check 'retained recovery evidence re-verifies without rewriting original files' ($verified.state -eq $status.state)
$after = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Native context is unsettled; retain the development process and all evidence.' }
Check 'read-only reconciliation preserves exact current target and modified state' (
    ($before.context.snapshot.target | ConvertTo-Json -Compress) -ceq ($after.context.snapshot.target | ConvertTo-Json -Compress) -and
    $before.context.snapshot.isModified -eq $after.context.snapshot.isModified)
Check 'original generation files directories and timings are unchanged' (($original -join "`n") -ceq ((Inventory $GenerationDirectory) -join "`n"))
Check 'both saved original and disposable RVT hashes are unchanged' (($sourceHashes -join '|') -ceq (@($sourcePaths | ForEach-Object { Hash $_ }) -join '|'))
$result = @{passed=(@($checks | Where-Object {-not $_.passed}).Count -eq 0);runFolder=$RunFolder;generationDirectory=$GenerationDirectory;
    recovery=$status;expectAbsent=[bool]$ExpectAbsent;checks=$checks;physicalChecksOutstanding=$true}
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'qualification.json') -Encoding utf8
$result | ConvertTo-Json -Depth 20
if (-not $result.passed) { throw 'Native reconciliation qualification failed; retain the existing model and both evidence directories.' }
