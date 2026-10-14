param(
    [Parameter(Mandatory)][ValidateSet('prepare','start','check','cleanup')][string]$Action,
    [Parameter(Position=1)][string]$RunFolder,
    [switch]$Abort
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'host-path.ps1')
$runs = Join-Path $root 'artifacts\foundation\live'
$registration = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.FoundationQualification.addin'
$null = Assert-NativeHostPath $registration
if ($Action -eq 'prepare') {
    & (Join-Path $root 'project.cmd') build
    if ($LASTEXITCODE) { throw 'Build failed.' }
    $folder = Join-Path $runs ([Guid]::NewGuid().ToString())
    $bundle = Join-Path $folder 'bundle'
    New-Item -ItemType Directory -Path $bundle -Force | Out-Null
    $native = Join-Path $root 'tests\RevitIntegration\bin\Release\net10.0-windows'
    foreach ($file in @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','RevitThyme.RevitIntegration.dll')) {
        Copy-Item -LiteralPath (Join-Path $native $file) -Destination (Join-Path $bundle $file)
    }
    dotnet publish (Join-Path $root 'src\RevitThyme.Cli') -c Release --no-self-contained -o (Join-Path $bundle 'cli') --nologo
    if ($LASTEXITCODE) { throw 'CLI publish failed.' }
    New-Item -ItemType Directory -Path (Join-Path $folder 'sessions') -Force | Out-Null
    $xmlPath = [Security.SecurityElement]::Escape($bundle)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application"><Name>RevitThyme Foundation</Name><Assembly>$xmlPath\RevitThyme.Addin.dll</Assembly><AddInId>B09CCF69-B916-40B7-8A57-5923E45915D6</AddInId><FullClassName>RevitThyme.Addin.FoundationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme C# Foundation</VendorDescription></AddIn>
  <AddIn Type="Application"><Name>RevitThyme Foundation Qualification</Name><Assembly>$xmlPath\RevitThyme.RevitIntegration.dll</Assembly><AddInId>28254B23-7735-4A8D-9D3A-96639370D088</AddInId><FullClassName>RevitThyme.RevitIntegration.QualificationApplication</FullClassName><VendorId>WLKR</VendorId><VendorDescription>Disposable Foundation qualification only</VendorDescription></AddIn>
</RevitAddIns>
"@
    [IO.File]::WriteAllText((Join-Path $folder 'qualification.addin'), $manifest)
    $hashes = @{}; Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($bundle, $_.FullName); $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    }
    @{folder=$folder; source_revision=(git -C $root rev-parse HEAD); hashes=$hashes; manifest_hash=(Get-FileHash (Join-Path $folder 'qualification.addin')).Hash; revit_build=(Get-Item 'C:\Program Files\Autodesk\Revit 2027\Revit.exe').VersionInfo.FileVersion} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'plan.json') -Encoding utf8
    Write-Output $folder; return
}
if (-not $RunFolder) { throw 'An exact prepared run folder is required.' }
$folder = [IO.Path]::GetFullPath($RunFolder)
$runId = [Guid]::Empty
if ((Split-Path $folder -Parent) -ne $runs -or -not [Guid]::TryParse((Split-Path $folder -Leaf), [ref]$runId)) { throw 'Use an exact UUID folder beneath artifacts\foundation\live.' }
$plan = Get-Content -LiteralPath (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $folder) { throw 'Prepared folder identity changed.' }
foreach ($property in $plan.hashes.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $folder 'bundle') $property.Name)).Hash.ToLowerInvariant() -ne $property.Value) { throw "Prepared file changed: $($property.Name)" }
}
$sourceManifest = Join-Path $folder 'qualification.addin'
if ((Get-FileHash -LiteralPath $sourceManifest).Hash -ne $plan.manifest_hash) { throw 'Prepared manifest changed.' }
if ($Action -eq 'start') {
    if ((Test-Path -LiteralPath $registration) -or (Test-Path (Join-Path (Split-Path $registration -Parent) 'RevitThyme.Foundation.addin'))) { throw 'Foundation is already registered. Preserve it and prepare an isolated installation before testing.' }
    if ((Test-Path (Join-Path $folder 'launch.json')) -or (Test-Path (Join-Path $folder 'Foundation-Test.rvt'))) { throw 'Run already launched or fixture exists. Prepare a new run.' }
    $exe = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
    if ((Get-Item $exe).VersionInfo.FileVersion -ne $plan.revit_build) { throw 'Revit build changed.' }
    # Existing registrations and processes are inventoried and never replaced or stopped.
    $before = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { @{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()} })
    $registrations = @(Get-ChildItem -LiteralPath (Split-Path $registration -Parent) -File | ForEach-Object { @{path=$_.FullName;hash=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    Copy-Item -LiteralPath $sourceManifest -Destination $registration
    $null = Assert-NativeHostPath $registration
    if ((Get-FileHash -LiteralPath $registration).Hash -ne $plan.manifest_hash) { throw 'Native registration readback failed.' }
    $environment = @{
        REVITTHYME_FOUNDATION_TEST_RUN=$folder
        REVITTHYME_FOUNDATION_FIXTURE=(Join-Path $folder 'Foundation-Test.rvt')
        REVITTHYME_FOUNDATION_SESSIONS=(Join-Path $folder 'sessions')
    }
    $process = Start-Process -FilePath $exe -ArgumentList '/nosplash' -Environment $environment -PassThru -WindowStyle Hidden
    @{pid=$process.Id;start=$process.StartTime.ToUniversalTime().Ticks.ToString();existing_processes=$before;existing_registrations=$registrations;registration=$registration} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'launch.json') -Encoding utf8
    Write-Output "Started owned qualification process $($process.Id): $folder"; return
}
$launch = Get-Content -LiteralPath (Join-Path $folder 'launch.json') -Raw | ConvertFrom-Json
if ($Action -eq 'cleanup') {
    $process = Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $launch.start) {
        if (-not (Test-Path (Join-Path $folder 'native-result.json'))) {
            if (-not $Abort) { throw 'Qualification is unfinished. Inspect its state; use -Abort only to end this exact owned test run.' }
            @{passed=$false;aborted=$true;reason='Explicitly aborted owned qualification run';pid=$process.Id} | ConvertTo-Json | Set-Content (Join-Path $folder 'abort.json')
        }
        # This process was launched for qualification and contains only owned fixtures. Never stop by process name.
        Stop-Process -Id $process.Id; $process.WaitForExit(10000) | Out-Null
    }
    if (Test-Path -LiteralPath $registration) {
        if ((Get-FileHash -LiteralPath $registration).Hash -ne $plan.manifest_hash) { throw 'Qualification registration changed; preserve it.' }
        Remove-Item -LiteralPath $registration
    }
    foreach ($entry in $launch.existing_registrations) {
        if ((Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.hash) { throw 'An existing registration changed.' }
    }
    Write-Output 'Owned test process stopped; original registrations verified; all fixtures and evidence retained.'; return
}
if (-not (Test-Path (Join-Path $folder 'ready.json'))) {
    if (Test-Path (Join-Path $folder 'native-result.json')) { Get-Content (Join-Path $folder 'native-result.json') -Raw; throw 'Native qualification failed before CLI readiness.' }
    Write-Output 'Native startup/WPF qualification is pending. Check this exact Revit process.'; return
}
if (Test-Path (Join-Path $folder 'cli-complete.json')) {
    if (Test-Path (Join-Path $folder 'native-result.json')) {
        $result = Get-Content (Join-Path $folder 'native-result.json') -Raw | ConvertFrom-Json
        $result | ConvertTo-Json -Depth 8; if (-not $result.passed) { throw 'Native qualification failed.' }
    } else { Write-Output 'CLI checks passed; native Undo/Redo and fixture guard checks are pending.' }
    return
}
$ready = Get-Content (Join-Path $folder 'ready.json') -Raw | ConvertFrom-Json
if ($ready.processId -ne $launch.pid -or $ready.binding.processStart -ne $launch.start) { throw 'Readiness is from a different process.' }
$session = Join-Path (Join-Path $folder 'sessions') ($ready.binding.sessionId+'.json')
$cli = Join-Path $folder 'bundle\cli\revitthyme.exe'
$checks = [Collections.Generic.List[object]]::new()
function Invoke-CheckedCli([string[]]$Arguments, [int]$ExpectedExit, [string]$ExpectedCode) {
    $output = & $cli @Arguments
    $exit = $LASTEXITCODE
    $data = ($output -join "`n") | ConvertFrom-Json
    $passed = $exit -eq $ExpectedExit -and $data.code -eq $ExpectedCode
    $checks.Add(@{code=$ExpectedCode;exit=$exit;passed=$passed;result=$data})
    if (-not $passed) { $checks | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $folder 'cli-failed.json'); throw "CLI check failed: $ExpectedCode" }
    return $data
}
function Save-Capture($Data, [string]$Name) {
    $path = Join-Path $folder $Name
    $Data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}
