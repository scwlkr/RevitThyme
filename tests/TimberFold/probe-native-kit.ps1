[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunFolder,
    [Parameter(Mandatory)][string]$CutDirectory,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
foreach ($path in @($RunFolder, $CutDirectory, $Destination)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Use absolute pinned-run, cutting and fresh output paths.' }
}
if (Test-Path -LiteralPath $Destination) { throw 'Evidence destination already exists; no overwrite is permitted.' }
$beforeRaw = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String
$before = $beforeRaw | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or $before.context.snapshot.isModified) {
    throw 'A ready unmodified pinned development source with no pending request is required.'
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'RevitThyme.TimberFold.Fixtures.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Native probe assembly build failed.' }
}
$assembly = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\RevitThyme.TimberFold.Fixtures.dll'
$assemblyLiteral = ConvertTo-Json $assembly -Compress
$hashLiteral = ConvertTo-Json (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant() -Compress
$cutLiteral = ConvertTo-Json $CutDirectory -Compress
$destinationLiteral = ConvertTo-Json $Destination -Compress
$source = @"
// Test-only writes: NEW unsaved documents and the fresh evidence directory only.
var bytes = System.IO.File.ReadAllBytes($assemblyLiteral);
if (System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != $hashLiteral)
    throw new System.IO.InvalidDataException("Native kit probe assembly changed.");
var assembly = System.Reflection.Assembly.Load(bytes);
try {
    return assembly.GetType("RevitThyme.TimberFold.Fixtures.NativeKitProbe", true).GetMethod("Check")
        .Invoke(null, new object[] { uiapp, $cutLiteral, $destinationLiteral, cancellationToken });
} catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
"@
$runnerFolder = Join-Path $RunFolder 'timberfold-kit-probe-runners'
New-Item -ItemType Directory -Force -Path $runnerFolder | Out-Null
$runner = Join-Path $runnerFolder ([Guid]::NewGuid().ToString('N') + '.csx')
[IO.File]::WriteAllText($runner, $source, [Text.UTF8Encoding]::new($false))
$raw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000 | Out-String
$nativeExit = $LASTEXITCODE
if (Test-Path -LiteralPath $Destination -PathType Container) {
    [IO.File]::WriteAllText((Join-Path $Destination 'native-operation.json'), $raw)
    [IO.File]::WriteAllText((Join-Path $Destination 'before-context.json'), $beforeRaw)
    $afterRaw = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String
    $afterExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $Destination 'after-context.json'), $afterRaw)
}
$raw
$operation = $raw | ConvertFrom-Json
if ($nativeExit -ne 0 -or -not $operation.execution.value.passed) {
    throw 'Probe failed or is unconfirmed. Inspect its retained request and evidence before another invocation.'
}
$after = $afterRaw | ConvertFrom-Json
$identityFields = @('processId','processStart','sessionId','documentId','viewId')
if ($afterExit -ne 0 -or $after.state -ne 'ready' -or $after.pending_request -or $after.context.snapshot.isModified -or
    @($identityFields | Where-Object { $before.context.snapshot.target.$_ -ne $after.context.snapshot.target.$_ }).Count -ne 0) {
    throw 'Pinned source context was not restored; inspect the retained request before further work.'
}
$report = Join-Path $Destination 'native-kit-probe.json'
if ($operation.execution.value.reportPath -ne $report -or
    (Get-FileHash -LiteralPath $report).Hash.ToLowerInvariant() -ne $operation.execution.value.reportSha256) {
    throw 'Retained native report does not match the completed query receipt.'
}
