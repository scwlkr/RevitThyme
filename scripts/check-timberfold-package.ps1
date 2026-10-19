param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'installer\common.ps1')
. (Join-Path $root 'installer\mcp-files.ps1')
. (Join-Path $root 'installer\agent-files.ps1')
$run = Join-Path $root ('artifacts\foundation\package-check\'+[Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $run | Out-Null
$checks = [Collections.Generic.List[object]]::new()

function Check([string]$Name, [scriptblock]$Body) {
    & $Body
    $checks.Add(@{name=$Name;passed=$true})
    Write-Output "PASS $Name"
}
function Refused([scriptblock]$Body, [string]$Pattern) {
    $failure = $null
    try { $null = & $Body } catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notmatch $Pattern) { throw "Expected refusal $Pattern; got: $failure" }
}
function New-PackageFixture([string]$Version, [switch]$TimberFold) {
    $folder = Join-Path $run ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $folder | Out-Null
    # Existing complete product plus the stage-one inspection guide; no native geometry dependencies.
    $files = @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','cli/revitthyme.exe','cli/revitthyme.dll',
        'cli/revitthyme.deps.json','cli/revitthyme.runtimeconfig.json','cli/RevitThyme.Core.dll','install.ps1','uninstall.ps1',
        'common.ps1','host-path.ps1','LICENSE','README.md','mcp-files.ps1','THIRD-PARTY-NOTICES.md','MCP.md',
        'notices/MCP-LICENSE.txt','notices/DOTNET-LICENSE.txt','NATIVE-UTILITIES.md','RevitThyme.Addin.deps.json',
        'Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','DYNAMIC-EXECUTION.md','prerequisites.ps1',
        'setup-entry.ps1','global-command.ps1','cli/rthyme.exe')
    $files += @(Get-McpPackageFiles | ForEach-Object { 'mcp/'+$_ })
    $files += Get-AgentPackageFiles
    if ($TimberFold) { $files += 'TIMBERFOLD.md' }
    $hashes = @{}
    foreach ($name in $files) {
        $path = Join-Path $folder $name
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'Offline package validator fixture; not executable.')
        $hashes[$name] = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    }
    @{version=$Version;product_version='0.10.0';revit_year='2027';supported_revit_years=@('2027');codex_version='0.162.1';files=$hashes} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
    return $folder
}

Check 'TimberFold inspection candidate accepts prior product contents plus its guide' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    $manifest = Assert-FoundationPackage $candidate
    if ($manifest.version -ne '0.10.0-timberfold.1') { throw 'Wrong candidate accepted.' }
}
Check 'candidate requires TimberFold guide' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1'
    Refused { Assert-FoundationPackage $candidate } 'allowlist'
}
Check 'modified TimberFold guide is refused' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    [IO.File]::WriteAllText((Join-Path $candidate 'TIMBERFOLD.md'), 'Changed fixture.')
    Refused { Assert-FoundationPackage $candidate } 'modified'
}
Check 'RevitAPIIFC remains excluded from candidate' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    [IO.File]::WriteAllText((Join-Path $candidate 'RevitAPIIFC.dll'), 'Forbidden proprietary fixture.')
    Refused { Assert-FoundationPackage $candidate } 'Unexpected or missing'
}
Check 'previous identity package remains valid for recovery without TimberFold guide' {
    $previous = New-PackageFixture '0.9.3-identity'
    $manifest = Assert-FoundationPackage $previous
    if ($manifest.version -ne '0.9.3-identity') { throw 'Previous package was not accepted.' }
}
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output "Passed $($checks.Count) package validator checks in $run; no installation or native execution."
