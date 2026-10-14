param(
    [Parameter(Mandatory)][ValidateSet('prepare','start','check','cleanup')][string]$Action,
    [Parameter(Position=1)][string]$RunFolder,
    [switch]$Abort
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'host-path.ps1')
$runs = Join-Path $root 'artifacts\dynamic\live'
$registration = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.DynamicQualification.addin'
$null = Assert-NativeHostPath $registration
if ($Action -eq 'prepare') {
    & (Join-Path $root 'project.cmd') build
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $folder = Join-Path $runs ([Guid]::NewGuid().ToString())
    $bundle = Join-Path $folder 'bundle'
    New-Item -ItemType Directory -Path $bundle -Force | Out-Null
    foreach ($file in @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','RevitThyme.RevitIntegration.dll')) {
        Copy-Item -LiteralPath (Join-Path $root "tests\RevitIntegration\bin\Release\net10.0-windows\$file") -Destination (Join-Path $bundle $file)
    }
    foreach ($file in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','RevitThyme.Addin.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $root "src\RevitThyme.Addin\bin\Release\net10.0-windows\$file") -Destination (Join-Path $bundle $file)
    }
    foreach ($entry in @(@('src\RevitThyme.Mcp','mcp'),@('tests\Mcp','tests'),@('src\RevitThyme.Cli','cli'))) {
        dotnet publish (Join-Path $root $entry[0]) -c Release --no-self-contained -o (Join-Path $bundle $entry[1]) --nologo
        if ($LASTEXITCODE) { throw 'MCP qualification publish failed.' }
    }
    New-Item -ItemType Directory -Path (Join-Path $folder 'sessions') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bundle 'eval') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'tests\CabinEval\build-cabin.csx') -Destination (Join-Path $bundle 'eval\build-cabin.csx')
    $escaped = [Security.SecurityElement]::Escape($bundle)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
<AddIn Type="Application"><Name>RevitThyme</Name><Assembly>$escaped\RevitThyme.Addin.dll</Assembly><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme</VendorDescription></AddIn>
<AddIn Type="Application"><Name>RevitThyme Dynamic Qualification</Name><Assembly>$escaped\RevitThyme.RevitIntegration.dll</Assembly><AddInId>9768BB55-FFB9-4A35-B85D-B896B45EB71B</AddInId><FullClassName>RevitThyme.RevitIntegration.DynamicQualificationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>Disposable qualification</VendorDescription></AddIn>
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
if ((Split-Path $folder -Parent) -ne $runs -or -not [Guid]::TryParse((Split-Path $folder -Leaf),[ref]$runId)) { throw 'Use a UUID folder directly beneath artifacts\dynamic\live.' }
$plan = Get-Content (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder) { throw 'Prepared identity changed.' }
foreach ($entry in $plan.hashes.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $folder 'bundle') $entry.Name)).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Prepared file changed: $($entry.Name)" }
}
$sourceManifest = Join-Path $folder 'qualification.addin'
if ((Get-FileHash $sourceManifest).Hash -ne $plan.manifest_hash) { throw 'Manifest changed.' }
if ($Action -eq 'start') {
    foreach ($name in @('RevitThyme.DynamicQualification.addin','RevitThyme.Foundation.addin','RevitThyme.FoundationQualification.addin')) {
        if (Test-Path (Join-Path (Split-Path $registration -Parent) $name)) { throw "Existing owned registration must be preserved: $name" }
    }
    if ((Test-Path (Join-Path $folder 'launch.json')) -or (Test-Path (Join-Path $folder 'Foundation-Test.rvt'))) { throw 'Prepare a new run.' }
    $exe = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
    if ((Get-Item $exe).VersionInfo.FileVersion -ne $plan.revit_build) { throw 'Revit build changed.' }
    $before = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { @{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()} })
    $registrations = @(Get-ChildItem -LiteralPath (Split-Path $registration -Parent) -File | ForEach-Object { @{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash} })
    Copy-Item -LiteralPath $sourceManifest -Destination $registration
    if ((Get-FileHash (Assert-NativeHostPath $registration)).Hash -ne $plan.manifest_hash) { throw 'Registration readback failed.' }
    $environment = @{REVITTHYME_DYNAMIC_TEST_RUN=$folder;REVITTHYME_TRUSTED_CODE='1';REVITTHYME_FOUNDATION_FIXTURE=(Join-Path $folder 'Foundation-Test.rvt');REVITTHYME_FOUNDATION_SESSIONS=(Join-Path $folder 'sessions')}
    $process = Start-Process -FilePath $exe -ArgumentList '/nosplash' -Environment $environment -PassThru -WindowStyle Hidden
    @{pid=$process.Id;start=$process.StartTime.ToUniversalTime().Ticks.ToString();existing_processes=$before;existing_registrations=$registrations;registration=$registration} |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'launch.json') -Encoding utf8
    Write-Output "Started owned Revit process $($process.Id): $folder"; return
}
$launch = Get-Content (Join-Path $folder 'launch.json') -Raw | ConvertFrom-Json
if ($Action -eq 'cleanup') {
    $process = Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $launch.start) {
        if (-not (Test-Path (Join-Path $folder 'native-result.json'))) {
            if (-not $Abort) { throw 'Run unfinished. Explicit -Abort required to stop this owned test process.' }
            @{aborted=$true;pid=$process.Id} | ConvertTo-Json | Set-Content (Join-Path $folder 'abort.json')
        }
        Stop-Process -Id $process.Id; $process.WaitForExit(10000) | Out-Null
    }
    if (Test-Path -LiteralPath $registration) {
        if ((Get-FileHash $registration).Hash -ne $plan.manifest_hash) { throw 'Changed registration must be preserved.' }
        Remove-Item -LiteralPath $registration
    }
    foreach ($entry in $launch.existing_registrations) { if ((Get-FileHash $entry.path).Hash -ne $entry.hash) { throw 'Existing registration changed.' } }
    foreach ($entry in $launch.existing_processes) { $p=Get-Process -Id $entry.pid; if ($p.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.start) { throw 'Existing process changed.' } }
    @{passed=$true;existing_registrations_verified=$true;existing_processes_verified=$true} | ConvertTo-Json | Set-Content (Join-Path $folder 'cleanup.json')
    Write-Output 'Owned test process/registration removed; existing sessions and registrations verified; fixtures/evidence retained.'; return
}
if (-not (Test-Path (Join-Path $folder 'ready.json'))) {
    if (Test-Path (Join-Path $folder 'native-result.json')) { Get-Content (Join-Path $folder 'native-result.json'); throw 'Native startup failed.' }
    Write-Output 'Owned native dynamic qualification is pending.'; return
}
if (-not (Test-Path (Join-Path $folder 'dynamic-mcp-complete.json'))) {
    dotnet (Join-Path $folder 'bundle\tests\RevitThyme.McpTests.dll') --dynamic-live $folder | Tee-Object (Join-Path $folder 'mcp-checks.txt')
    if ($LASTEXITCODE) { throw 'Live MCP checks failed.' }
}
if (Test-Path (Join-Path $folder 'native-result.json')) {
    $result = Get-Content (Join-Path $folder 'native-result.json') -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 8; if (-not $result.passed) { throw 'Native qualification failed.' }
} else { Write-Output 'MCP scripted checks complete; native verification remains pending.' }
