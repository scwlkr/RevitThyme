param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$report = Join-Path $root 'artifacts\mcp\checks'
New-Item -ItemType Directory -Path $report -Force | Out-Null
$solution = if($Portable){'RevitThyme.Portable.slnf'}else{'RevitThyme.sln'}
& (Join-Path $PSScriptRoot 'export-ribbon-icons.ps1') -Check
dotnet restore (Join-Path $root 'src\RevitThyme.Mcp') --locked-mode --nologo
if ($LASTEXITCODE) { throw 'Pinned MCP dependency restore failed.' }
dotnet restore (Join-Path $root 'tests\Unit') --locked-mode --nologo
if ($LASTEXITCODE) { throw 'Pinned portable compiler dependency restore failed.' }
if (-not $Portable) {
    dotnet restore (Join-Path $root 'src\RevitThyme.Addin') --locked-mode --nologo
    if ($LASTEXITCODE) { throw 'Pinned Roslyn dependency restore failed.' }
}
dotnet build (Join-Path $root $solution) -c Release --nologo -warnaserror
if ($LASTEXITCODE) { throw 'Foundation build failed.' }
dotnet run --project (Join-Path $root 'tests\Unit') -c Release --no-build | Tee-Object -FilePath (Join-Path $report 'unit.txt')
if ($LASTEXITCODE) { throw 'Foundation unit checks failed.' }
dotnet run --project (Join-Path $root 'tests\Mcp') -c Release --no-build | Tee-Object -FilePath (Join-Path $report 'mcp.txt')
if ($LASTEXITCODE) { throw 'MCP protocol and operation checks failed.' }
dotnet run --project (Join-Path $root 'tests\Agent') -c Release --no-build | Tee-Object -FilePath (Join-Path $report 'agent.txt')
if ($LASTEXITCODE) { throw 'Agent protocol and lifecycle checks failed.' }
$assemblyPaths = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter 'RevitAPI*.dll')
if ($assemblyPaths.Count) { throw 'Autodesk binaries were copied into the source/build tree.' }
$scriptPaths = @(Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -File -Filter '*.ps1') + @(Get-ChildItem -LiteralPath (Join-Path $root 'installer') -File -Filter '*.ps1')
foreach ($file in $scriptPaths) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell syntax failed: $($file.FullName): $errors" }
}
$projects = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.csproj'
if ($projects.Count -ne 6 -or @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Include '*.rs','*.tsx','*.py').Count) { throw 'Active product must contain only the six C# toolkit projects.' }
$migration = Join-Path $root 'artifacts\foundation\migration.json'
if (Test-Path -LiteralPath $migration) {
    $record = Get-Content -LiteralPath $migration -Raw | ConvertFrom-Json
    foreach ($entry in $record.preserved) {
        if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $root 'legacy\pre-foundation') $entry.path)).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Legacy preservation failed: $($entry.path)" }
    }
}
@{passed=$true;portable=[bool]$Portable;source_revision=(git -C $root rev-parse HEAD);dirty=[bool](git -C $root status --porcelain);build=$solution;unit='unit.txt';native_evidence='Separate live run required'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'result.json') -Encoding utf8
Write-Output 'Toolkit through Built-in Agent: build, unit/protocol, script syntax, architecture and preservation checks passed.'
