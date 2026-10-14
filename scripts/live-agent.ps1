param(
    [Parameter(Mandatory)][ValidateSet('prepare','start','check','cleanup')][string]$Action,
    [Parameter(Position=1)][string]$RunFolder,
    [switch]$Abort,
    [switch]$ReuseCodexSignIn
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'host-path.ps1')
$runs = Join-Path $root 'artifacts\agent\live'
$addins = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027'
$registration = Assert-NativeHostPath (Join-Path $addins 'RevitThyme.AgentQualification.addin')
$production = Assert-NativeHostPath (Join-Path $addins 'RevitThyme.Foundation.addin')
if ($Action -eq 'prepare') {
    & (Join-Path $root 'project.cmd') build
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $folder = Join-Path $runs ([Guid]::NewGuid().ToString())
    $bundle = Join-Path $folder 'bundle'
    New-Item -ItemType Directory -Path $bundle -Force | Out-Null
    foreach ($file in @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','RevitThyme.Codex.dll','RevitThyme.RevitIntegration.dll')) {
        Copy-Item -LiteralPath (Join-Path $root "tests\RevitIntegration\bin\Release\net10.0-windows\$file") -Destination (Join-Path $bundle $file)
    }
    foreach ($file in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','RevitThyme.Addin.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $root "src\RevitThyme.Addin\bin\Release\net10.0-windows\$file") -Destination (Join-Path $bundle $file)
    }
    dotnet publish (Join-Path $root 'src\RevitThyme.Mcp') -c Release --no-self-contained -o (Join-Path $bundle 'mcp') --nologo
    if ($LASTEXITCODE) { throw 'MCP publish failed.' }
    Copy-Item -LiteralPath (Join-Path $root 'skills') -Destination (Join-Path $bundle 'skills') -Recurse
    & (Join-Path $root 'installer\codex-runtime.ps1') -Destination (Join-Path $bundle 'codex')
    New-Item -ItemType Directory -Path (Join-Path $folder 'sessions') -Force | Out-Null
    $escaped = [Security.SecurityElement]::Escape($bundle)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
