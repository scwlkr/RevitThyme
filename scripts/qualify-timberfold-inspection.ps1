[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunFolder,
    [Parameter(Mandatory)][string]$FixtureReceipt
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Read-SourceHash([string]$Path) {
    # Revit retains a write handle; share it while reading without changing bytes.
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($FixtureReceipt)) {
    throw 'Use absolute paths to the pinned development run and original fixture receipt.'
}
$receipt = Get-Content -LiteralPath $FixtureReceipt -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $RunFolder 'plan.json') -Raw | ConvertFrom-Json
$launch = Get-Content -LiteralPath (Join-Path $RunFolder 'launch.json') -Raw | ConvertFrom-Json
if (-not $receipt.nativeFixtureChecksPassed -or $plan.fixture_source -ne $receipt.modelPath -or
    $plan.fixture_sha256 -ne $receipt.rvtSha256) { throw 'Run is not a copy of the independently qualified original fixture.' }
$paths = @($plan.fixture_source, $plan.fixture)
$beforeHashes = @($paths | ForEach-Object { Read-SourceHash $_ })
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or
    $before.context.snapshot.isModified) { throw 'Pinned host must be ready, unmodified and without an outstanding request.' }
$directory = Join-Path $RunFolder ('timberfold-inspection-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
# Ask native elements for phase membership independently of product extraction.
# The qualified original receipt supplies IDs/UniqueIds, not the product reply.
$nativeIds = ($receipt.sourceMap.mapped.id | ForEach-Object { [long]$_ }) -join ','
$auditSource = @"
var phase = doc.GetElement(doc.ActiveView.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.VIEW_PHASE).AsElementId()) as Autodesk.Revit.DB.Phase;
if (phase == null) throw new System.InvalidOperationException("Fixture view requires an explicit native phase.");
return new { phaseId = phase.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    sources = new long[] { $nativeIds }.Select(id => {
        var element = doc.GetElement(new Autodesk.Revit.DB.ElementId(id));
        if (element == null) throw new System.InvalidOperationException("Original fixture source is missing.");
        return new { id = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            present = !element.HasPhases() || element.GetPhaseStatus(phase.Id) is Autodesk.Revit.DB.ElementOnPhaseStatus.New or Autodesk.Revit.DB.ElementOnPhaseStatus.Existing,
            kind = element is Autodesk.Revit.DB.Wall ? "Wall" : element is Autodesk.Revit.DB.RoofBase ? "Roof" : element is Autodesk.Revit.DB.Opening ? "Opening" : "Window" };
    }).ToArray() };
