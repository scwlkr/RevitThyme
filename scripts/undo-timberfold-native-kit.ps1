[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunFolder, [Parameter(Mandatory)][string]$GenerationDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'dev-common.ps1')
# PostCommand is an external queued effect. Query rollback does NOT undo or cancel
# it. A persistent CreateNew claim permits one attempt only, even after a timeout.
function Hash([string]$Path) {
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Read-Bounded([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0 -or $item.Length -gt 33554432 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Invalid or redirected retained evidence: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($GenerationDirectory)) {
    throw 'Use absolute pinned development and host-owned generation directories.'
}
$plan = Get-DevPlan $RunFolder
$launch = Get-DevLaunch $plan
$RunFolder = $plan.folder
$generationId = [Guid]::Empty
if (-not [Guid]::TryParseExact((Split-Path $GenerationDirectory -Leaf),'D',[ref]$generationId)) { throw 'Generation needs its exact run GUID.' }
$owned = Join-Path $env:LOCALAPPDATA ('RevitThyme\TimberFold\runs\' + $generationId.ToString('D'))
if ([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($GenerationDirectory)) -ne $owned -or
    ((Get-Item -LiteralPath $owned).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Use the exact unredirected host-owned generation directory.' }
$GenerationDirectory = $owned
$receiptPath = Join-Path $owned 'native-receipt.json'
$preparedPath = Join-Path $owned 'native-prepared.json'
$sealPath = Join-Path $owned 'generation-inputs.json'
$receipt = Read-Bounded $receiptPath
$prepared = Read-Bounded $preparedPath
$seal = Read-Bounded $sealPath
$receiptHash = Hash $receiptPath; $preparedHash = Hash $preparedPath; $sealHash = Hash $sealPath
if ($receipt.state -ne 'Committed' -or $receipt.runId -ne $generationId.ToString('D') -or
    -not $receipt.sourcePreserved -or -not $receipt.nativeReadbackPassed -or $prepared.schema -ne 2 -or
    $prepared.runId -ne $receipt.runId -or $prepared.state -ne 'Prepared' -or
    $preparedHash -cne $receipt.preparedReceiptSha256 -or $sealHash -cne $receipt.inputSealSha256 -or
    $prepared.inputSealSha256 -cne $sealHash -or $seal.schema -ne 1 -or $seal.runId -ne $receipt.runId -or
    $prepared.previewRunId -ne $seal.previewRunId -or $prepared.sourcePath -ne $plan.fixture -or
    [string]::IsNullOrWhiteSpace($prepared.savedSourceSha256)) { throw 'Committed receipt, prepared record and generation input seal do not agree.' }
$savedHashes = @($plan.fixture_source,$plan.fixture) | ForEach-Object { Hash $_ }
if ($savedHashes[0] -cne $plan.fixture_sha256.ToLowerInvariant() -or $savedHashes[1] -cne $prepared.savedSourceSha256 -or
    $savedHashes[0] -cne $savedHashes[1]) { throw 'Saved original or disposable source file changed.' }
$claimPath = Join-Path $RunFolder ('timberfold-undo-' + $generationId.ToString('D') + '.claim.json')
if (Test-Path -LiteralPath $claimPath) { throw "Undo already reserved/attempted; never retry an ambiguous post. Inspect $claimPath" }
function Pinned-Status {
    $value = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $value.state -ne 'ready' -or $value.pending_request -or -not $value.context.snapshot.isModified -or
        $value.context.snapshot.isWorkshared -or $value.context.snapshot.documentPath -ne $plan.fixture) {
        throw 'Undo requires a ready modified disposable source with no pending request.'
    }
    foreach ($field in @('processId','processStart','sessionId','documentId','viewId')) {
        if ($value.context.snapshot.target.$field -ne $receipt.afterTarget.$field) { throw "Undo target differs from native commit: $field" }
    }
    if ($value.context.snapshot.target.processId -ne $launch.pid -or $value.context.snapshot.target.processStart -ne $launch.start -or
        $value.context.snapshot.target.sessionId -ne $launch.session_id -or $value.context.snapshot.target.documentId -ne $launch.document_id -or
        $value.context.snapshot.viewElementId -ne $prepared.originalViewId) { throw 'Launch identity or original active view changed.' }
    return $value
}
$before = Pinned-Status
$evidence = Join-Path $RunFolder ('timberfold-native-undo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$before | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $evidence 'before-status.json') -Encoding utf8
$auditRaw = & (Join-Path $PSScriptRoot 'check-timberfold-native-document.ps1') -RunFolder $RunFolder -GenerationDirectory $owned | Out-String
[IO.File]::WriteAllText((Join-Path $evidence 'before-audit.json'),$auditRaw)
$audit = $auditRaw | ConvertFrom-Json
if (-not $audit.passed -or $audit.afterUndo) { throw "Full pre-Undo document audit failed: $evidence" }
$posting = Pinned-Status
$posting | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $evidence 'posting-status.json') -Encoding utf8
if ((Hash $receiptPath) -cne $receiptHash -or (Hash $preparedPath) -cne $preparedHash -or (Hash $sealPath) -cne $sealHash -or
    (Hash $plan.fixture) -cne $prepared.savedSourceSha256 -or (Hash $plan.fixture_source) -cne $savedHashes[0]) { throw 'Evidence or saved source changed during the document audit.' }
$target = $posting.context.snapshot.target
$literals = @{}
foreach ($pair in @(@('run',$owned),@('source',$plan.fixture),@('receiptHash',$receiptHash),@('preparedHash',$preparedHash),@('sealHash',$sealHash),
    @('session',$target.sessionId),@('document',$target.documentId),@('view',$target.viewId),@('start',$target.processStart),
    @('addinHash',(Hash (Join-Path $launch.bundle 'RevitThyme.Addin.dll'))))) {
    $literals[$pair[0]] = ConvertTo-Json ([string]$pair[1]) -Compress
}
$source = @"
const string run = $($literals.run);
string Hash(string path) {
 using var stream = new System.IO.FileStream(path,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete);
 return System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
}
var receiptPath = System.IO.Path.Combine(run,"native-receipt.json");
var preparedPath = System.IO.Path.Combine(run,"native-prepared.json");
var sealPath = System.IO.Path.Combine(run,"generation-inputs.json");
if(Hash(receiptPath)!=$($literals.receiptHash)||Hash(preparedPath)!=$($literals.preparedHash)||Hash(sealPath)!=$($literals.sealHash))
 throw new System.InvalidOperationException("Committed Undo evidence changed.");
using var receipt = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(receiptPath));
using var prepared = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(preparedPath));
var r=receipt.RootElement; var p=prepared.RootElement;
var addin=System.AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="RevitThyme.Addin");
if(Hash(addin.Location)!=$($literals.addinHash)) throw new System.InvalidOperationException("Loaded Addin differs from the pinned installation.");
var hostType=addin.GetType("RevitThyme.Addin.FoundationApplication",true);
var host=hostType.GetProperty("Instance").GetValue(null);
var binding=hostType.GetProperty("Binding").GetValue(host);
string Binding(string name)=>System.Convert.ToString(binding.GetType().GetProperty(name).GetValue(binding),System.Globalization.CultureInfo.InvariantCulture);
if(System.Environment.ProcessId!=$($launch.pid)||System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)!=$($literals.start)||
 Binding("ProcessId")!="$($launch.pid)"||Binding("ProcessStart")!=$($literals.start)||Binding("SessionId")!=$($literals.session)||
 (string)hostType.GetMethod("DocumentId").Invoke(host,new object[]{doc})!=$($literals.document)||
 (long)hostType.GetProperty("Revision").GetValue(host)!=$($target.revision)L||uiapp.Application.Documents.Size!=1||doc.IsWorkshared||doc.IsFamilyDocument||!doc.IsModified||
 !System.String.Equals(doc.PathName,$($literals.source),System.StringComparison.OrdinalIgnoreCase)||!doc.Equals(uiapp.ActiveUIDocument.Document)||
 System.Environment.GetEnvironmentVariable("REVITTHYME_TRUSTED_CODE")!="1"||
 !System.String.Equals(System.Environment.GetEnvironmentVariable("REVITTHYME_FOUNDATION_FIXTURE"),doc.PathName,System.StringComparison.OrdinalIgnoreCase))
 throw new System.InvalidOperationException("Exact pinned owned source or current revision changed.");