<AddIn Type="Application"><Name>RevitThyme</Name><Assembly>$escaped\RevitThyme.Addin.dll</Assembly><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme</VendorDescription></AddIn>
<AddIn Type="Application"><Name>RevitThyme Agent Qualification</Name><Assembly>$escaped\RevitThyme.RevitIntegration.dll</Assembly><AddInId>50C6065C-FE18-448E-A7B4-D8ED3C6FE2DD</AddInId><FullClassName>RevitThyme.RevitIntegration.AgentQualificationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>Disposable qualification</VendorDescription></AddIn>
</RevitAddIns>
"@
    [IO.File]::WriteAllText((Join-Path $folder 'qualification.addin'), $manifest)
    $hashes = @{}; Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object { $hashes[[IO.Path]::GetRelativePath($bundle,$_.FullName)] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
    @{folder=$folder;source_revision=(git -C $root rev-parse HEAD);hashes=$hashes;manifest_hash=(Get-FileHash (Join-Path $folder 'qualification.addin')).Hash;revit_build=(Get-Item 'C:\Program Files\Autodesk\Revit 2027\Revit.exe').VersionInfo.FileVersion} |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'plan.json') -Encoding utf8
    Write-Output $folder; return
}
if (-not $RunFolder) { throw 'Use an exact prepared run folder.' }
$folder = [IO.Path]::GetFullPath($RunFolder); $runId = [Guid]::Empty
if ((Split-Path $folder -Parent) -ne $runs -or -not [Guid]::TryParse((Split-Path $folder -Leaf),[ref]$runId)) { throw 'Use a UUID folder directly beneath artifacts\agent\live.' }
$plan = Get-Content (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder) { throw 'Prepared identity changed.' }
foreach ($entry in $plan.hashes.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $folder 'bundle') $entry.Name)).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Prepared file changed: $($entry.Name)" }
}
$sourceManifest = Join-Path $folder 'qualification.addin'
if ((Get-FileHash $sourceManifest).Hash -ne $plan.manifest_hash) { throw 'Manifest changed.' }
$parked = Join-Path $folder 'production-Foundation.addin'
$authCopy = Join-Path $folder 'agent\codex-home\auth.json'
if ($Action -eq 'start') {
    if ((Test-Path $registration) -or (Test-Path (Join-Path $folder 'launch.json')) -or (Test-Path $parked)) { throw 'Preserve existing test state; prepare a fresh run.' }
    $exe = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
    if ((Get-Item $exe).VersionInfo.FileVersion -ne $plan.revit_build) { throw 'Revit build changed.' }
    $before = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { @{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()} })
    $registrations = @(Get-ChildItem -LiteralPath $addins -File | ForEach-Object { @{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash} })
    if ($ReuseCodexSignIn) {
        $auth = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\auth.json'
        if (-not (Test-Path -LiteralPath $auth)) { throw 'Existing Codex sign-in unavailable; use a fresh browser sign-in.' }
        New-Item -ItemType Directory -Path (Split-Path $authCopy -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $auth -Destination $authCopy
    }
    # Park only the exact registration for startup. Already-running models retain their loaded add-in.
    @{existing_processes=$before;existing_registrations=$registrations;registration=$registration;production=$production;parked=$parked} |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'launch.json') -Encoding utf8
    try {
        if (Test-Path $production) { Move-Item -LiteralPath $production -Destination $parked }
        Copy-Item -LiteralPath $sourceManifest -Destination $registration
        if ((Get-FileHash (Assert-NativeHostPath $registration)).Hash -ne $plan.manifest_hash) { throw 'Registration readback failed.' }
        $environment = @{REVITTHYME_AGENT_TEST_RUN=$folder;REVITTHYME_AGENT_HOME=(Join-Path $folder 'agent');REVITTHYME_FOUNDATION_FIXTURE=(Join-Path $folder 'Foundation-Test.rvt');REVITTHYME_FOUNDATION_SESSIONS=(Join-Path $folder 'sessions')}
        $process = Start-Process -FilePath $exe -ArgumentList '/nosplash' -Environment $environment -PassThru -WindowStyle Hidden
        @{pid=$process.Id;start=$process.StartTime.ToUniversalTime().Ticks.ToString();existing_processes=$before;existing_registrations=$registrations;registration=$registration;production=$production;parked=$parked} |
            ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'launch.json') -Encoding utf8
    } catch {
        if ((Test-Path $parked) -and -not (Test-Path $production)) { Move-Item -LiteralPath $parked -Destination $production }
        throw
    }
    Write-Output "Started owned Revit process $($process.Id): $folder"; return
}
$launch = Get-Content (Join-Path $folder 'launch.json') -Raw | ConvertFrom-Json
function Restore-Production {
    if (Test-Path -LiteralPath $parked) {
        $original = @($launch.existing_registrations | Where-Object path -eq $production)
        if ($original.Count -ne 1 -or (Get-FileHash $parked).Hash -ne $original[0].hash -or (Test-Path $production)) { throw 'Production registration identity changed; preserve both files.' }
        Move-Item -LiteralPath $parked -Destination $production
        if ((Get-FileHash $production).Hash -ne $original[0].hash) { throw 'Production restoration failed.' }
    }
}
if ($Action -eq 'cleanup') {
    $process = Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $launch.start) {
        if (-not (Test-Path (Join-Path $folder 'native-result.json')) -and -not $Abort) { throw 'Run unfinished. Explicit -Abort required.' }
        $children = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains((Join-Path $folder 'bundle'),[StringComparison]::OrdinalIgnoreCase) -and $_.Name -in @('codex.exe','revitthyme-mcp.exe') })
        foreach ($child in $children) {
            $owned = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
            if ($owned -and $owned.StartTime -ge $process.StartTime) { Stop-Process -Id $owned.Id }
        }
        Stop-Process -Id $process.Id; $process.WaitForExit(10000) | Out-Null
    }
    if (Test-Path $registration) { if ((Get-FileHash $registration).Hash -ne $plan.manifest_hash) { throw 'Changed test registration must be preserved.' }; Remove-Item -LiteralPath $registration }
    Restore-Production
    if (Test-Path -LiteralPath $authCopy) { Remove-Item -LiteralPath $authCopy }
    foreach ($entry in $launch.existing_registrations) { if ((Get-FileHash $entry.path).Hash -ne $entry.hash) { throw 'Existing registration changed.' } }
    foreach ($entry in $launch.existing_processes) { $p=Get-Process -Id $entry.pid; if ($p.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.start) { throw 'Existing process changed.' } }
    @{passed=$true;existing_registrations_verified=$true;existing_processes_verified=$true;test_credential_removed=$true} | ConvertTo-Json | Set-Content (Join-Path $folder 'cleanup.json')
    Write-Output 'Owned test stopped; original registration restored; working processes preserved; temporary credential removed.'; return
}
if (Test-Path (Join-Path $folder 'progress.json')) { Restore-Production; Get-Content (Join-Path $folder 'progress.json') }
if (Test-Path (Join-Path $folder 'native-result.json')) {
    $result = Get-Content (Join-Path $folder 'native-result.json') -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 8; if (-not $result.passed) { throw 'Native Agent qualification failed.' }
} else { Write-Output 'Owned native Agent qualification is pending.' }