$inspect = Invoke-CheckedCli @('inspect','--session',$session,'--json') 0 'inspected'
if ($inspect.snapshot.documentPath -ne $ready.fixture -or $inspect.snapshot.viewName -ne 'Foundation Plan') { throw 'CLI inspection targeted the wrong fixture.' }
$capture = Save-Capture $inspect 'capture.json'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$capture,'--name','Foundation Plan','--json') 0 'unchanged'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$capture,'--name','CLI Foundation Renamed','--json') 0 'applied'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$capture,'--name','Stale Edit','--json') 1 'stale_target'
$fresh = Invoke-CheckedCli @('inspect','--session',$session,'--json') 0 'inspected'
$capture = Save-Capture $fresh 'fresh.json'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$capture,'--name','Occupied Name','--json') 1 'rolled_back'
$fresh = Invoke-CheckedCli @('inspect','--session',$session,'--json') 0 'inspected'
$fresh.snapshot.target.viewId = 'wrong-view'
$wrong = Save-Capture $fresh 'wrong-view.json'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$wrong,'--name','Wrong View','--json') 1 'stale_target'
$fresh = Invoke-CheckedCli @('inspect','--session',$session,'--json') 0 'inspected'
$fresh.snapshot.target.documentId = 'wrong-document'
$wrong = Save-Capture $fresh 'wrong-document.json'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$wrong,'--name','Wrong Document','--json') 1 'stale_target'
$fresh = Invoke-CheckedCli @('inspect','--session',$session,'--json') 0 'inspected'
$capture = Save-Capture $fresh 'restore.json'
$null = Invoke-CheckedCli @('rename-view','--session',$session,'--capture',$capture,'--name','Foundation Plan','--json') 0 'applied'
$checks | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $folder 'cli-complete.json') -Encoding utf8
Write-Output "Passed $($checks.Count) actual-Revit CLI checks. Native Undo/Redo checks will finish on Idling."