if(r.GetProperty("state").GetString()!="Committed"||r.GetProperty("runId").GetGuid()!=System.Guid.Parse("$generationId")||
 p.GetProperty("runId").GetGuid()!=r.GetProperty("runId").GetGuid()||r.GetProperty("preparedReceiptSha256").GetString()!=$($literals.preparedHash)||
 r.GetProperty("inputSealSha256").GetString()!=$($literals.sealHash)||p.GetProperty("inputSealSha256").GetString()!=$($literals.sealHash)||
 p.GetProperty("originalViewId").GetInt64()!=doc.ActiveView.Id.Value||doc.ActiveView.UniqueId!=$($literals.view)||
 Hash(doc.PathName)!=p.GetProperty("savedSourceSha256").GetString()) throw new System.InvalidOperationException("Undo source/view/file binding changed.");
var expected=new System.Collections.Generic.HashSet<long>();
foreach(var row in p.GetProperty("beforeCatalog").EnumerateArray().Concat(r.GetProperty("createdElements").EnumerateArray())) {
 cancellationToken.ThrowIfCancellationRequested(); var id=row.GetProperty("id").GetInt64();
 if(!expected.Add(id)||doc.GetElement(new Autodesk.Revit.DB.ElementId(id))?.UniqueId!=row.GetProperty("uniqueId").GetString())
  throw new System.InvalidOperationException("Original or created native identity changed.");
}
using var all=new Autodesk.Revit.DB.FilteredElementCollector(doc);
var actual=all.WherePasses(new Autodesk.Revit.DB.LogicalOrFilter(new Autodesk.Revit.DB.ElementIsElementTypeFilter(),new Autodesk.Revit.DB.ElementIsElementTypeFilter(true))).ToElementIds().Select(id=>id.Value);
if(!expected.SetEquals(actual)) throw new System.InvalidOperationException("Unexpected native inventory; refuse Undo.");
cancellationToken.ThrowIfCancellationRequested();
var command=Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.Undo);
if(!uiapp.CanPostCommand(command)) throw new System.InvalidOperationException("Revit Undo is not postable.");
uiapp.PostCommand(command);
return new { postedNativeUndo=true,generationRunId=r.GetProperty("runId").GetString(),command=command.Name,
 note="Undo queued ONCE beyond query rollback; independently verify restoration. Never repost after an ambiguous result." };
