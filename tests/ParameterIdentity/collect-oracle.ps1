param(
    [Parameter(Mandatory)][string]$RunFolder,
    [Parameter(Mandatory)][ValidateSet('before','after-inspection','after-edit','after-undo')][string]$Stage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$folder = [IO.Path]::GetFullPath($RunFolder)
$plan = Get-Content -LiteralPath (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder) { throw 'Exact prepared run required.' }
$info = Get-Content -LiteralPath (Join-Path $folder 'fixture-info.json') -Raw | ConvertFrom-Json
if ($info.execution) { $info = $info.execution.value }
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'oracle.csx') -Raw
$all = @(); $pages = @(); $offset = 0; $baseline = $null
do {
    $source = Join-Path $folder ('oracle-'+$Stage+'-'+$offset+'.csx')
    $resultPath = Join-Path $folder ('oracle-'+$Stage+'-'+$offset+'.json')
    $template.Replace('ELEMENT_UNIQUE_ID',$info.elementUniqueId).Replace('const int offset = 0; // OFFSET',('const int offset = '+$offset+';')) |
        Set-Content -LiteralPath $source -Encoding utf8
    & (Join-Path $root 'project.cmd') dev-query -RunFolder $folder -Source $source > $resultPath
    if ($LASTEXITCODE) { throw "Native oracle failed; retain $resultPath and recover its request if uncertain." }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.succeeded -or $result.snapshot.documentPath -ne $plan.fixture) { throw 'Oracle target/result changed.' }
    $value = $result.execution.value
    if ($baseline -and ($baseline.nativeRawDigest -ne $value.nativeRawDigest -or $baseline.elementCount -ne $value.elementCount)) {
        throw 'Native fixture changed between oracle pages.'
    }
    if (-not $baseline) { $baseline = $value }
    $all += $value.parameters; $pages += $resultPath
    if ($null -ne $value.nextOffset -and $value.nextOffset -le $offset) { throw 'Oracle did not advance.' }
    $offset = $value.nextOffset
} while ($null -ne $offset)
if ($all.Count -ne $baseline.parameterCount) { throw 'Incomplete oracle.' }
$stream = [IO.File]::Open($plan.fixture,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
try { $savedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
finally { $stream.Dispose() }
$baseline.parameters = $all
$baseline | Add-Member -NotePropertyName fixtureFileSha256 -NotePropertyValue $savedHash
$baseline | Add-Member -NotePropertyName snapshot -NotePropertyValue $result.snapshot
$baseline | Add-Member -NotePropertyName pages -NotePropertyValue $pages
$destination = Join-Path $folder ('oracle-'+$Stage+'.json')
$baseline | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $destination -Encoding utf8
[pscustomobject]@{stage=$Stage;parameters=$all.Count;raw_digest=$baseline.nativeRawDigest;element_count=$baseline.elementCount;
    revision=$result.snapshot.target.revision;modified=$result.snapshot.isModified;file_hash=$savedHash;evidence=$destination} | ConvertTo-Json
