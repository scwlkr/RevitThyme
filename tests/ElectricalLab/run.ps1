param(
    [ValidateSet('prepare','start','ready','close')][string]$Action,
    [string]$RunFolder,
    [string]$FixtureSource,
    [string]$HandoffSource,
    [string]$ReferencePath
)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence=Join-Path $root 'artifacts\electrical-lifecycle'
function Hash([string]$Path) {
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
}
if($Action -eq 'prepare') {
    foreach($path in @($FixtureSource,$HandoffSource,$ReferencePath)) {
        if(-not[IO.Path]::IsPathFullyQualified($path) -or -not(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Use explicit existing absolute fixture and reference files.' }
    }
    if($FixtureSource -eq $HandoffSource) { throw 'Use a distinct clean handoff fixture.' }
    $reference=Get-Content -LiteralPath $ReferencePath -Raw|ConvertFrom-Json
    if($reference.views.Count -ne 4 -or $reference.counts.Count -lt 4 -or $reference.schedules.Count -ne 3) { throw 'Expected reviewed four-view electrical-lab reference inventory.' }
    New-Item -ItemType Directory -Force $evidence|Out-Null
    $RunFolder=Join-Path $evidence ([Guid]::NewGuid().ToString()); New-Item -ItemType Directory $RunFolder|Out-Null
    $receipt=Get-Content (Join-Path $env:LOCALAPPDATA 'RevitThyme\setup\receipt.json') -Raw|ConvertFrom-Json
    $plan=[ordered]@{folder=$RunFolder;source=$FixtureSource;sourceSha256=(Hash $FixtureSource);bSource=$HandoffSource;bSourceSha256=(Hash $HandoffSource)
        a=(Join-Path $RunFolder 'Electrical-Lab.rvt');b=(Join-Path $RunFolder 'Handoff.rvt');referencePath=(Join-Path $RunFolder 'reference.json')
        referenceSha256=(Hash $ReferencePath);scriptsDirectory=$PSScriptRoot;bundle=$receipt.installed;manifestSha256=(Hash (Join-Path $receipt.installed 'manifest.json'))
        registration=$receipt.registration;registrationSha256=(Hash $receipt.registration)}
    Copy-Item -LiteralPath $FixtureSource -Destination $plan.a; Copy-Item -LiteralPath $HandoffSource -Destination $plan.b
    Copy-Item -LiteralPath $ReferencePath -Destination $plan.referencePath
    if((Hash $plan.a) -ne $plan.sourceSha256 -or (Hash $plan.b) -ne $plan.bSourceSha256 -or
        (Hash $FixtureSource) -ne $plan.sourceSha256 -or (Hash $HandoffSource) -ne $plan.bSourceSha256 -or (Hash $plan.referencePath) -ne $plan.referenceSha256) { throw 'Input changed while copying.' }
    $plan|ConvertTo-Json|Set-Content (Join-Path $RunFolder 'plan.json') -Encoding utf8
    $plan|ConvertTo-Json; return
}
if(-not[IO.Path]::IsPathFullyQualified($RunFolder) -or (Split-Path $RunFolder -Parent) -ne $evidence) { throw 'Use exact owned absolute run folder.' }
$plan=Get-Content (Join-Path $RunFolder 'plan.json') -Raw|ConvertFrom-Json
if($plan.folder -ne $RunFolder -or (Hash $plan.source) -ne $plan.sourceSha256 -or (Hash $plan.bSource) -ne $plan.bSourceSha256 -or
    (Hash $plan.referencePath) -ne $plan.referenceSha256 -or (Hash $plan.registration) -ne $plan.registrationSha256 -or
    (Hash (Join-Path $plan.bundle 'manifest.json')) -ne $plan.manifestSha256) { throw 'Pinned input or installation changed.' }
if($Action -eq 'start') {
    if(Test-Path (Join-Path $RunFolder 'launch.json')) { throw 'Inspect existing launch; never restart solely because observation timed out.' }
    if((Hash $plan.a) -ne $plan.sourceSha256 -or (Hash $plan.b) -ne $plan.bSourceSha256) { throw 'Initial copies changed.' }
    $prior=@(Get-Process Revit -ErrorAction SilentlyContinue|ForEach-Object{@{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()}})
    $sessions=Join-Path $RunFolder 'sessions'; New-Item -ItemType Directory $sessions|Out-Null
    $environment=@{REVITTHYME_TRUSTED_CODE='1';REVITTHYME_FOUNDATION_FIXTURE=$null;REVITTHYME_FOUNDATION_SESSIONS=$sessions;REVITTHYME_AGENT_HOME=(Join-Path $RunFolder 'agent')}
    foreach($key in @('REVITTHYME_DISCOVERY_PROBE','REVITTHYME_LIFECYCLE_PROBE','REVITTHYME_DYNAMIC_TEST_RUN','REVITTHYME_FOUNDATION_TEST_RUN','REVITTHYME_MCP_TEST_RUN','REVITTHYME_UTILITIES_TEST_RUN','REVITTHYME_AGENT_TEST_RUN','REVITTHYME_UPDATE_TEST_RUN','REVITTHYME_RIBBON_TEST_RUN','REVITTHYME_IMAGE_TEST_RUN')) { $environment[$key]=$null }
    $p=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2027\Revit.exe' -ArgumentList '/nosplash' -Environment $environment -WindowStyle Hidden -PassThru
    @{pid=$p.Id;start=$p.StartTime.ToUniversalTime().Ticks.ToString();sessions=$sessions;prior=$prior;trustedOptIn=$true;fixtureGuard=$false}|
        ConvertTo-Json -Depth 5|Set-Content (Join-Path $RunFolder 'launch.json') -Encoding utf8
    Get-Content (Join-Path $RunFolder 'launch.json'); return
}
$launch=Get-Content (Join-Path $RunFolder 'launch.json') -Raw|ConvertFrom-Json
$p=Get-Process -Id $launch.pid
if($p.ProcessName -ne 'Revit' -or $p.StartTime.ToUniversalTime().Ticks.ToString() -ne $launch.start) { throw 'Owned process binding changed.' }
$files=@(Get-ChildItem -LiteralPath $launch.sessions -Filter '*.json')
if($files.Count -ne 1) { throw 'Exact native binding not ready.' }
$binding=Get-Content $files[0].FullName -Raw|ConvertFrom-Json
if($binding.protocolVersion -ne 14 -or $binding.processId -ne $launch.pid -or $binding.processStart -ne $launch.start) { throw 'Native session does not match launch.' }
$context=& (Join-Path $plan.bundle 'cli\revitthyme.exe') inspect --session $files[0].FullName --json|ConvertFrom-Json
if(-not$context.succeeded) { throw 'Native context unavailable; inspect original pending requests, never use a fallback.' }
if($Action -eq 'ready') {
    if($context.snapshot.hasDocument) { throw 'Expected owned Home; preserve unexpected document.' }
    $input=$plan|ConvertTo-Json|ConvertFrom-Json -AsHashtable
    $input.pid=$launch.pid; $input.start=$launch.start; $input.session=$files[0].FullName
    $input|ConvertTo-Json|Set-Content (Join-Path $RunFolder 'input.json') -Encoding utf8
    $context|ConvertTo-Json -Depth 10|Set-Content (Join-Path $RunFolder 'home.json') -Encoding utf8
    @{ready=$true;pid=$p.Id;folder=$RunFolder}|ConvertTo-Json; return
}
$complete=Get-Content (Join-Path $RunFolder 'complete.json') -Raw|ConvertFrom-Json
if(-not$complete.passed -or $context.snapshot.documentPath -ne $plan.a -or $context.snapshot.isModified) { throw 'Require accepted clean active A before normal close.' }
$page=& (Join-Path $plan.bundle 'cli\revitthyme.exe') list-documents --session $files[0].FullName --request-id ([Guid]::NewGuid().ToString()) --json|ConvertFrom-Json
if(-not$page.succeeded -or $page.documents.total -ne 1 -or $page.documents.items[0].path -ne $plan.a) { throw 'Unexpected native document membership; preserve it.' }
if((Hash $plan.a) -ne $complete.savedA -or (Hash $plan.b) -ne $plan.bSourceSha256) { throw 'Final saved fixture hashes differ.' }
$null=$p.CloseMainWindow(); if(-not$p.WaitForExit(15000)) { throw 'Normal close pending; no forced termination.' }
@{closedNormally=$true;pid=$launch.pid;savedA=(Hash $plan.a);sourcePreserved=((Hash $plan.source) -eq $plan.sourceSha256)
    handoffPreserved=((Hash $plan.b) -eq $plan.bSourceSha256);handoffSourcePreserved=((Hash $plan.bSource) -eq $plan.bSourceSha256)}|
    ConvertTo-Json|Set-Content (Join-Path $RunFolder 'cleanup.json') -Encoding utf8
Get-Content (Join-Path $RunFolder 'cleanup.json')
