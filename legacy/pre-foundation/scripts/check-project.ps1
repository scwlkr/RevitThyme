[CmdletBinding()]
param(
    [string]$LocalConfigPath,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectChecks = New-Object 'System.Collections.Generic.List[object]'
$sourceRecords = New-Object 'System.Collections.Generic.List[object]'

function Add-ProjectCheck {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $projectChecks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
}

function Resolve-ProjectPath {
    param([string]$Value)
    if ([IO.Path]::IsPathRooted($Value)) {
        return [IO.Path]::GetFullPath($Value)
    }
    return [IO.Path]::GetFullPath((Join-Path $projectRoot $Value))
}

function Read-ProjectJson {
    param([string]$Path, [string]$CheckName)
    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        Add-ProjectCheck $CheckName $true 'Valid JSON.'
        return $json
    }
    catch {
        Add-ProjectCheck $CheckName $false $_.Exception.Message
        return $null
    }
}

$requiredFiles = @(
    'README.md', 'PROJECT.md', 'AGENTS.md', 'CHANGELOG.md',
    'docs/ARCHITECTURE.md', 'docs/ROADMAP.md', 'docs/DEPLOYMENT.md',
    'tools/timberfold/README.md', 'suite.json',
    'config/local.example.json', 'src/README.md', 'verification/README.md'
)
foreach ($relativeFile in $requiredFiles) {
    $filePath = Resolve-ProjectPath $relativeFile
    Add-ProjectCheck "Required file: $relativeFile" (Test-Path -LiteralPath $filePath -PathType Leaf) $filePath
}

$suite = Read-ProjectJson (Resolve-ProjectPath 'suite.json') 'Suite metadata JSON'
if ($null -ne $suite) {
    Add-ProjectCheck 'Suite identity' ($suite.id -eq 'revitthyme' -and $suite.name -eq 'RevitThyme') 'Expected product ID revitthyme and display name RevitThyme.'
    Add-ProjectCheck 'Suite schema and version' ($suite.schema_version -eq 1 -and $suite.version -match '^\d+\.\d+\.\d+$') 'Schema 1 and a three-part foundation/product version.'
    Add-ProjectCheck 'At least one registered tool' (@($suite.tools).Count -gt 0) 'Tool registry must identify its manifests.'
}

if ([string]::IsNullOrWhiteSpace($LocalConfigPath)) {
    $userConfigPath = Resolve-ProjectPath 'config/local.json'
    if (Test-Path -LiteralPath $userConfigPath -PathType Leaf) {
        $configPath = $userConfigPath
        $configMode = 'local'
    }
    else {
        $configPath = Resolve-ProjectPath 'config/local.example.json'
        $configMode = 'example'
    }
}
else {
    $configPath = Resolve-ProjectPath $LocalConfigPath
    $configMode = 'explicit'
}
$config = Read-ProjectJson $configPath 'Local configuration JSON'
if ($null -ne $config) {
    Add-ProjectCheck 'Local configuration schema' ($config.schema_version -eq 1) 'Schema 1 is supported.'
}

