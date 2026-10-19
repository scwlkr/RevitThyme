[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunFolder, [Parameter(Mandatory)][string]$RetainedRun, [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('full','actions','calibrated')][string]$Mode = 'full')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not [IO.Path]::IsPathFullyQualified($Destination) -or (Test-Path -LiteralPath $Destination)) { throw 'Use a fresh absolute destination.' }
$before = & (Join-Path $repo 'project.cmd') dev-status -RunFolder $RunFolder | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $before.state -ne 'ready' -or $before.pending_request -or $before.context.snapshot.isModified) {
    throw 'Use the ready, unmodified pinned development source with no outstanding request.'
}
$assembly = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\RevitThyme.TimberFold.Fixtures.dll'
$assemblyLiteral = ConvertTo-Json $assembly -Compress
$hashLiteral = ConvertTo-Json (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant() -Compress
$inputLiteral = ConvertTo-Json $RetainedRun -Compress
$outputLiteral = ConvertTo-Json $Destination -Compress
$modeLiteral = ConvertTo-Json $Mode -Compress
$source = @"
var bytes = System.IO.File.ReadAllBytes($assemblyLiteral);
if (System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != $hashLiteral)
    throw new System.IO.InvalidDataException("Native preservation probe assembly changed.");
var assembly = System.Reflection.Assembly.Load(bytes);
try {
    return assembly.GetType("RevitThyme.TimberFold.Fixtures.NativePreservationProbe", true).GetMethod("Check")
        .Invoke(null, new object[] { uiapp, $inputLiteral, $outputLiteral, $modeLiteral, cancellationToken });
} catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
"@
$runners = Join-Path $RunFolder 'native-preservation-probe-runners'
New-Item -ItemType Directory -Force -Path $runners | Out-Null
$runner = Join-Path $runners ([Guid]::NewGuid().ToString('N') + '.csx')
[IO.File]::WriteAllText($runner, $source, [Text.UTF8Encoding]::new($false))
$raw = & (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000 | Out-String
$nativeExit = $LASTEXITCODE
if (Test-Path -LiteralPath $Destination) { [IO.File]::WriteAllText((Join-Path $Destination 'native-operation.json'), $raw) }
$operation = $raw | ConvertFrom-Json
$operation.execution.value | ConvertTo-Json -Depth 10
if ($nativeExit -ne 0 -or -not $operation.execution.value.probeSucceeded) { throw 'Probe preservation/closure incomplete; inspect retained evidence.' }
