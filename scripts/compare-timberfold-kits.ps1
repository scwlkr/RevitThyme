[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FirstDirectory,
    [Parameter(Mandatory)][string]$SecondDirectory,
    [Parameter(Mandatory)][string]$FirstNativeReadback,
    [Parameter(Mandatory)][string]$SecondNativeReadback,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($FirstDirectory,$SecondDirectory,$FirstNativeReadback,$SecondNativeReadback,$ReportPath)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Use absolute kit, independent native readback and fresh report paths.' }
}
$FirstDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($FirstDirectory))
$SecondDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($SecondDirectory))
if ($FirstDirectory -eq $SecondDirectory -or (Test-Path -LiteralPath $ReportPath)) { throw 'Compare distinct runs and preserve previous evidence.' }
foreach ($directory in @($FirstDirectory,$SecondDirectory)) {
    if ([IO.Path]::GetFullPath($ReportPath).StartsWith($directory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Write comparison evidence outside both immutable kit directories.'
    }
}
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Json([string]$Path) {
    if ((Get-Item -LiteralPath $Path).Length -gt 33554432) { throw "Evidence exceeds 32MiB: $Path" }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}
function Sort-Ordinal([string[]]$Values) {
    [string[]]$ordered = @($Values)
    [Array]::Sort($ordered,[StringComparer]::Ordinal)
    return $ordered
}
function Inventory([string]$Root) {
    $files = [Collections.Generic.List[string]]::new()
    $folders = [Collections.Generic.Stack[string]]::new(); $folders.Push($Root)
    while ($folders.Count) {
        $folder = $folders.Pop()
        if (([IO.File]::GetAttributes($folder) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Redirected evidence directory.' }
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($folder)) {
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Redirected kit evidence.' }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $folders.Push($entry); continue }
            $relative = [IO.Path]::GetRelativePath($Root,$entry).Replace('\','/')
            if ($relative -notin @('kit.json','run.json','diagnostics.json','run-timings.json')) { $files.Add($relative) }
            if ($files.Count -gt 4096) { throw 'Kit inventory exceeds the retained evidence limit.' }
        }
    }
    return @(Sort-Ordinal ($files.ToArray()))
}
function Read-Kit([string]$Directory, [string]$NativeReadback) {
    $kit = Read-Json (Join-Path $Directory 'kit.json')
    $status = Read-Json (Join-Path $Directory 'run.json')
    if ($kit.schema -notin @(1,2) -or $kit.state -ne 'DigitalKitVerified' -or $status.state -ne 'KitReady' -or
        $status.runId -ne $kit.runId -or -not $status.nativeCommitted -or $kit.manufacturingReady -or
        -not $kit.physicalChecksOutstanding -or -not $kit.nativeReadbackPassed -or $kit.files.Count -eq 0) {
        throw 'Both runs must retain complete digitally verified native kits.'
    }
    $names = @($kit.files | ForEach-Object { $_.name })
    if ([Collections.Generic.HashSet[string]]::new([string[]]$names,[StringComparer]::OrdinalIgnoreCase).Count -ne $names.Count -or
        ($names -join "`n") -cne ((Inventory $Directory) -join "`n")) { throw 'Kit inventory differs from its final manifest.' }
    foreach ($file in $kit.files) {
        if ($file.name -match '[\\:]' -or $file.name -match '(^|/)\.{0,2}(/|$)' -or [IO.Path]::IsPathRooted($file.name)) { throw 'Invalid retained evidence path.' }
        $path = Join-Path $Directory $file.name
        if ($file.bytes -le 0 -or $file.bytes -gt 268435456 -or (Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Hash $path) -cne $file.sha256) {
            throw "Retained evidence changed: $path"
        }
    }
    if ($kit.schema -ge 2) {
        if ($names -cnotcontains 'producer.json') { throw 'Digital kit producer identity is missing.' }
        $producer = Read-Json (Join-Path $Directory 'producer.json')
        $producerCreated = [DateTimeOffset]::MinValue
        if ($producer.schema -ne 1 -or [string]::IsNullOrWhiteSpace($producer.productVersion) -or
            $producer.coreSha256 -cnotmatch '^[0-9a-f]{64}$' -or $producer.protocol -le 0 -or
            -not [DateTimeOffset]::TryParse($producer.createdUtc,[ref]$producerCreated)) { throw 'Invalid retained producer identity.' }
    }
    $native = Read-Json $NativeReadback
    $dwgs = @($names | Where-Object { $_ -like 'native-cuts/*.dwg' })
    if (-not $native.passed -or -not $native.sourcePreserved -or -not $native.physicalChecksOutstanding -or
        [IO.Path]::GetFullPath($native.runDirectory) -ne $Directory -or $native.files.Count -ne $dwgs.Count -or
        ((Sort-Ordinal @($native.files | ForEach-Object { 'native-cuts/' + $_.file })) -join "`n") -cne ($dwgs -join "`n")) {
        throw 'Independent native consumer report does not cover this complete DWG inventory.'
    }
    foreach ($file in $native.files) {
        $retained = @($kit.files | Where-Object { $_.name -ceq ('native-cuts/' + $file.file) })
        if ($retained.Count -ne 1 -or -not $file.passed -or $file.sha256 -cne $retained[0].sha256 -or $file.bytes -ne $retained[0].bytes -or
            $file.expectedClosedContours -le 0 -or $file.expectedClosedContours -ne $file.nativeClosedContours -or
            $file.contourToleranceInches -ne 0.00001 -or $file.layers.Count -ne 1 -or $file.layers[0] -cne 'CUT' -or
            $file.importUnits -cne 'Default: consume saved CAD units') { throw 'Independent native readback proof is incomplete or refers to different file bytes.' }
    }
    return @{kit=$kit;names=$names;request=(Read-Json (Join-Path $Directory 'request.json'));nativeReportSha256=(Hash $NativeReadback)}
}
$first = Read-Kit $FirstDirectory $FirstNativeReadback
$second = Read-Kit $SecondDirectory $SecondNativeReadback
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Passed) { $checks.Add(@{name=$Name;passed=$Passed}) }
Check 'same original source fingerprint' ($first.kit.sourceFingerprint -ceq $second.kit.sourceFingerprint)
Check 'same retained profile and explicit scope' ((@($first.request.profile,$first.request.scope) | ConvertTo-Json -Depth 100 -Compress) -ceq
    (@($second.request.profile,$second.request.scope) | ConvertTo-Json -Depth 100 -Compress))
Check 'same complete part and sheet counts' ($first.kit.panelCount -eq $second.kit.panelCount -and $first.kit.sheetCount -eq $second.kit.sheetCount)
$a = Read-Json (Join-Path $FirstDirectory 'snapshot.json'); $b = Read-Json (Join-Path $SecondDirectory 'snapshot.json')
$null = $a.Remove('target'); $null = $b.Remove('target')
Check 'source geometry repeats after excluding only native target context' (($a | ConvertTo-Json -Depth 100 -Compress) -ceq ($b | ConvertTo-Json -Depth 100 -Compress))
$canonical = @('fitted.json','layout.json') + @($first.names | Where-Object { $_ -match '^(cutting|guides|native-reference-inputs)/' })
$other = @('fitted.json','layout.json') + @($second.names | Where-Object { $_ -match '^(cutting|guides|native-reference-inputs)/' })
Check 'same canonical cutting and guide file inventory' (($canonical -join "`n") -ceq ($other -join "`n"))
foreach ($name in $canonical) { Check "canonical bytes repeat: $name" ((Hash (Join-Path $FirstDirectory $name)) -ceq (Hash (Join-Path $SecondDirectory $name))) }
$result = @{passed=(@($checks | Where-Object {-not $_.passed}).Count -eq 0);firstRun=$first.kit.runId;secondRun=$second.kit.runId;
    firstDirectory=$FirstDirectory;secondDirectory=$SecondDirectory;checks=$checks;allRetainedHashesVerified=$true;
    firstNativeReportSha256=$first.nativeReportSha256;secondNativeReportSha256=$second.nativeReportSha256;
    independentDwgReadbackPassed=$true;nativeDwgByteEqualityClaimed=$false;physicalChecksOutstanding=$true}
$bytes = [Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 8))
$stream = [IO.FileStream]::new($ReportPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
$result | ConvertTo-Json -Depth 8
if (-not $result.passed) { throw 'Canonical kit repeatability failed; both original runs and the comparison report remain preserved.' }
