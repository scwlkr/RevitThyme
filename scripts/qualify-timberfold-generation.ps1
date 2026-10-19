[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunFolder, [Parameter(Mandatory)][string]$FixtureReceipt,
    [switch]$RequirePerformanceTarget, [string]$MachineProfile)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'dev-common.ps1')
function SourceHash([string]$Path) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($FixtureReceipt)) {
    throw 'Use absolute pinned development run and independently qualified fixture receipt paths.'
}
if ($RequirePerformanceTarget -and (-not $MachineProfile -or -not [IO.Path]::IsPathFullyQualified($MachineProfile))) {
    throw 'Performance qualification requires an absolute hardware profile JSON path.'
}
$machine = if ($MachineProfile) { Get-Content -LiteralPath $MachineProfile -Raw | ConvertFrom-Json } else { $null }
if ($RequirePerformanceTarget -and (-not $machine.cpu -or -not $machine.memoryGiB -or -not $machine.osVersion -or -not $machine.dotnetSdk)) {
    throw 'Performance hardware profile must record CPU, RAM, OS and .NET SDK.'
}
$fixture = Get-Content -LiteralPath $FixtureReceipt -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $RunFolder 'plan.json') -Raw | ConvertFrom-Json
$launch = Get-Content -LiteralPath (Join-Path $RunFolder 'launch.json') -Raw | ConvertFrom-Json
if (-not $fixture.nativeFixtureChecksPassed -or $plan.fixture_source -ne $fixture.modelPath -or $plan.fixture_sha256 -ne $fixture.rvtSha256) {
    throw 'Development copy differs from the qualified original fixture.'
}
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or $before.context.snapshot.isModified) {
    throw 'Generation qualification requires a ready unmodified development copy with no uncertain request.'
}
$paths = @($plan.fixture_source, $plan.fixture)
$beforeHashes = @($paths | ForEach-Object { SourceHash $_ })
$directory = Join-Path $RunFolder ('timberfold-generation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$cli = Join-Path $launch.bundle 'cli/revitthyme.exe'
if ((Get-FileHash -LiteralPath $cli).Hash -ne $launch.cli_hash) { throw 'Pinned installed CLI changed.' }
$session = $before.session_file
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}) }
function Invoke-Cli([string]$Evidence, [string[]]$Arguments, [bool]$AllowTerminalFailure = $false) {
    $raw = & $cli @Arguments --session $session --json | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $directory $Evidence), $raw)
    $value = $raw | ConvertFrom-Json
    if (-not $value.timberRun -or ($code -ne 0 -and -not ($AllowTerminalFailure -and $value.timberRun.terminal))) {
        throw "CLI operation requires inspection: $Evidence"
    }
    return $value
}
function Wait-Run([string]$Id, [string]$Prefix) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $status = Invoke-Cli "$Prefix-status.json" @('timberfold','status','--run-id',$Id) $true
        if (-not $status.timberRun.terminal) { Start-Sleep -Milliseconds 250 }
        if ($clock.Elapsed.TotalSeconds -gt 180) { throw "Run exceeded 180 seconds; retain ID $Id and inspect its definitive native receipt before retrying." }
    } while (-not $status.timberRun.terminal)
    return $status
}
$previewId = [Guid]::NewGuid().ToString('D')
$null = Invoke-Cli 'preview-accepted.json' @('timberfold','preview','--run-id',$previewId)
$preview = (Wait-Run $previewId 'preview').timberRun
if ($preview.state -ne 'PreviewReady') { throw "Preview blocked: $($preview.directory)" }
$generationId = [Guid]::NewGuid().ToString('D')
$generationAccepted = Invoke-Cli 'generation-accepted.json' @('timberfold','generate','--preview-run-id',$previewId,'--run-id',$generationId)
$generation = (Wait-Run $generationId 'generation').timberRun
if ($generation.state -ne 'KitReady') {
    throw "Native generation $($generation.state): $($generation.diagnostics[0].message). Inspect $($generation.directory)"
}
$verified = Invoke-Cli 'verification.json' @('timberfold','verify','--run-id',$generationId)
Check 'installed CLI publishes and rehashes a digitally verified native kit' ($verified.timberRun.state -eq 'KitReady' -and $generation.nativeCommitted)
Check 'digital delivery retains outstanding physical checks' ($generation.physicalChecksOutstanding -and -not $generation.manufacturingReady -and -not $generation.nativeImportOutstanding)
Check 'complete original panel and opening mappings survive generation' ($generation.panelCount -eq $fixture.sourceMap.expected.AnalyticShellPanels -and
    ((Get-Content -LiteralPath (Join-Path $generation.directory 'fitted.json') -Raw | ConvertFrom-Json).draft.openingMap.Count -eq
        ($fixture.sourceMap.expected.Windows + $fixture.sourceMap.expected.Openings)))
