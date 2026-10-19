[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunFolder,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)]
    [ValidateSet('gable','irregular','multistorey','flat','curved-wall','slanted-wall','oversized-wall',
        'ambiguous-glazing','thin-bar','arc-roof','phase-scope','performance-100')]
    [string]$Fixture,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not [IO.Path]::IsPathFullyQualified($Destination) -or (Test-Path -LiteralPath $Destination)) {
    throw 'Destination must be a fresh absolute directory. Existing results are never replaced.'
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or -not (Test-Path -LiteralPath $RunFolder -PathType Container)) {
    throw 'Supply the absolute folder of the verified pinned development run.'
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'RevitThyme.TimberFold.Fixtures.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Fixture assembly failed to build.' }
}
$assembly = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\RevitThyme.TimberFold.Fixtures.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build the fixture assembly first.' }
$assemblyLiteral = ConvertTo-Json -InputObject $assembly -Compress
$destinationLiteral = ConvertTo-Json -InputObject $Destination -Compress
$fixtureLiteral = ConvertTo-Json -InputObject $Fixture -Compress
$assemblyHashLiteral = ConvertTo-Json -InputObject (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant() -Compress
$source = @"
// Test-only side effects are limited to NEW family/project documents and this fresh directory.
// The rollback query protects the pinned baseline; saved new files are retained separately.
var assemblyBytes = System.IO.File.ReadAllBytes($assemblyLiteral);
var assemblyHash = System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(assemblyBytes));
if (assemblyHash != $assemblyHashLiteral) throw new System.IO.InvalidDataException("Fixture assembly changed after runner preparation.");
var assembly = System.Reflection.Assembly.Load(assemblyBytes);
var builder = assembly.GetType("RevitThyme.TimberFold.Fixtures.OriginalFixtureBuilder", true);
try {
    return builder.GetMethod("Build").Invoke(null, new object[] { uiapp, $destinationLiteral, $fixtureLiteral, cancellationToken, $assemblyLiteral, assemblyHash });
} catch (System.Reflection.TargetInvocationException e) {
    throw e.InnerException ?? e;
}
"@
$runnerFolder = Join-Path $RunFolder 'timberfold-fixture-runners'
New-Item -ItemType Directory -Force -Path $runnerFolder | Out-Null
$runner = Join-Path $runnerFolder ($Fixture + '-' + [Guid]::NewGuid().ToString('N') + '.csx')
[IO.File]::WriteAllText($runner, $source, [Text.UTF8Encoding]::new($false))
if ((Get-Item -LiteralPath $runner).Length -gt 24576) { throw 'Fixture runner exceeds trusted execution source limit.' }
& (Join-Path $repo 'project.cmd') dev-query -RunFolder $RunFolder -Source $runner -BudgetMilliseconds 30000
if ($LASTEXITCODE -ne 0) {
    throw 'Inspect the retained development request outcome and fixture-failed.json before retrying in a new directory.'
}
