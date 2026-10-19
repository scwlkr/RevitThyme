$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$script:DevProject = Split-Path $PSScriptRoot -Parent
$script:DevRuns = Join-Path $script:DevProject 'artifacts\dev'
. (Join-Path $script:DevProject 'installer\common.ps1')

function Get-DevSettingsPath {
    Assert-NativeHostPath (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\development\session.json')
}
function Write-DevJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $Value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $temporary -Encoding utf8
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
function Get-DevPlan([string]$RunFolder) {
    if (-not $RunFolder -or -not [IO.Path]::IsPathFullyQualified($RunFolder)) { throw 'Supply an absolute -RunFolder from dev-prepare/start.' }
    $folder = Assert-NativeHostPath $RunFolder
    $id = [Guid]::Empty
    if ((Split-Path $folder -Parent) -ne $script:DevRuns -or -not [Guid]::TryParseExact((Split-Path $folder -Leaf),'D',[ref]$id)) {
        throw 'Use an exact UUID folder directly below artifacts\dev.'
    }
    $plan = Get-Content -LiteralPath (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
    if ($plan.schema -ne 1 -or $plan.folder -ne $folder -or $plan.fixture -ne (Join-Path $folder 'Fixture.rvt')) { throw 'Development plan identity changed.' }
    $null = Assert-NativeHostPath $plan.fixture
    return $plan
}
function Get-DevInstalled {
    $registration = Get-FoundationRegistration
    $bundle = Read-FoundationRegistration $registration
    $null = Assert-FoundationPackage $bundle
    return @{bundle=$bundle;registration=$registration;registration_hash=(Get-FileHash -LiteralPath $registration).Hash;manifest_hash=(Get-FileHash -LiteralPath (Join-Path $bundle 'manifest.json')).Hash}
}
function Assert-DevSigned([string]$Bundle, [string]$ConfigPath) {
    $output = & (Join-Path $PSScriptRoot 'dev-signing.ps1') -Action Verify -PackageDirectory $Bundle -ConfigPath $ConfigPath
    $signed = ($output -join "`n") | ConvertFrom-Json
    if (-not $signed.ready) { throw 'Development package signing verification failed.' }
    return $signed
}
function Get-DevLaunch($Plan) {
    $launch = Get-Content -LiteralPath (Join-Path $Plan.folder 'launch.json') -Raw | ConvertFrom-Json
    if ($launch.folder -ne $Plan.folder -or $launch.pid -le 0 -or -not $launch.start -or $launch.fixture -ne $Plan.fixture) { throw 'Invalid development launch identity.' }
    if ($launch.manifest_hash -ne $Plan.manifest_hash -or (Split-Path $launch.bundle -Leaf) -cne $Plan.manifest_hash.ToLowerInvariant()) { throw 'Unexpected launched bundle.' }
    $null = Assert-NativeHostPath $launch.bundle
    if ((Get-FileHash -LiteralPath (Join-Path $launch.bundle 'manifest.json')).Hash -ne $Plan.manifest_hash) { throw 'Launched package manifest changed.' }
    return $launch
}
function Get-DevProcess($Launch) {
    $process = Get-Process -Id $Launch.pid -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    if ($process.ProcessName -ne 'Revit' -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne $Launch.start -or $process.Path -ne $Launch.revit) {
        throw 'Development PID was reused or changed; no process was addressed.'
    }
    return $process
}
function Get-DevBinding($Plan, $Launch) {
    if (-not (Get-DevProcess $Launch)) { throw 'Development Revit has exited. Start a fresh run.' }
    $matches = @(Get-ChildItem -LiteralPath (Join-Path $Plan.folder 'sessions') -Filter '*.json' -File | ForEach-Object {
        if ($_.Length -gt 4096) { throw 'Oversized development binding.' }
        $binding = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        if ($binding.processId -eq $Launch.pid -and $binding.processStart -eq $Launch.start) {
            $id = [Guid]::Empty
            if ($binding.protocolVersion -ne 9 -or $binding.revitVersion -ne '2027' -or -not [Guid]::TryParseExact($binding.sessionId,'D',[ref]$id) -or
                $binding.pipeName -ne "RevitThyme-Foundation-$($Launch.pid)-$($binding.sessionId)") { throw 'Invalid development session binding.' }
            @{path=$_.FullName;value=$binding}
        }
    })
    if ($matches.Count -ne 1) { throw 'Development binding is not ready or is ambiguous. Check only the development Revit window and retry dev-status.' }
    $binding = $matches[0]
    if ($Launch.session_id -and $Launch.session_id -ne $binding.value.sessionId) { throw 'Development add-in session changed.' }
    if (-not $Launch.session_id) {
        $Launch.session_id = $binding.value.sessionId
        Write-DevJson (Join-Path $Plan.folder 'launch.json') $Launch
    }
    return $binding.path
}
function Invoke-DevCli($Launch, [string[]]$Arguments, [string]$OutputPath) {
    $exe = Join-Path $Launch.bundle 'cli\revitthyme.exe'
    if ((Get-FileHash -LiteralPath $exe).Hash -ne $Launch.cli_hash) { throw 'Pinned CLI changed.' }
    if (-not (Get-DevProcess $Launch)) { throw 'Development Revit has exited.' }
    $lines = & $exe @Arguments 2> ($OutputPath+'.stderr.txt')
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText($OutputPath, ($lines -join "`n"))
    try { $value = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "CLI returned no valid result (exit $exitCode). Evidence: $OutputPath" }
    if (-not $value -or -not $value.id) { throw "CLI returned an invalid operation result: $OutputPath" }
    return @{exit_code=$exitCode;value=$value;path=$OutputPath}
}
function Get-DevContext($Plan, $Launch, [string]$Binding, [string]$OutputPath) {
    $result = Invoke-DevCli $Launch @('inspect','--session',$Binding,'--json') $OutputPath
    $snapshot = $result.value.snapshot
    if ($result.exit_code -ne 0 -or -not $result.value.succeeded -or -not $snapshot) { throw "Development inspection rejected: $($result.value.message)" }
    if ($snapshot.documentPath -ne $Plan.fixture -or $snapshot.isWorkshared -or $snapshot.target.processId -ne $Launch.pid -or
        $snapshot.target.processStart -ne $Launch.start -or $snapshot.target.sessionId -ne $Launch.session_id) { throw 'Native development target does not match its pinned fixture/process.' }
    if ($Launch.document_id -and $Launch.document_id -ne $snapshot.target.documentId) { throw 'Development fixture was reopened. Start a fresh run.' }
    if (-not $Launch.document_id) {
        $Launch.document_id = $snapshot.target.documentId
        Write-DevJson (Join-Path $Plan.folder 'launch.json') $Launch
    }
    return $result.value
}
function Get-DevLock($Plan) {
    try { return [IO.File]::Open((Join-Path $Plan.folder 'session.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw 'Another development command owns this run. Wait for it to finish.' }
}
