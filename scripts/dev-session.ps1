param(
    [Parameter(Mandatory)][ValidateSet('prepare','start','status','query','modify','outcome','mcp','stop')][string]$Action,
    [string]$RunFolder, [string]$FixtureSource, [string]$PackageDirectory, [string]$SigningConfigPath,
    [string]$Source, [Guid]$RequestId=[Guid]::Empty, [ValidateRange(100,30000)][int]$BudgetMilliseconds=10000
)
. (Join-Path $PSScriptRoot 'dev-common.ps1')
if (-not $SigningConfigPath) {
    $SigningConfigPath = Join-Path (Split-Path (Get-DevSettingsPath) -Parent) 'signing.json'
}
if ($Action -eq 'prepare' -or ($Action -eq 'start' -and -not $RunFolder)) {
    if ($Action -eq 'start' -and $PackageDirectory) { throw 'dev-start uses the registered installed host. Use dev-prepare for a staged package.' }
    $settingsPath = Get-DevSettingsPath
    if (-not $FixtureSource -and (Test-Path -LiteralPath $settingsPath)) {
        $FixtureSource = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).fixture_source
    }
    if (-not $FixtureSource -or -not [IO.Path]::IsPathFullyQualified($FixtureSource) -or [IO.Path]::GetExtension($FixtureSource) -ne '.rvt') {
        throw 'Supply -FixtureSource with an absolute path to a known disposable, nonworkshared baseline RVT.'
    }
    $FixtureSource = Assert-NativeHostPath $FixtureSource
    if (-not (Test-Path -LiteralPath $FixtureSource -PathType Leaf)) { throw 'Fixture source is missing.' }
    $bundle = if ($PackageDirectory) { Assert-NativeHostPath $PackageDirectory } else { (Get-DevInstalled).bundle }
    $null = Assert-FoundationPackage $bundle
    $signed = Assert-DevSigned $bundle $SigningConfigPath
    $RunFolder = Join-Path $script:DevRuns ([Guid]::NewGuid().ToString())
    $null = Assert-NativeHostPath $RunFolder
    New-Item -ItemType Directory -Path $RunFolder | Out-Null
    foreach ($name in @('sessions','results','agent')) { New-Item -ItemType Directory -Path (Join-Path $RunFolder $name) | Out-Null }
    $sourceHash = (Get-FileHash -LiteralPath $FixtureSource).Hash
    $fixture = Join-Path $RunFolder 'Fixture.rvt'
    Copy-Item -LiteralPath $FixtureSource -Destination $fixture
    if ((Get-FileHash -LiteralPath $fixture).Hash -ne $sourceHash -or (Get-FileHash -LiteralPath $FixtureSource).Hash -ne $sourceHash) { throw 'Fixture source changed during copy. Preserve this run and prepare another.' }
    $plan = @{schema=1;folder=$RunFolder;fixture=$fixture;fixture_source=$FixtureSource;fixture_sha256=$sourceHash;bundle=$bundle;
        manifest_hash=(Get-FileHash -LiteralPath (Join-Path $bundle 'manifest.json')).Hash;signing_config=$SigningConfigPath;signer=$signed.thumbprint;
        source_revision=(git -C $script:DevProject rev-parse HEAD);prepared_utc=[DateTime]::UtcNow.ToString('o')}
    Write-DevJson (Join-Path $RunFolder 'plan.json') $plan
    New-Item -ItemType Directory -Path (Split-Path $settingsPath -Parent) -Force | Out-Null
    Write-DevJson $settingsPath @{fixture_source=$FixtureSource}
    if ($Action -eq 'prepare') { $plan | ConvertTo-Json -Depth 5; return }
}
$plan = Get-DevPlan $RunFolder
$lock = Get-DevLock $plan
try {
    if ($Action -eq 'start') {
        if (Test-Path -LiteralPath (Join-Path $plan.folder 'launch.json')) { throw 'This run has already launched. Prepare a fresh run or inspect it with dev-status.' }
        if ((Get-FileHash -LiteralPath $plan.fixture).Hash -ne $plan.fixture_sha256) { throw 'Prepared fixture changed before launch.' }
        $installed = Get-DevInstalled
        if ($installed.manifest_hash -ne $plan.manifest_hash) { throw 'Install the prepared signed base package before launching this run, or prepare a fresh run for the installed host.' }
        $signed = Assert-DevSigned $installed.bundle $plan.signing_config
        if ($signed.thumbprint -ne $plan.signer) { throw 'Development signing identity changed.' }
        $revit = Assert-NativeHostPath 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
        $environment = @{REVITTHYME_TRUSTED_CODE='1';REVITTHYME_FOUNDATION_FIXTURE=$plan.fixture;
            REVITTHYME_FOUNDATION_SESSIONS=(Join-Path $plan.folder 'sessions');REVITTHYME_AGENT_HOME=(Join-Path $plan.folder 'agent')}
        # Remove inherited qualification flags only in the child, so automated test loaders remain dormant.
        foreach ($name in @('REVITTHYME_DYNAMIC_TEST_RUN','REVITTHYME_FOUNDATION_TEST_RUN','REVITTHYME_MCP_TEST_RUN','REVITTHYME_UTILITIES_TEST_RUN','REVITTHYME_AGENT_TEST_RUN','REVITTHYME_UPDATE_TEST_RUN','REVITTHYME_RIBBON_TEST_RUN')) { $environment[$name]=$null }
        $launch = @{folder=$plan.folder;fixture=$plan.fixture;bundle=$installed.bundle;manifest_hash=$installed.manifest_hash;revit=$revit;
            cli_hash=(Get-FileHash -LiteralPath (Join-Path $installed.bundle 'cli\revitthyme.exe')).Hash;
            mcp_hash=(Get-FileHash -LiteralPath (Join-Path $installed.bundle 'mcp\revitthyme-mcp.exe')).Hash;pid=0;start='';session_id='';document_id='';
            existing_processes=@(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { @{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()} });
            registration=$installed.registration;registration_hash=$installed.registration_hash}
        # Reserving launch.json makes an interrupted/failed launch non-repeatable; prepare a new run instead.
        Write-DevJson (Join-Path $plan.folder 'launch.json') $launch
        $process = Start-Process -FilePath $revit -ArgumentList @('/nosplash',('"'+$plan.fixture+'"')) -Environment $environment -PassThru -WindowStyle Normal
        $launch.pid=$process.Id; $launch.start=$process.StartTime.ToUniversalTime().Ticks.ToString()
        Write-DevJson (Join-Path $plan.folder 'launch.json') $launch
        @{state='starting';run_folder=$plan.folder;pid=$launch.pid;next='Use dev-status -RunFolder with this exact run folder after Revit opens.'} | ConvertTo-Json
        return
    }
    $launch = Get-DevLaunch $plan
    $process = Get-DevProcess $launch
    if (-not $process) { @{state='exited';run_folder=$plan.folder;pid=$launch.pid} | ConvertTo-Json; return }
    if ($Action -eq 'stop') {
        # Ask this exact process to close; Revit retains control of unsaved-model prompts.
        $requested = $process.CloseMainWindow()
        @{state='close_requested';pid=$process.Id;requested=$requested;message='Complete any save/discard prompt in the development window; then use dev-status. No process was killed.'} | ConvertTo-Json
        return
    }
    if ((Get-FileHash -LiteralPath $launch.registration).Hash -ne $launch.registration_hash) { throw 'Registered host changed during this development run; inspect and prepare a fresh run.' }
    $binding = Get-DevBinding $plan $launch
    if ($Action -eq 'mcp') {
        $mcp = Join-Path $launch.bundle 'mcp\revitthyme-mcp.exe'
        if ((Get-FileHash -LiteralPath $mcp).Hash -ne $launch.mcp_hash) { throw 'Pinned MCP executable changed.' }
        $lock.Dispose(); $lock=$null
        # The server's --session option refuses switching to another process.
        & $mcp --session $binding --sessions-directory (Join-Path $plan.folder 'sessions')
        exit $LASTEXITCODE
    }
    if ($Action -eq 'status') {
        $context = Get-DevContext $plan $launch $binding (Join-Path $plan.folder 'latest-context.json')
        @{state='ready';run_folder=$plan.folder;pid=$launch.pid;session_file=$binding;context=$context;
            pending_request=$(if(Test-Path -LiteralPath (Join-Path $plan.folder 'pending.json')){Get-Content -LiteralPath (Join-Path $plan.folder 'pending.json') -Raw | ConvertFrom-Json}else{$null})} | ConvertTo-Json -Depth 12
        return
    }
    & (Join-Path $PSScriptRoot 'dev-execute.ps1') -Action $Action -Plan $plan -Launch $launch -Binding $binding -Source $Source -RequestId $RequestId -BudgetMilliseconds $BudgetMilliseconds
} finally { if ($lock) { $lock.Dispose() } }