$receipt = Get-Content -LiteralPath (Join-Path $generation.directory 'native-receipt.json') -Raw | ConvertFrom-Json
Check 'native receipt confirms preserved source and independent DWG readback' ($receipt.state -eq 'Committed' -and $receipt.sourcePreserved -and $receipt.nativeReadbackPassed -and $receipt.createdElements.Count -gt 0)
Check 'native cut-only DWG inventory matches every stock sheet and calibration' (@($receipt.files | Where-Object {$_.role -eq 'CutDwg'}).Count -eq $generation.sheetCount -and
    @($receipt.files | Where-Object {$_.role -eq 'CalibrationDwg'}).Count -eq 1)
Check 'native documentation includes owned sheets labels and viewports' (@($receipt.createdElements | Where-Object {$_.role -eq 'NativeSheet'}).Count -eq ($generation.sheetCount + 1) -and
    @($receipt.createdElements | Where-Object {$_.role -eq 'PartLabel'}).Count -ge $generation.panelCount -and
    @($receipt.createdElements | Where-Object {$_.role -eq 'Viewport'}).Count -eq ($generation.sheetCount + 1))
$timingPath = Join-Path $generation.directory 'run-timings.json'
$timing = Get-Content -LiteralPath $timingPath -Raw | ConvertFrom-Json
$knownStages = @('Accepted','Capturing','Fitting','Layout','Cutting','Guides','Finalizing')
$seenStages = @($timing.stages | ForEach-Object {$_.stage})
$invalidStages = @($timing.stages | Where-Object {
    $_.stage -notin $knownStages -or $null -eq $_.milliseconds -or
    [double]::IsNaN([double]$_.milliseconds) -or [double]::IsInfinity([double]$_.milliseconds) -or [double]$_.milliseconds -lt 0
})
$stageTotal = [double](($timing.stages | Measure-Object -Property milliseconds -Sum).Sum)
Check 'generation timing accounts for every completed stage once' ($timing.schema -eq 1 -and $timing.runId -eq $generationId -and
    $timing.state -eq 'KitReady' -and $null -eq $timing.activeStage -and $invalidStages.Count -eq 0 -and
    $seenStages.Count -eq $knownStages.Count -and @($seenStages | Sort-Object -Unique).Count -eq $knownStages.Count -and
    [Math]::Abs($stageTotal - [double]$generation.elapsedMilliseconds) -le 0.001 -and
    [Math]::Abs([double]$timing.elapsedMilliseconds - [double]$generation.elapsedMilliseconds) -le 0.001)
function Stage-Milliseconds([string[]]$Names) {
    return [double](($timing.stages | Where-Object {$_.stage -in $Names} | Measure-Object -Property milliseconds -Sum).Sum)
}
$performance = @{panelCount=$generation.panelCount;elapsedMilliseconds=$generation.elapsedMilliseconds;
    extractionMilliseconds=(Stage-Milliseconds @('Accepted','Capturing'));
    computationLayoutMilliseconds=(Stage-Milliseconds @('Fitting','Layout'));
    outputNativeMilliseconds=(Stage-Milliseconds @('Cutting','Guides','Finalizing'));
    nativeExtractionMilliseconds=$timing.nativeExtractionMilliseconds;nativeFinalizationMilliseconds=$timing.nativeFinalizationMilliseconds;
    machine=$machine;targetRequired=[bool]$RequirePerformanceTarget;
    timingBasis='Generation recaptures its source and includes geometry, layout, guides and native checks. Startup and the prerequisite preview are excluded. Native callback durations overlap stage wall times; do not add them again.'}
