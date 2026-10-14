param(
    [Parameter(Mandatory)][ValidateSet('install','start','status')][string]$Action,
    [Parameter(Mandatory)][string]$RunFolder,
    [string]$PreviousRunFolder,
    [switch]$Approved
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-path.ps1')
$root = Split-Path $PSScriptRoot -Parent
$folder = [IO.Path]::GetFullPath($RunFolder)
$labRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\revit-lab'))
$runId = [Guid]::Empty
if ((Split-Path $folder -Parent) -ne $labRoot -or -not [Guid]::TryParse((Split-Path $folder -Leaf), [ref]$runId)) { throw 'Use an exact prepared run folder beneath artifacts\revit-lab.' }
$null = Assert-NativeHostPath $folder
$plan = Get-Content -LiteralPath (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
$configPath = Join-Path $folder 'config.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder -or $config.run_id -ne $runId.ToString() -or $config.fixture_path -ne (Join-Path $folder 'RevitThyme-Test.rvt') -or $config.binding_path -ne (Join-Path $folder 'binding.json') -or $config.allow_writes -or -not $config.create_fixture) { throw 'Prepared read-only plan changed. Prepare a fresh run.' }
foreach ($property in $plan.hashes.PSObject.Properties) {
    $file = Join-Path (Join-Path $folder 'bundle') $property.Name
    $null = Assert-NativeHostPath $file
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $property.Value) { throw "Prepared bundle changed: $($property.Name)" }
}
$addin = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.Lab.addin'
$codexConfig = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\config.toml'
$revit = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
if ($Action -eq 'status') {
    Get-Process -Name Revit -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{pid=$_.Id;start_ticks=$_.StartTime.ToUniversalTime().Ticks.ToString();title=$_.MainWindowTitle} } | ConvertTo-Json
    if (Test-Path -LiteralPath $config.binding_path) { Get-Content -LiteralPath $config.binding_path -Raw }
    $errorPath = [IO.Path]::ChangeExtension($config.binding_path, '.error.json')
    if (Test-Path -LiteralPath $errorPath) { Get-Content -LiteralPath $errorPath -Raw }
    exit 0
}
if (-not $Approved) { throw 'Exact installation/fixture steps must be approved before this command.' }
if ((Get-Item -LiteralPath $revit).VersionInfo.FileVersion -ne $config.revit_version) { throw 'Installed Revit build changed; stop and prepare against the actual SDK.' }
if ($Action -eq 'install') {
    $null = Assert-NativeHostPath $addin
    $null = Assert-NativeHostPath $codexConfig
    $existing = [IO.File]::ReadAllText($codexConfig)
    $nextConfig = $existing
    $priorManifest = $null
    if ($PreviousRunFolder) {
        $previous = [IO.Path]::GetFullPath($PreviousRunFolder)
        $previousId = [Guid]::Empty
        if ($previous -eq $folder -or (Split-Path (Split-Path $previous -Parent) -Leaf) -ne 'revit-lab' -or -not [Guid]::TryParse((Split-Path $previous -Leaf), [ref]$previousId)) { throw 'Replacement requires the exact previous lab run folder.' }
        $null = Assert-NativeHostPath $previous
        $oldPlan = Get-Content -LiteralPath (Join-Path $previous 'plan.json') -Raw | ConvertFrom-Json
        if ($oldPlan.folder -ne $previous) { throw 'Previous prepared folder identity mismatch.' }
        $oldLaunch = Join-Path $previous 'launch.json'
        if (Test-Path -LiteralPath $oldLaunch) {
            $old = Get-Content -LiteralPath $oldLaunch -Raw | ConvertFrom-Json
            $running = Get-Process -Id $old.pid -ErrorAction SilentlyContinue
            if ($running -and $running.StartTime.ToUniversalTime().Ticks.ToString() -eq $old.start_ticks) { throw 'Previous test Revit process is still running. Do not replace its connection.' }
        }
        $priorManifest = [IO.File]::ReadAllText((Join-Path $previous 'RevitThyme.Lab.addin'))
        [xml]$oldManifest = $priorManifest
        if ($oldManifest.RevitAddIns.AddIn.Assembly -ne (Join-Path $previous 'bundle\RevitThyme.LabAdapter.dll') -or $oldManifest.RevitAddIns.AddIn.FullClassName -ne 'RevitThyme.Lab.Host' -or $oldManifest.RevitAddIns.AddIn.AddInId -ne '45F4DB43-1E27-4C0F-A629-0DCC5B43FB18') { throw 'Previous lab registration identity mismatch.' }
        if (-not (Test-Path -LiteralPath $addin) -or [IO.File]::ReadAllText($addin) -ne $priorManifest) { throw 'Existing lab registration does not match the exact previous run. Refusing replacement.' }
        $rotationOutput = & python -X utf8 (Join-Path $PSScriptRoot 'native-mcp-config.py') prepare-rotation $previous $folder
        if ($LASTEXITCODE) { throw 'Native MCP rotation preparation failed.' }
        $rotation = $rotationOutput | ConvertFrom-Json
        if ($rotation.candidate -ne (Join-Path $folder 'config.toml.prepared-rotation') -or (Get-FileHash -LiteralPath $codexConfig).Hash.ToLowerInvariant() -ne $rotation.source_sha256) { throw 'Codex configuration changed during rotation preparation.' }
        $nextConfig = [IO.File]::ReadAllText($rotation.candidate)
        Copy-Item -LiteralPath $addin -Destination (Join-Path $folder 'RevitThyme.Lab.addin.before-install')
    }
    if (-not $PreviousRunFolder -and $nextConfig -match '(?m)^\[mcp_servers\.revitthyme_test(\]|\.)') { throw 'Test MCP entry already exists. Review it; never silently replace a connection.' }
    $source = Join-Path $folder 'RevitThyme.Lab.addin'
    [xml]$manifest = [IO.File]::ReadAllText($source)
    if ($manifest.RevitAddIns.AddIn.Assembly -ne (Join-Path $folder 'bundle\RevitThyme.LabAdapter.dll') -or $manifest.RevitAddIns.AddIn.FullClassName -ne 'RevitThyme.Lab.Host' -or $manifest.RevitAddIns.AddIn.AddInId -ne '45F4DB43-1E27-4C0F-A629-0DCC5B43FB18') { throw 'Prepared registration changed.' }
    if ((Test-Path -LiteralPath $addin) -and -not $PreviousRunFolder) { throw 'Lab registration already exists. Review and back it up before replacement.' }
    $backup = Join-Path $folder 'config.toml.before-install'
    Copy-Item -LiteralPath $codexConfig -Destination $backup -ErrorAction Stop
    $snippet = [IO.File]::ReadAllText((Join-Path $folder 'mcp.toml'))
    $expectedExe = (Join-Path $folder 'bundle\revitthyme-test.exe') | ConvertTo-Json -Compress
    $expectedBinding = $config.binding_path | ConvertTo-Json -Compress
    $expected = "[mcp_servers.revitthyme_test]`ncommand = $expectedExe`nargs = [`"--lab-mcp`", $expectedBinding]`nstartup_timeout_sec = 30`ntool_timeout_sec = 60`n"
    if ($snippet.Replace("`r`n","`n") -ne $expected) { throw 'Prepared MCP snippet changed.' }
    $wroteConfig = $false
    try {
        Copy-Item -LiteralPath $source -Destination $addin -ErrorAction Stop
        if ([IO.File]::ReadAllText($codexConfig) -ne $existing) { throw 'Codex configuration changed during installation. Retry only after review.' }
        $revisedConfig = if ($PreviousRunFolder) { $nextConfig } else { $nextConfig.TrimEnd()+"`n`n"+$snippet }
        [IO.File]::WriteAllText($codexConfig, $revisedConfig, [Text.UTF8Encoding]::new($false))
        $wroteConfig = $true
        $null = Assert-NativeHostPath $addin
        if ((Get-FileHash -LiteralPath $addin).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Native registration readback mismatch.' }
    } catch {
        if ($wroteConfig) { [IO.File]::WriteAllText($codexConfig, $existing, [Text.UTF8Encoding]::new($false)) }
        if ($priorManifest) { [IO.File]::WriteAllText($addin, $priorManifest, [Text.UTF8Encoding]::new($false)) }
        elseif (Test-Path -LiteralPath $addin) { Remove-Item -LiteralPath $addin }
        throw
    }
    @{status='installed';registration=$addin;mcp_config=$codexConfig;backup=$backup;run=$folder} | ConvertTo-Json
} else {
    if (-not (Test-Path -LiteralPath $addin) -or [IO.File]::ReadAllText($addin) -ne [IO.File]::ReadAllText((Join-Path $folder 'RevitThyme.Lab.addin'))) { throw 'This exact native lab bundle is not registered.' }
    if (Test-Path -LiteralPath $config.fixture_path) { throw 'Fixture exists; refusing replacement or reuse.' }
    if (Test-Path -LiteralPath $config.binding_path) { throw 'Run already bound; refusing another Revit process.' }
    $receipt = Join-Path $folder 'launch.json'
    if (Test-Path -LiteralPath $receipt) { throw 'Run already launched. Inspect status before any retry.' }
    # Only this child inherits the lab flag. Never restart, close or kill the working instance.
    $process = Start-Process -FilePath $revit -PassThru -WindowStyle Hidden -Environment @{REVITTHYME_LAB_CONFIG=$configPath} -RedirectStandardOutput (Join-Path $folder 'launch.stdout.log') -RedirectStandardError (Join-Path $folder 'launch.stderr.log')
    @{status='launched';pid=$process.Id;start_ticks=$process.StartTime.ToUniversalTime().Ticks.ToString();run=$folder;fixture_creation='pending';actual_revit_verified=$false} | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding utf8
    Get-Content -LiteralPath $receipt -Raw
}
