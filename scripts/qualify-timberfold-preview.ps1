[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunFolder,
    [Parameter(Mandatory)][string]$FixtureReceipt
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'dev-common.ps1')
function Read-SourceHash([string]$Path) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($FixtureReceipt)) {
    throw 'Use absolute pinned-run and original-fixture receipt paths.'
}
$fixture = Get-Content -LiteralPath $FixtureReceipt -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $RunFolder 'plan.json') -Raw | ConvertFrom-Json
$launch = Get-Content -LiteralPath (Join-Path $RunFolder 'launch.json') -Raw | ConvertFrom-Json
if (-not $fixture.nativeFixtureChecksPassed -or $plan.fixture_source -ne $fixture.modelPath -or
    $plan.fixture_sha256 -ne $fixture.rvtSha256) { throw 'Pinned run does not match the independently qualified original.' }
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or $before.context.snapshot.isModified) {
    throw 'A ready unmodified pinned source with no outstanding request is required.'
}
$paths = @($plan.fixture_source, $plan.fixture)
$beforeHashes = @($paths | ForEach-Object { Read-SourceHash $_ })
$directory = Join-Path $RunFolder ('timberfold-preview-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$cli = Join-Path $launch.bundle 'cli\revitthyme.exe'
$session = $before.session_file
$runs = [Collections.Generic.List[object]]::new()
foreach ($attempt in 1..2) {
    $id = [Guid]::NewGuid().ToString('D')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $raw = & $cli timberfold preview --session $session --run-id $id --json | Out-String
    [IO.File]::WriteAllText((Join-Path $directory "accepted-$attempt.json"), $raw)
    $accepted = $raw | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $accepted.timberRun.runId -ne $id) { throw 'Shared preview was not accepted; retain evidence and inspect before retrying.' }
    do {
        $raw = & $cli timberfold status --session $session --run-id $id --json | Out-String
        $status = $raw | ConvertFrom-Json
        if ($null -eq $status.timberRun) { throw 'Preview status did not return its retained handle.' }
        if (-not $status.timberRun.terminal) { Start-Sleep -Milliseconds 250 }
        if ($clock.Elapsed.TotalSeconds -gt 60) { throw 'Preview exceeded 60 seconds; inspect the retained run before any retry.' }
    } while (-not $status.timberRun.terminal)
    $clock.Stop()
    [IO.File]::WriteAllText((Join-Path $directory "terminal-$attempt.json"), $raw)
    if ($status.timberRun.state -ne 'PreviewReady') { throw "Preview did not qualify: $($status.timberRun.directory)" }
    $runs.Add(@{ summary=$status.timberRun; wallMilliseconds=$clock.Elapsed.TotalMilliseconds })
}
$first = $runs[0].summary
$second = $runs[1].summary
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}) }
$afterPreview = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Preview context could not be recovered.' }
Check 'product previews preserve the exact native target and revision' ((ConvertTo-Json $before.context.snapshot.target -Compress) -eq
    (ConvertTo-Json $afterPreview.context.snapshot.target -Compress))
Check 'original native panel inventory is complete' ($first.panelCount -eq $fixture.sourceMap.expected.AnalyticShellPanels)
$fitted = Get-Content -LiteralPath (Join-Path $first.directory 'fitted.json') -Raw | ConvertFrom-Json
Check 'fitting passed exact collision verification' ($fitted.state -eq 'GeometryFitted' -and $fitted.verification.passed -and
    $fitted.verification.collisions.Count -eq 0 -and $fitted.verification.unresolved.Count -eq 0)
Check 'every original opening retains a panel mapping' ($fitted.draft.openingMap.Count -eq ($fixture.sourceMap.expected.Windows + $fixture.sourceMap.expected.Openings) -and
    @($fitted.draft.openingMap | Where-Object { $_.panelIds.Count -eq 0 }).Count -eq 0)
Check 'successful preview keeps physical and native-finalization gaps explicit' (-not $first.manufacturingReady -and $first.physicalChecksOutstanding -and $first.nativeImportOutstanding)
$canonical = @('snapshot.json','fitted.json','layout.json') + @(Get-ChildItem -LiteralPath (Join-Path $first.directory 'cutting') -File | ForEach-Object { 'cutting\'+$_.Name })
foreach ($relative in $canonical) {
    Check "canonical content repeats: $relative" ((Get-FileHash -LiteralPath (Join-Path $first.directory $relative)).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $second.directory $relative)).Hash)
}
$assembly = Join-Path $repo 'tests\TimberFold\bin\Debug\net10.0-windows\RevitThyme.TimberFold.Fixtures.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build the independent TimberFold fixture/readback assembly first.' }
$assemblyLiteral = ConvertTo-Json $assembly -Compress
$assemblyHashLiteral = ConvertTo-Json (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant() -Compress
$runLiteral = ConvertTo-Json $first.directory -Compress
$readbackLiteral = ConvertTo-Json (Join-Path $directory 'native-readback') -Compress
$source = @"
var bytes = System.IO.File.ReadAllBytes($assemblyLiteral);
if (System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != $assemblyHashLiteral)
    throw new System.IO.InvalidDataException("Independent qualification assembly changed.");
var assembly = System.Reflection.Assembly.Load(bytes);
try {
    return assembly.GetType("RevitThyme.TimberFold.Fixtures.NativeCutQualification", true).GetMethod("Check")
        .Invoke(null, new object[] { uiapp, $runLiteral, $readbackLiteral, cancellationToken });
} catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
"@
$runner = Join-Path $directory 'native-cut-readback.csx'
[IO.File]::WriteAllText($runner, $source)
$nativeRaw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000 | Out-String
$nativeExit = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $directory 'native-operation.json'), $nativeRaw)
Check 'actual Revit imports all saved DXF contours at physical-inch scale' ($nativeExit -eq 0 -and [bool]($nativeRaw | ConvertFrom-Json).execution.value.passed)
$after = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Native context could not be recovered; retain this run.' }
$afterHashes = @($paths | ForEach-Object { Read-SourceHash $_ })
Check 'qualified original and disposable copy remain byte-identical' (($beforeHashes -join '|') -eq ($afterHashes -join '|') -and
    @($afterHashes | Where-Object { $_ -ne $fixture.rvtSha256 }).Count -eq 0)
Check 'pinned source stays unmodified and unsaved' (-not $after.context.snapshot.isModified)
# The independent consumer opens/closes another document. The host's conservative
# global revision observes that lifecycle; the product preview check above is exact.
$identityFields = @('processId','processStart','sessionId','documentId','viewId')
Check 'independent consumer preserves pinned process, document and view' (@($identityFields | Where-Object {
    $before.context.snapshot.target.$_ -ne $after.context.snapshot.target.$_
}).Count -eq 0)
$result = @{passed=(@($checks | Where-Object { -not $_.passed }).Count -eq 0);fixture=$fixture.fixture;runFolder=$RunFolder;
    packageManifestSha256=$plan.manifest_hash;previewRuns=$runs;checks=$checks;physicalChecksOutstanding=$true;nativeKitGenerationOutstanding=$true}
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'qualification.json') -Encoding utf8
$result | ConvertTo-Json -Depth 20
if (-not $result.passed) { throw 'Preview qualification failed; inspect the retained evidence.' }