$registeredIds = @{}
if ($null -ne $suite) {
    foreach ($registeredTool in @($suite.tools)) {
        $toolId = [string]$registeredTool.id
        $unique = -not [string]::IsNullOrWhiteSpace($toolId) -and -not $registeredIds.ContainsKey($toolId)
        Add-ProjectCheck "Unique tool ID: $toolId" $unique 'Each tool requires a nonempty unique ID.'
        if (-not $unique) { continue }
        $registeredIds[$toolId] = $true

        if ([string]::IsNullOrWhiteSpace([string]$registeredTool.manifest)) {
            Add-ProjectCheck "Manifest path: $toolId" $false 'A manifest path is required.'
            continue
        }
        $manifestPath = Resolve-ProjectPath ([string]$registeredTool.manifest)
        $manifest = Read-ProjectJson $manifestPath "Tool manifest JSON: $toolId"
        if ($null -eq $manifest) { continue }
        Add-ProjectCheck "Tool manifest identity: $toolId" ($manifest.schema_version -eq 1 -and $manifest.id -eq $toolId) 'Registry and manifest IDs must match; schema 1 is supported.'

        $operationNames = @{}
        foreach ($operation in @($manifest.operations)) {
            $operationName = [string]$operation.name
            $uniqueOperation = -not [string]::IsNullOrWhiteSpace($operationName) -and -not $operationNames.ContainsKey($operationName)
            Add-ProjectCheck "Unique operation: $operationName" $uniqueOperation 'Operation names are required and unique within a tool.'
            if ($uniqueOperation) { $operationNames[$operationName] = $true }
        }

        if ($null -eq $config) { continue }
        $sourceKey = [string]$manifest.source.config_key
        $sourceProperty = $config.tool_roots.PSObject.Properties[$sourceKey]
        if ($null -eq $sourceProperty -or [string]::IsNullOrWhiteSpace([string]$sourceProperty.Value)) {
            Add-ProjectCheck "Source configuration: $toolId" $false "Missing tool_roots.$sourceKey."
            continue
        }
        $sourceRoot = Resolve-ProjectPath ([string]$sourceProperty.Value)
        Add-ProjectCheck "Source directory: $toolId" (Test-Path -LiteralPath $sourceRoot -PathType Container) $sourceRoot
        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { continue }

        foreach ($sourceDoc in @('PROJECT.md', 'README.md', 'AGENTS.md')) {
            $sourceDocPath = Join-Path $sourceRoot $sourceDoc
            Add-ProjectCheck "Source instructions: $toolId/$sourceDoc" (Test-Path -LiteralPath $sourceDocPath -PathType Leaf) $sourceDocPath
        }
        foreach ($entryPoint in $manifest.entry_points.PSObject.Properties) {
            $entryPath = Join-Path $sourceRoot ([string]$entryPoint.Value)
            Add-ProjectCheck "Source entry: $toolId/$($entryPoint.Name)" (Test-Path -LiteralPath $entryPath -PathType Leaf) $entryPath
        }

        $currentRevision = $null
        $sourceState = 'git_unavailable'
        $gitCommand = Get-Command git -ErrorAction SilentlyContinue
        if ($null -ne $gitCommand) {
            $sourceRepoRoot = & git -C $sourceRoot rev-parse --show-toplevel 2>$null
            if ($LASTEXITCODE -eq 0 -and [IO.Path]::GetFullPath([string]$sourceRepoRoot) -eq $sourceRoot) {
                $currentRevision = [string](& git -C $sourceRoot rev-parse HEAD)
                $sourceChanges = @(& git -C $sourceRoot status --porcelain)
                $sourceState = if ($sourceChanges.Count -eq 0) { 'clean' } else { 'has_local_changes' }
            }
            else {
                $sourceState = 'not_an_independent_git_checkout'
            }
        }
        $sourceRecords.Add([pscustomobject]@{
            tool_id = $toolId
            source_root = $sourceRoot
            reference_commit = $manifest.source.reference_commit
            current_commit = $currentRevision
            reference_matches_current = ($null -ne $currentRevision -and $currentRevision -eq $manifest.source.reference_commit)
            source_state = $sourceState
            integration_status = $manifest.integration_status
        })
    }
}

# Validate local Markdown file links; this check deliberately does not fetch web pages.
$markdownPaths = & git -C $projectRoot ls-files --cached --others --exclude-standard -- '*.md'
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate project Markdown files.' }
$markdownFiles = $markdownPaths | ForEach-Object { Get-Item -LiteralPath (Join-Path $projectRoot $_) }
foreach ($markdownFile in $markdownFiles) {
    $markdown = Get-Content -LiteralPath $markdownFile.FullName -Raw
    foreach ($link in [regex]::Matches($markdown, '\[[^\]]+\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value.Trim('<', '>')
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('#')) { continue }
        $fileTarget = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($fileTarget)) { continue }
        $linkedPath = [IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $fileTarget))
        Add-ProjectCheck "Markdown link: $($markdownFile.Name) -> $target" (Test-Path -LiteralPath $linkedPath) $linkedPath
    }
}

$failedChecks = @($projectChecks | Where-Object { -not $_.passed })
$report = [pscustomobject]@{
    schema_version = 1
    product = 'RevitThyme'
    checked_at_utc = [DateTime]::UtcNow.ToString('o')
    scope = 'read_only_project_structure_and_external_source_references'
    live_revit_checked = $false
    host_execution_checked = $false
    timberfold_generation_run = $false
    configuration_mode = $configMode
    suite_version = if ($null -ne $suite) { $suite.version } else { $null }
    suite_stage = if ($null -ne $suite) { $suite.stage } else { $null }
    passed = ($failedChecks.Count -eq 0)
    check_count = $projectChecks.Count
    checks = @($projectChecks.ToArray())
    sources = @($sourceRecords.ToArray())
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFile = Resolve-ProjectPath $ReportPath
    $reportDirectory = Split-Path -Path $reportFile -Parent
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $reportJson = $report | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($reportFile, $reportJson, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Report: $reportFile"
}

if ($failedChecks.Count -gt 0) {
    foreach ($failedCheck in $failedChecks) {
        Write-Output "FAIL: $($failedCheck.name) - $($failedCheck.detail)"
    }
    Write-Output "Project check failed: $($failedChecks.Count) of $($projectChecks.Count) checks."
    exit 1
}
Write-Output "RevitThyme $($report.suite_version): $($projectChecks.Count) structural checks passed."
foreach ($sourceRecord in $sourceRecords) {
    Write-Output "$($sourceRecord.tool_id): $($sourceRecord.integration_status); source $($sourceRecord.source_state); current commit $($sourceRecord.current_commit)"
    if ($null -ne $sourceRecord.current_commit -and -not $sourceRecord.reference_matches_current) {
        Write-Output "NOTE: source revision has changed since registration; inspect it before integration."
    }
}
Write-Output 'Scope: read-only files and source references. No live Revit call, host execution or TimberFold generation.'
