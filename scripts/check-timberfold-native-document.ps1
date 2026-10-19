[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunFolder, [Parameter(Mandatory)][string]$GenerationDirectory, [switch]$AfterUndo, [switch]$CutFiles)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not [IO.Path]::IsPathFullyQualified($GenerationDirectory)) {
    throw 'Use absolute pinned development and generation directories.'
}
$status = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $status.state -ne 'ready' -or $status.pending_request) { throw 'Pinned development source must be ready with no uncertain native request.' }
$receipt = Get-Content -LiteralPath (Join-Path $GenerationDirectory 'native-receipt.json') -Raw | ConvertFrom-Json
foreach ($field in @('processId','processStart','sessionId','documentId','viewId')) {
    if ($status.context.snapshot.target.$field -ne $receipt.afterTarget.$field) { throw "Native qualification target differs: $field" }
}
$assembly = Join-Path $repo 'tests/TimberFold/bin/Debug/net10.0-windows/RevitThyme.TimberFold.Fixtures.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build the independent native fixture qualification assembly first.' }
$evidence = Join-Path $RunFolder ('timberfold-document-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$assemblyLiteral = ConvertTo-Json $assembly -Compress
$hashLiteral = ConvertTo-Json (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant() -Compress
$generationLiteral = ConvertTo-Json $GenerationDirectory -Compress
$reportLiteral = ConvertTo-Json (Join-Path $evidence 'native-report') -Compress
$undoLiteral = if ($AfterUndo) { 'true' } else { 'false' }
$classLiteral = if ($CutFiles) { 'NativeCutQualification' } else { 'NativeKitQualification' }
$methodLiteral = if ($CutFiles) { 'CheckDwg' } else { 'Check' }
$argumentsLiteral = if ($CutFiles) { "uiapp, $generationLiteral, $reportLiteral, cancellationToken" } else {
    "uiapp, $generationLiteral, $reportLiteral, $undoLiteral, cancellationToken"
}
$source = @"
var bytes = System.IO.File.ReadAllBytes($assemblyLiteral);
if (System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != $hashLiteral)
    throw new System.IO.InvalidDataException("Independent qualification assembly changed.");
var assembly = System.Reflection.Assembly.Load(bytes);
try {
    return assembly.GetType("RevitThyme.TimberFold.Fixtures.$classLiteral", true).GetMethod("$methodLiteral")
        .Invoke(null, new object[] { $argumentsLiteral });
} catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
"@
$runner = Join-Path $evidence 'check.csx'
[IO.File]::WriteAllText($runner, $source)
$raw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000 | Out-String
$code = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $evidence 'operation.json'), $raw)
$result = $raw | ConvertFrom-Json
if ($code -ne 0 -or -not $result.execution.value.passed) { throw "Native document qualification failed; inspect $evidence" }
$result.execution.value | ConvertTo-Json -Depth 8
