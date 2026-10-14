param(
    [Parameter(Position=0)][ValidateSet('check','install','disconnect','bind','status')][string]$Action = 'check',
    [Parameter(Position=1)][string]$RunFolder,
    [string]$CiReport
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-path.ps1')
$root = Split-Path $PSScriptRoot -Parent
if ($Action -eq 'check') {
    & python -X utf8 (Join-Path $PSScriptRoot 'native-mcp-config.py') check
    exit $LASTEXITCODE
}
$folder = [IO.Path]::GetFullPath($RunFolder)
$guid = [Guid]::Empty
if ((Split-Path $folder -Parent) -ne (Join-Path $root 'artifacts\revit-lab') -or -not [Guid]::TryParse((Split-Path $folder -Leaf),[ref]$guid)) { throw 'Use an exact prepared lab folder from this checkout.' }
$null = Assert-NativeHostPath $folder
$plan = Get-Content -LiteralPath (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder) { throw 'Prepared folder identity mismatch.' }
$hostDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027'
$previewManifest = Join-Path $hostDir 'RevitThyme.NativePreview.addin'
$labManifest = Join-Path $hostDir 'RevitThyme.Lab.addin'
$pyManifest = Join-Path $hostDir 'pyRevit.addin'
$disabledManifest = Join-Path $hostDir 'pyRevit.addin.disabled'
$codexConfig = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\config.toml'
foreach ($target in @($previewManifest,$labManifest,$pyManifest,$disabledManifest,$codexConfig)) { $null = Assert-NativeHostPath $target }
if ($Action -eq 'status') {
    $runtime = Join-Path $folder 'binding.runtime.json'
    if (Test-Path -LiteralPath $runtime) { Get-Content -LiteralPath $runtime -Raw }
    Get-Process -Name Revit -ErrorAction SilentlyContinue | Select-Object Id,MainWindowTitle | ConvertTo-Json
    @{pyrevit_registered=(Test-Path -LiteralPath $pyManifest);disabled_copy=(Test-Path -LiteralPath $disabledManifest)} | ConvertTo-Json
    exit 0
}
if ($Action -eq 'install') {
    $sha = (& git -C $root rev-parse HEAD).Trim()
    if (& git -C $root status --porcelain) { throw 'Installation requires a clean checkout.' }
    $ci = Get-Content -LiteralPath $CiReport -Raw | ConvertFrom-Json
    $package = Join-Path $root 'desktop\out\RevitThyme-win32-x64'
    $manifest = Get-Content -LiteralPath (Join-Path $package 'resources\package-manifest.json') -Raw | ConvertFrom-Json
    if (-not $ci.passed -or -not $ci.worktree_clean -or $ci.scope -ne 'full' -or $ci.sha -ne $sha -or $manifest.source_sha -ne $sha -or $plan.sha -ne $sha) { throw 'Require matching clean source, full CI, desktop package and lab plan.' }
    if ((Get-Item 'C:\Program Files\Autodesk\Revit 2027\Revit.exe').VersionInfo.FileVersion -ne $manifest.build_reference_revit) { throw 'Native package does not match the installed Revit build.' }
    foreach ($entry in $plan.hashes.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $folder ('bundle\'+$entry.Name))).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Prepared lab bundle changed.' }
    }
    foreach ($entry in $manifest.native.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $package ('resources\native\'+$entry.Name))).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Native desktop payload changed.' }
    }
    $installedRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('AppData\Local\RevitThyme\native-preview\'+$sha)
    $destination = Join-Path $installedRoot 'RevitThyme-win32-x64'
    $null = Assert-NativeHostPath $destination
    if (Test-Path -LiteralPath $installedRoot) { throw 'Immutable candidate folder exists; inspect the previous install instead of overwriting.' }
    foreach ($pair in @(@($previewManifest,'preview.addin.before-cutover'),@($labManifest,'lab.addin.before-cutover'))) {
        [xml]$registered = [IO.File]::ReadAllText($pair[0])
        $expectedClass = if ($pair[0] -eq $previewManifest) { 'RevitThyme.RevitAdapter.Host' } else { 'RevitThyme.Lab.Host' }
        if ($registered.RevitAddIns.AddIn.FullClassName -ne $expectedClass -or $registered.RevitAddIns.AddIn.VendorId -ne 'WLKR') { throw 'Refusing to replace an unrelated native registration.' }
        $backup = Join-Path $folder $pair[1]
        if (Test-Path -LiteralPath $backup) { throw 'Registration backup already exists.' }
        Copy-Item -LiteralPath $pair[0] -Destination $backup
    }
    New-Item -ItemType Directory -Path $installedRoot | Out-Null
    Copy-Item -LiteralPath $package -Destination $destination -Recurse
    $null = Assert-NativeHostPath $destination
    foreach ($file in Get-ChildItem -LiteralPath $package -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($package,$file.FullName)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination $relative)).Hash) { throw 'Immutable bundle copy mismatch.' }
    }
    $previewText = [IO.File]::ReadAllText((Join-Path $destination 'resources\native\RevitThyme.addin.template')).Replace('__APPROVED_ABSOLUTE_BUNDLE_PATH__',[System.Security.SecurityElement]::Escape($destination))
    $labText = [IO.File]::ReadAllText((Join-Path $folder 'RevitThyme.Lab.addin'))
    try {
        [IO.File]::WriteAllText($previewManifest,$previewText,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($labManifest,$labText,[Text.UTF8Encoding]::new($false))
        if ([IO.File]::ReadAllText($previewManifest) -ne $previewText -or [IO.File]::ReadAllText($labManifest) -ne $labText) { throw 'Native registration readback mismatch.' }
    } catch {
        Copy-Item -LiteralPath (Join-Path $folder 'preview.addin.before-cutover') -Destination $previewManifest
        Copy-Item -LiteralPath (Join-Path $folder 'lab.addin.before-cutover') -Destination $labManifest
        throw
    }
    @{sha=$sha;bundle=$destination;preview_registration=$previewManifest;lab_registration=$labManifest;working_processes_preserved=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'native-install.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $folder 'native-install.json') -Raw
} elseif ($Action -eq 'disconnect') {
    if (-not (Test-Path -LiteralPath (Join-Path $folder 'native-install.json'))) { throw 'Install the verified native candidate before disconnecting legacy routes.' }
    if (-not (Test-Path -LiteralPath $pyManifest) -or (Test-Path -LiteralPath $disabledManifest)) { throw 'Expected one active pyRevit registration and no disabled copy.' }
    [xml]$py = [IO.File]::ReadAllText($pyManifest)
    if ($py.RevitAddIns.AddIn.FullClassName -ne 'PyRevitLoader.PyRevitLoaderApplication') { throw 'Unexpected pyRevit registration; inspect before moving it.' }
    $backup = Join-Path $folder 'pyRevit.addin.before-disconnect'
    if (Test-Path -LiteralPath $backup) { throw 'Disconnect backup exists; inspect the prior operation.' }
    Copy-Item -LiteralPath $pyManifest -Destination $backup
    Move-Item -LiteralPath $pyManifest -Destination $disabledManifest
    try {
        $configChange = & python -X utf8 (Join-Path $PSScriptRoot 'native-mcp-config.py') disable $folder
        if ($LASTEXITCODE) { throw 'Legacy MCP disable failed.' }
        $configReceipt = $configChange | ConvertFrom-Json
        $configChange
        if ((Get-FileHash -LiteralPath $disabledManifest).Hash -ne (Get-FileHash -LiteralPath $backup).Hash -or (Test-Path -LiteralPath $pyManifest)) { throw 'pyRevit registration disable readback mismatch.' }
    } catch {
        if ($configReceipt -and (Get-FileHash -LiteralPath $codexConfig).Hash.ToLowerInvariant() -eq $configReceipt.sha256) { Copy-Item -LiteralPath $configReceipt.backup -Destination $codexConfig }
        Move-Item -LiteralPath $disabledManifest -Destination $pyManifest
        throw
    }
    @{status='disabled_for_future_startup';manifest=$disabledManifest;backup=$backup;legacy_mcp=@('revit','jev-revit');running_revit_restarted=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'pyrevit-disconnect.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $folder 'pyrevit-disconnect.json') -Raw
} else {
    $proof = Get-Content -LiteralPath (Join-Path $folder 'live-readonly.json') -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath (Join-Path $folder 'binding.runtime.json') -Raw | ConvertFrom-Json
    $installed = Get-Content -LiteralPath (Join-Path $folder 'native-install.json') -Raw | ConvertFrom-Json
    if (-not $proof.passed -or -not $runtime.preview_active -or @($runtime.pyrevit_assemblies).Count -ne 0 -or $proof.binding.process_id -ne $runtime.process_id -or $proof.binding.process_start_ticks -ne $runtime.process_start_ticks -or $runtime.preview_assembly -ne (Join-Path $installed.bundle 'resources\native\RevitThyme.RevitAdapter.dll')) { throw 'Require successful exact native-only live proof before selecting this MCP session.' }
    & python -X utf8 (Join-Path $PSScriptRoot 'native-mcp-config.py') bind $folder
    if ($LASTEXITCODE) { throw 'Native MCP selection failed.' }
}