Check 'native callback durations are finite subsets of their measured stages' ($null -ne $timing.nativeExtractionMilliseconds -and
    $null -ne $timing.nativeFinalizationMilliseconds -and
    -not [double]::IsNaN([double]$timing.nativeExtractionMilliseconds) -and -not [double]::IsInfinity([double]$timing.nativeExtractionMilliseconds) -and
    -not [double]::IsNaN([double]$timing.nativeFinalizationMilliseconds) -and -not [double]::IsInfinity([double]$timing.nativeFinalizationMilliseconds) -and
    [double]$timing.nativeExtractionMilliseconds -ge 0 -and [double]$timing.nativeFinalizationMilliseconds -ge 0 -and
    [double]$timing.nativeExtractionMilliseconds -le (Stage-Milliseconds @('Capturing')) + 1 -and
    [double]$timing.nativeFinalizationMilliseconds -le (Stage-Milliseconds @('Finalizing')) + 1)
$performance.targetPassed = ($generation.panelCount -eq 100 -and $performance.elapsedMilliseconds -le 60000 -and
    $performance.extractionMilliseconds -le 15000 -and $performance.computationLayoutMilliseconds -le 20000 -and
    $performance.outputNativeMilliseconds -le 25000)
if ($RequirePerformanceTarget) { Check '100-panel complete generation meets the approved 60-second stage budgets' $performance.targetPassed }
$performance | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'performance.json') -Encoding utf8
$retry = Invoke-Cli 'same-id-retry.json' @('timberfold','generate','--preview-run-id',$previewId,'--run-id',$generationId)
# Dispatch history replays the original acceptance; a fresh status reads the settled run.
Check 'same-ID installed retry replays the exact original generation acceptance' (
    [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $directory 'generation-accepted.json'))) -ceq
    [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $directory 'same-id-retry.json'))) -and
    $retry.id -eq $generationAccepted.id -and $retry.id -eq $generationId -and $retry.timberRun.runId -eq $generationId)
$retained = (Invoke-Cli 'same-id-retained-status.json' @('timberfold','status','--run-id',$generationId)).timberRun
Check 'same-ID retry preserves the settled native generation and elapsed time' ($retained.runId -eq $generationId -and
    $retained.state -eq 'KitReady' -and $retained.terminal -and $retained.nativeCommitted -and
    $retained.elapsedMilliseconds -eq $generation.elapsedMilliseconds)
$after = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot recover native context after generation; retain this development process.' }
$identityFields = @('processId','processStart','sessionId','documentId','viewId')
Check 'generation preserves the exact process document and active view' (@($identityFields | Where-Object {
    $before.context.snapshot.target.$_ -ne $after.context.snapshot.target.$_
}).Count -eq 0)
Check 'requested documentation modifies only the unsaved document' ($after.context.snapshot.isModified -and
    ($beforeHashes -join '|') -eq (@($paths | ForEach-Object { SourceHash $_ }) -join '|'))
$result = @{passed=(@($checks | Where-Object {-not $_.passed}).Count -eq 0);runFolder=$RunFolder;fixture=$fixture.fixture;
    packageManifestSha256=$plan.manifest_hash;preview=$preview;generation=$generation;checks=$checks;performance=$performance;
    independentPostGenerationOutstanding=$true;singleUndoOutstanding=$true;physicalChecksOutstanding=$true}
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'qualification.json') -Encoding utf8
$result | ConvertTo-Json -Depth 20
if (-not $result.passed) { throw 'Installed generation checks failed; retain evidence and the native process.' }
