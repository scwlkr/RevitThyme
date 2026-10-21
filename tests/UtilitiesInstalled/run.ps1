param(
    [Parameter(Mandatory)][ValidateSet('start','ready','check','close')][string]$Action,
    [string]$FixtureSource, [string]$RunFolder
)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Hash([string]$Path) {
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
}
function Save([string]$Name,$Value) { $Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $RunFolder $Name) -Encoding utf8 }
if($Action -eq 'start') {
    if(-not[IO.Path]::IsPathFullyQualified($FixtureSource) -or [IO.Path]::GetExtension($FixtureSource) -ne '.rvt') { throw 'Supply an absolute closed disposable utility fixture RVT.' }
    $FixtureSource=(Resolve-Path -LiteralPath $FixtureSource).Path
    $receipt=Get-Content (Join-Path $env:LOCALAPPDATA 'RevitThyme\setup\receipt.json') -Raw|ConvertFrom-Json
    $RunFolder=Join-Path $root ('artifacts\utilities-installed\'+[Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $RunFolder|Out-Null
    $fixture=Join-Path $RunFolder 'Fixture.rvt'; $sourceHash=Hash $FixtureSource
    Copy-Item -LiteralPath $FixtureSource -Destination $fixture
    if((Hash $fixture) -ne $sourceHash -or (Hash $FixtureSource) -ne $sourceHash) { throw 'Source changed during copy; preserve this attempt.' }
    $sessions=Join-Path $RunFolder 'sessions'; New-Item -ItemType Directory -Path $sessions|Out-Null
    $environment=@{}; Get-ChildItem Env:REVITTHYME*|ForEach-Object{$environment[$_.Name]=$null}
    $environment.REVITTHYME_FOUNDATION_SESSIONS=$sessions
    $environment.REVITTHYME_AGENT_HOME=Join-Path $RunFolder 'agent'
    $launch=@{folder=$RunFolder;fixture=$fixture;original=$FixtureSource;fixtureSha256=$sourceHash;bundle=$receipt.installed;
        manifestSha256=(Hash (Join-Path $receipt.installed 'manifest.json'));sessions=$sessions;registration=$receipt.registration;
        registrationSha256=(Hash $receipt.registration);trustedCodeEnabled=$false;testLoaderUsed=$false;
        prior=@(Get-Process Revit -ErrorAction SilentlyContinue|ForEach-Object{@{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()}})}
    Save 'plan.json' $launch
    $process=Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2027\Revit.exe' -ArgumentList @('/nosplash',('"'+$fixture+'"')) -Environment $environment -WindowStyle Hidden -PassThru
    $launch.pid=$process.Id; $launch.start=$process.StartTime.ToUniversalTime().Ticks.ToString(); Save 'launch.json' $launch
    @{folder=$RunFolder;pid=$process.Id;state='starting'}|ConvertTo-Json; return
}
if(-not[IO.Path]::IsPathFullyQualified($RunFolder) -or (Split-Path $RunFolder -Parent) -ne (Join-Path $root 'artifacts\utilities-installed')) { throw 'Use the exact absolute owned run folder.' }
$launch=Get-Content (Join-Path $RunFolder 'launch.json') -Raw|ConvertFrom-Json
if($launch.folder -ne $RunFolder -or (Hash $launch.registration) -ne $launch.registrationSha256 -or (Hash $launch.original) -ne $launch.fixtureSha256) { throw 'Pinned registration or saved source changed; inspect without replay.' }
if($Action -eq 'close' -and (Test-Path -LiteralPath (Join-Path $RunFolder 'pending.json'))) { throw 'Recover and inspect the original pending request before closing its native session; preserve the retained outcome history.' }
$process=Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
if($process -and ($process.ProcessName -ne 'Revit' -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne $launch.start)) { throw 'PID was reused; do not target it.' }
if($Action -eq 'close' -and -not$process) {
    if(-not(Test-Path -LiteralPath (Join-Path $RunFolder 'close-request.json'))) { throw 'Process exited without a recorded normal close request; inspect its actual outcome.' }
    if((Hash $launch.fixture) -ne $launch.fixtureSha256) { throw 'Saved fixture changed; preserve it and investigate.' }
    foreach($prior in $launch.prior) {
        $p=Get-Process -Id $prior.pid -ErrorAction SilentlyContinue
        if(-not$p -or $p.StartTime.ToUniversalTime().Ticks.ToString() -ne $prior.start) { throw 'A pre-existing Revit process changed; record it separately.' }
    }
    Save 'cleanup.json' @{closedNormally=$true;sourcePreserved=$true;fixturePreserved=$true;registrationPreserved=$true;priorProcessesPreserved=$true;pid=$launch.pid}
    Get-Content -LiteralPath (Join-Path $RunFolder 'cleanup.json'); return
}
if(-not$process) { throw 'Owned process exited; retain evidence rather than relaunching this run.' }
if($Action -eq 'close' -and (Test-Path -LiteralPath (Join-Path $RunFolder 'close-request.json'))) {
    @{closePending=$true;pid=$process.Id;message='Original normal close request is still outstanding; inspect the owned window or wait for this process, without another close request.'}|ConvertTo-Json; return
}
$files=@(Get-ChildItem -LiteralPath $launch.sessions -Filter '*.json')
if($files.Count -ne 1) { throw 'Wait for the exact binding; do not relaunch on an observation timeout.' }
$binding=Get-Content -LiteralPath $files[0].FullName -Raw|ConvertFrom-Json
if($binding.processId -ne $launch.pid -or $binding.processStart -ne $launch.start -or $binding.protocolVersion -ne 14) { throw 'Native session binding mismatch.' }
$context=& (Join-Path $launch.bundle 'cli\revitthyme.exe') inspect --session $files[0].FullName --json|ConvertFrom-Json
if(-not$context.succeeded -or $context.snapshot.documentPath -ne $launch.fixture -or $context.snapshot.isWorkshared) { throw 'Expected exact disposable nonworkshared fixture; no model fallback.' }
if((Hash $launch.fixture) -ne $launch.fixtureSha256) { throw 'Saved fixture changed; preserve it and investigate.' }
if($Action -eq 'ready') {
    if($context.snapshot.isModified) { throw 'Initial fixture must be unmodified.' }
    $input=$launch|ConvertTo-Json -Depth 8|ConvertFrom-Json -AsHashtable; $input.session=$files[0].FullName
    Save 'installed-input.json' $input; Save 'ready-context.json' $context
    @{ready=$true;folder=$RunFolder;pid=$process.Id}|ConvertTo-Json; return
}
if($Action -eq 'check') {
    dotnet run --project (Join-Path $root 'tests\Mcp\RevitThyme.McpTests.csproj') -c Release -- --utilities-installed $RunFolder
    if($LASTEXITCODE) { throw 'Installed utility qualification failed; retain this process and its evidence.' }; return
}
Save 'close-request.json' @{pid=$process.Id;start=$launch.start;activeModified=$context.snapshot.isModified;utc=[DateTime]::UtcNow}
$requested=$process.CloseMainWindow()
@{closeRequested=$requested;pid=$process.Id;next='Resolve only the owned disposable save prompt, then call close again to verify exit and preservation.'}|ConvertTo-Json
