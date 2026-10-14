param([Parameter(Mandatory,Position=0)][string]$RunFolder)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = [IO.Path]::GetFullPath($RunFolder); $id = [Guid]::Empty
if ((Split-Path $folder -Parent) -ne (Join-Path $root 'artifacts\mcp\live') -or -not [Guid]::TryParse((Split-Path $folder -Leaf),[ref]$id)) { throw 'Use an exact prepared MCP qualification run.' }
if (-not (Test-Path (Join-Path $folder 'mcp-native.json'))) { throw 'MCP and independent native checks must pass first.' }
if (Test-Path (Join-Path $folder 'codex-complete.json')) { throw 'Codex qualification already complete; preserve it.' }
$plan = Get-Content (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
foreach ($entry in $plan.hashes.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $folder 'bundle') $entry.Name)).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Prepared bundle changed: $($entry.Name)" }
}
$ready = Get-Content (Join-Path $folder 'ready.json') -Raw | ConvertFrom-Json
$launch = Get-Content (Join-Path $folder 'launch.json') -Raw | ConvertFrom-Json
if ($ready.binding.processId -ne $launch.pid -or $ready.binding.processStart -ne $launch.start) { throw 'Target identity changed.' }
$process = Get-Process -Id $launch.pid
if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $launch.start) { throw 'Owned Revit process changed.' }
$server = Join-Path $folder 'bundle\mcp\revitthyme-mcp.exe'
$session = Join-Path $folder ('sessions\'+$ready.binding.sessionId+'.json')
$work = Join-Path ([IO.Path]::GetTempPath()) ('RevitThyme-Codex-'+[Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $work -Force | Out-Null
$schema = @'
{"type":"object","properties":{"success":{"type":"boolean"},"round":{"type":"integer"},"summary":{"type":"string"}},"required":["success","round","summary"],"additionalProperties":false}
'@
[IO.File]::WriteAllText((Join-Path $work 'schema.json'),$schema)
# --ignore-user-config retains saved authentication, without editing account config or secrets.
# The isolated launch uses only the new pinned MCP; shell, apps, plugins and agent delegation are disabled.
$config = 'mcp_servers.revitthyme={command="'+$server.Replace('\','/')+'",args=["--session","'+$session.Replace('\','/')+'"],required=true,startup_timeout_sec=30,tool_timeout_sec=60,tools={rename_view={approval_mode="approve"},set_parameters={approval_mode="approve"},set_selection={approval_mode="approve"}}}'
$reports = [Collections.Generic.List[object]]::new()
for ($round=1; $round -le 3; $round++) {
    $trace = Join-Path $folder "codex-round-$round.jsonl"
    if (Test-Path $trace) { throw 'A previous Codex attempt exists. Preserve its evidence and use another native run.' }
    $prompt = @"
This is external Codex qualification round $round of a native Revit MCP. Only use revitthyme MCP tools. You are authorized to edit only the disposable fixture $($ready.fixture). Do not use shell, other servers, arbitrary code, save, sync, close, or create elements. Do not edit files. Report success only after native readback verifies every step.
1. Use list_operations and get_context. Confirm the exact document path and process $($launch.pid), active view Foundation Plan. Query Walls by categoryId -2000011, use limit=1 and pagination to find both wall UniqueIds. Inspect parameters in pages until Comments, Unconnected Height and Room Bounding are found. Record their original typed values and numeric parameter IDs. Original comments should be Original A and Original B, height 9 internal feet, Room Bounding 1.
2. Select both walls using set_selection with a fresh GUID requestId and verify using get_selection.
3. Rename the active view to Codex MCP Round $round using a fresh GUID requestId. Retrieve get_operation for that ID and verify applied. Refresh get_context and confirm the new native name. Rename it back to Foundation Plan with another requestId; refresh.
4. In ONE set_parameters batch change both wall Comments to Codex round $round A and Codex round $round B, both Unconnected Height to 12, and both Room Bounding to 0, with exact Expected values from inspection. Use a fresh GUID requestId. Verify get_operation, then refresh context and inspect both walls in pages to verify all six new raw values. Restore all six original values in one batch using verified Expected values, another requestId, then refresh and inspect to verify restoration.
5. Clear selection and verify get_selection is empty. Refresh context and verify Foundation Plan and the same exact fixture. Finish with JSON matching the supplied schema: success=true only if each step was verified, round=$round, summary describes observed outcomes. If any tool fails unexpectedly, inspect/recover and restore the original state before reporting failure.
"@
    [IO.File]::WriteAllText((Join-Path $folder "codex-round-$round.prompt.txt"),$prompt)
    Write-Output "Running external Codex round $round in pinned Revit process $($launch.pid)."
    $prompt | & codex exec --ignore-user-config --ephemeral --skip-git-repo-check --json -C $work --sandbox read-only -c 'approval_policy="never"' -c $config --disable shell_tool --disable apps --disable plugins --disable multi_agent --disable hooks --disable computer_use --disable browser_use --disable image_generation --output-schema (Join-Path $work 'schema.json') -o (Join-Path $folder "codex-round-$round.final.json") - 1> $trace 2> (Join-Path $folder "codex-round-$round.stderr.txt")
    if ($LASTEXITCODE) { throw "Codex round $round failed. Evidence retained." }
    $final = Get-Content (Join-Path $folder "codex-round-$round.final.json") -Raw | ConvertFrom-Json
    if (-not $final.success -or $final.round -ne $round) { throw 'Codex reported incomplete qualification.' }
    $events = @(Get-Content -LiteralPath $trace | ForEach-Object { $_ | ConvertFrom-Json })
    $calls = @($events | Where-Object { $_.type -eq 'item.completed' -and $_.item.type -eq 'mcp_tool_call' } | ForEach-Object { $_.item })
    foreach ($tool in @('get_context','query_elements','inspect_elements','set_selection','get_selection','rename_view','set_parameters','get_operation')) {
        if (-not @($calls | Where-Object { $_.tool -eq $tool }).Count) { throw "Codex trace did not prove $tool use." }
    }
    if (@($calls | Where-Object { $_.server -ne 'revitthyme' -or $_.status -ne 'completed' }).Count) { throw 'Unexpected MCP server or failed tool call.' }
    if (@($events | Where-Object { $_.type -eq 'item.completed' -and $_.item.type -in @('command_execution','file_change') }).Count) { throw 'Qualification used non-MCP mutation/execution.' }
    @{round=$round;passed=$true} | ConvertTo-Json | Set-Content (Join-Path $folder "codex-round-$round.request.json")
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $ack = Join-Path $folder "codex-round-$round.native.json"
    while (-not (Test-Path $ack) -and [DateTime]::UtcNow -lt $deadline) {
        if (Test-Path (Join-Path $folder 'native-result.json')) { throw 'Native qualification ended before checkpoint.' }
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-Path $ack) -or -not (Get-Content $ack -Raw | ConvertFrom-Json).passed) { throw 'Independent native checkpoint failed.' }
    $reports.Add(@{round=$round;passed=$true;tool_calls=$calls.Count;final=$final;native_checkpoint=$ack})
    Write-Output "Codex round $round passed $($calls.Count) MCP calls and independent native readback."
}
@{passed=$true;codex_version=(& codex --version);work_directory=$work;rounds=$reports} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'codex-complete.json') -Encoding utf8
Write-Output 'Three external Codex rounds passed; native Undo/Redo and document-switch qualification now finishes on Idling.'