"@
$runner = Join-Path $evidence 'post-undo-once.csx'
[IO.File]::WriteAllText($runner,$source)
$claim = @{schema=1;generationRunId=$generationId.ToString('D');evidence=$evidence;target=$target;preparedSha256=$preparedHash;
    sourceSha256=$prepared.savedSourceSha256;sourceHash=(Hash $runner);externalQueuedEffect='One PostableCommand.Undo beyond query rollback';neverAutomaticallyRetry=$true}
$claimBytes = [Text.Encoding]::UTF8.GetBytes(($claim | ConvertTo-Json -Depth 10))
$claimStream = [IO.FileStream]::new($claimPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $claimStream.Write($claimBytes); $claimStream.Flush($true) } finally { $claimStream.Dispose() }
Write-Verbose "Posting one guarded external native Undo; claim retained at $claimPath. Query rollback does not cancel this command."
try {
    $postRaw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000 | Out-String
    $postCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence 'post-result.json'),$postRaw)
    $posted = $postRaw | ConvertFrom-Json
    if ($postCode -ne 0 -or -not $posted.execution.value.postedNativeUndo -or $posted.execution.value.generationRunId -ne $generationId.ToString('D')) {
        throw 'Undo post result is rejected or ambiguous. The command may have executed; never repost.'
    }
    $afterRaw = & (Join-Path $PSScriptRoot 'check-timberfold-native-document.ps1') -RunFolder $RunFolder -GenerationDirectory $owned -AfterUndo | Out-String
    [IO.File]::WriteAllText((Join-Path $evidence 'after-undo-audit.json'),$afterRaw)
    $after = $afterRaw | ConvertFrom-Json
    if (-not $after.passed -or -not $after.afterUndo -or (Hash $plan.fixture) -cne $prepared.savedSourceSha256 -or
        (Hash $plan.fixture_source) -cne $savedHashes[0]) { throw 'Actual Undo restoration remains unconfirmed; inspect retained evidence without reposting.' }
    $result = @{passed=$true;generationRunId=$generationId.ToString('D');claim=$claimPath;evidence=$evidence;
        postedNativeUndo=$true;externalEffectBeyondQueryRollback=$true;beforeAudit=$audit;afterUndo=$after;physicalChecksOutstanding=$true}
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence 'qualification.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 12
} catch {
    @{passed=$false;error=$_.ToString();generationRunId=$generationId.ToString('D');claim=$claimPath;neverRepost=$true} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'unconfirmed.json') -Encoding utf8
    throw
}