"@
$auditPath = Join-Path $directory 'native-phase-audit.csx'
[IO.File]::WriteAllText($auditPath, $auditSource)
$auditRaw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $auditPath | Out-String
[IO.File]::WriteAllText((Join-Path $directory 'native-phase-audit.json'), $auditRaw)
if ($LASTEXITCODE -ne 0) { throw 'Independent native phase audit failed; retain this run.' }
$nativeAudit = ($auditRaw | ConvertFrom-Json).execution.value
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.context.snapshot.isModified) { throw 'Phase audit did not preserve the pinned source.' }
$cli = Join-Path $launch.bundle 'cli\revitthyme.exe'
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$raw = & $cli timberfold inspect --session $before.session_file --json | Out-String
$exitCode = $LASTEXITCODE
$stopwatch.Stop()
[IO.File]::WriteAllText((Join-Path $directory 'operation.json'), $raw)
$operation = $raw | ConvertFrom-Json
$after = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Post-inspection context could not be recovered; retain this run.' }
$afterHashes = @($paths | ForEach-Object { Read-SourceHash $_ })
$inspection = $operation.timberFold.inspection
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) { $checks.Add(@{ name = $Name; passed = $Passed }) }
Check 'installed shared inspection returned a successful bounded reply' ($exitCode -eq 0 -and $operation.succeeded -and $null -ne $inspection)
Check 'both source and copy match the qualified fixture hash' (@($beforeHashes | Where-Object { $_ -ne $receipt.rvtSha256 }).Count -eq 0)
Check 'source and disposable copy file hashes are unchanged' (($beforeHashes -join '|') -eq ($afterHashes -join '|'))
Check 'pinned native target and revision are unchanged' ((ConvertTo-Json $before.context.snapshot.target -Compress) -eq (ConvertTo-Json $after.context.snapshot.target -Compress))
Check 'source remains unmodified without automatic save' (-not $after.context.snapshot.isModified)
Check 'physical fabrication remains explicitly outstanding' ([bool]$inspection.physicalChecksOutstanding)
$blocked = $receipt.fixture -in @('curved-wall', 'slanted-wall', 'ambiguous-glazing')
Check 'unsupported geometry blocks readiness; supported geometry remains accounted' ([bool]$inspection.readyForPreview -ne $blocked)
if ($blocked) {
    $expectedDiagnostic = if ($receipt.fixture -eq 'ambiguous-glazing') { 'opening_geometry_unresolved' } else { 'native_host_geometry' }
    Check 'unsupported fixture reports the specific native geometry failure' ($expectedDiagnostic -in $inspection.diagnostics.code -and $inspection.errorCount -gt 0)
}
if (-not $blocked) {
    $expectedFaces = if ($receipt.fixture -eq 'phase-scope') { 1 } else { $receipt.sourceMap.expected.AnalyticShellPanels }
    Check 'all expected native shell faces are present' ($inspection.faceCount -eq $expectedFaces)
    Check 'all expected hosted cutouts are present' ($inspection.openingCount -eq ($receipt.sourceMap.expected.Windows + $receipt.sourceMap.expected.Openings))
    Check 'there are no blocking diagnostics' ($inspection.errorCount -eq 0)
}
$evidence = $operation.timberFold.evidence
if ($evidence) {
    Check 'full snapshot hash agrees with retained evidence' ((Get-FileHash -LiteralPath (Join-Path $evidence.directory 'snapshot.json')).Hash -eq $evidence.snapshotSha256)
    Check 'full diagnostic hash agrees with retained evidence' ((Get-FileHash -LiteralPath (Join-Path $evidence.directory 'diagnostics.json')).Hash -eq $evidence.diagnosticsSha256)
    $fullSnapshot = (Get-Content -LiteralPath (Join-Path $evidence.directory 'snapshot.json') -Raw | ConvertFrom-Json).snapshot
    Check 'selected phase agrees with the independently audited native view phase' ($inspection.phaseId -eq $nativeAudit.phaseId -and $fullSnapshot.phaseId -eq $nativeAudit.phaseId)
    foreach ($original in $receipt.sourceMap.mapped) {
        $sourceId = [string]$original.id
        $actual = @($fullSnapshot.sources | Where-Object id -eq $sourceId)
        $nativeSource = @($nativeAudit.sources | Where-Object id -eq $sourceId)
        $expectedDisposition = if (-not $nativeSource[0].present) { 'Excluded' }
            elseif (($receipt.fixture -in @('curved-wall','slanted-wall') -and $nativeSource[0].kind -eq 'Wall') -or
                ($receipt.fixture -eq 'ambiguous-glazing' -and $nativeSource[0].kind -eq 'Window')) { 'Unsupported' }
            else { 'Included' }
        Check "native source $sourceId retains its identity, category and scoped disposition" ($actual.Count -eq 1 -and
            $actual[0].uniqueId -eq $original.uniqueId -and $actual[0].category -eq $nativeSource[0].kind -and
            $actual[0].disposition -eq $expectedDisposition)
        if ($expectedDisposition -eq 'Unsupported') {
            Check "blocked native source $sourceId has its own diagnostic" ($sourceId -in $fullSnapshot.diagnostics.sourceId)
        }
        if ($expectedDisposition -eq 'Included' -and $nativeSource[0].kind -in @('Window','Opening')) {
            $cutouts = @($fullSnapshot.openings | Where-Object sourceId -eq $sourceId)
            Check "native opening $sourceId is accounted exactly once" ($cutouts.Count -eq 1 -and $cutouts[0].id -in $actual[0].openingIds)
        }
    }
}
else { Check 'complete private evidence exists' $false }
$result = @{
    schema = 1; fixture = $receipt.fixture; runFolder = $RunFolder; fixtureReceipt = $FixtureReceipt
    manifestHash = $launch.manifest_hash; pid = $launch.pid; elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
    checks = $checks.ToArray(); passed = @($checks | Where-Object { -not $_.passed }).Count -eq 0
    operationId = $operation.id; evidence = $evidence; afterContext = $after.context.snapshot
    manufacturingQualified = $false; physicalChecks = 'Outstanding'
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'qualification.json') -Encoding utf8
$result | Select-Object fixture, passed, elapsedMilliseconds, checks | ConvertTo-Json -Depth 5
Write-Output "Retained native inspection qualification: $directory"
if (-not $result.passed) { throw 'Native inspection qualification failed; inspect retained operation and checks before retrying.' }
