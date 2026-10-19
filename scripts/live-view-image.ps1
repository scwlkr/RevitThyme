param([Parameter(Mandatory)][ValidateSet('prepare','start','check','cleanup')][string]$Action, [string]$RunFolder, [switch]$Abort, [string]$ClientDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'host-path.ps1')
. (Join-Path $PSScriptRoot 'dev-signing-package.ps1')
$runs=Join-Path $root 'artifacts\view-image'
$registration=Assert-NativeHostPath (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027\RevitThyme.ImageQualification.addin')
if($Action -eq 'prepare'){
    dotnet build (Join-Path $root 'RevitThyme.sln') -c Release --nologo -warnaserror
    if($LASTEXITCODE){throw 'Candidate build failed.'}
    dotnet build (Join-Path $root 'tests\ImageExportLoader') -c Release --nologo -warnaserror
    if($LASTEXITCODE){throw 'Test loader build failed.'}
    $folder=Join-Path $runs ([guid]::NewGuid().ToString()); $bundle=Join-Path $folder 'bundle'
    New-Item -ItemType Directory -Path $bundle -Force | Out-Null
    Get-ChildItem (Join-Path $root 'tests\RevitIntegration\bin\Release\net10.0-windows') -File | Copy-Item -Destination $bundle
    Copy-Item (Join-Path $root 'tests\ImageExportLoader\bin\Release\net10.0-windows\RevitThyme.ImageExportLoader.dll') $bundle
    dotnet publish (Join-Path $root 'tests\Mcp') -c Release --no-self-contained -o (Join-Path $bundle 'client') --nologo
    if($LASTEXITCODE){throw 'External client publish failed.'}
    # Sign only new test copies with the existing persistent local identity.
    $signing=Get-Content (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\development\signing.json') -Raw | ConvertFrom-Json
    $tool=Find-DevelopmentSignTool
    foreach($file in Get-ChildItem $bundle -File -Filter 'RevitThyme*.dll'){
        & $tool sign /s My /sha1 $signing.thumbprint /fd SHA256 /q $file.FullName
        if($LASTEXITCODE){throw 'Test-copy signing failed.'}
        Assert-DevelopmentSignature $file.FullName $signing.thumbprint $tool
    }
    $escaped=[Security.SecurityElement]::Escape($bundle)
    $manifest=@"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application"><Name>RevitThyme Image Qualification</Name><Assembly>$escaped\RevitThyme.ImageExportLoader.dll</Assembly><AddInId>3D80A45F-BE1C-4EBC-9090-4F122CA40D17</AddInId><FullClassName>RevitThyme.ImageExportLoader.Loader</FullClassName><VendorId>WLKR</VendorId><VendorDescription>Disposable image export tests</VendorDescription><AllowLoadingIntoExistingSession>No</AllowLoadingIntoExistingSession></AddIn></RevitAddIns>
"@
    [IO.File]::WriteAllText((Join-Path $folder 'qualification.addin'),$manifest)
    $hashes=@{}; Get-ChildItem $bundle -Recurse -File | ForEach-Object {$hashes[[IO.Path]::GetRelativePath($bundle,$_.FullName)]=(Get-FileHash $_.FullName).Hash}
    @{folder=$folder;source_revision=(git -C $root rev-parse HEAD);source_diff_sha256=$(git -C $root diff | Out-String | ForEach-Object {[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($_)))});hashes=$hashes;manifest_hash=(Get-FileHash (Join-Path $folder 'qualification.addin')).Hash} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'plan.json')
    Write-Output $folder; return
}
$folder=[IO.Path]::GetFullPath($RunFolder)
if((Split-Path $folder -Parent) -ne $runs -or -not [guid]::TryParse((Split-Path $folder -Leaf),[ref]([guid]::Empty))){throw 'Use an exact prepared UUID run.'}
$plan=Get-Content (Join-Path $folder 'plan.json') -Raw | ConvertFrom-Json
if($plan.folder -ne $folder){throw 'Plan path changed.'}
foreach($entry in $plan.hashes.PSObject.Properties){if((Get-FileHash (Join-Path "$folder\bundle" $entry.Name)).Hash -ne $entry.Value){throw "Candidate changed: $($entry.Name)"}}
if((Get-FileHash "$folder\qualification.addin").Hash -ne $plan.manifest_hash){throw 'Test manifest changed.'}
if($Action -eq 'start'){
    if((Test-Path $registration) -or (Test-Path "$folder\launch.json")){throw 'Preserve existing test registration/run.'}
    $existing=@(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object {@{pid=$_.Id;start=$_.StartTime.ToUniversalTime().Ticks.ToString()}})
    $registrations=@(Get-ChildItem (Split-Path $registration) -File | ForEach-Object {@{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash}})
    Copy-Item "$folder\qualification.addin" $registration
    $envs=@{REVITTHYME_IMAGE_TEST_RUN=$folder;REVITTHYME_TRUSTED_CODE='0';REVITTHYME_FOUNDATION_SESSIONS="$folder\installed-host-sessions"}
    $p=Start-Process 'C:\Program Files\Autodesk\Revit 2027\Revit.exe' -ArgumentList '/nosplash' -Environment $envs -WindowStyle Hidden -PassThru
    @{pid=$p.Id;start=$p.StartTime.ToUniversalTime().Ticks.ToString();existing_processes=$existing;existing_registrations=$registrations;registration=$registration} | ConvertTo-Json -Depth 5 | Set-Content "$folder\launch.json"
    Write-Output "Started owned native image test PID $($p.Id): $folder";return
}
$launch=Get-Content "$folder\launch.json" -Raw | ConvertFrom-Json
if($Action -eq 'check'){
    if(-not(Test-Path "$folder\ready.json")){if(Test-Path "$folder\native-failure.json"){throw (Get-Content "$folder\native-failure.json" -Raw)};Write-Output 'Native startup pending.';return}
    $client=Join-Path $folder 'bundle\client'
    if($ClientDirectory){
        if(-not [IO.Path]::IsPathFullyQualified($ClientDirectory)){throw 'Use an absolute external client directory.'}
        $client=Assert-NativeHostPath $ClientDirectory
        if($client -eq "$folder\bundle\client"){throw 'Use the prepared client by default; do not replace immutable files.'}
        $inventory=@{};Get-ChildItem $client -File | ForEach-Object {$inventory[$_.Name]=(Get-FileHash $_.FullName).Hash}
        @{directory=$client;hashes=$inventory;source_revision=(git -C $root rev-parse HEAD)} | ConvertTo-Json -Depth 4 | Set-Content "$folder\external-client.json"
    }
    dotnet "$client\RevitThyme.McpTests.dll" --image-live $folder
    if($LASTEXITCODE){throw 'External MCP/native image tests failed. Preserve the run.'};return
}
if(-not(Test-Path "$folder\image-mcp-complete.json") -and -not $Abort){throw 'Preserve unfinished test process unless explicitly aborting this owned run.'}
$p=Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
if($p){
    if($Abort){
        $audit=Get-Content "$folder\audit.json" -Raw | ConvertFrom-Json
        if($audit.isModified){throw 'Restore/discard only the owned fixture and verify unmodified before abort close.'}
    }
    if($p.StartTime.ToUniversalTime().Ticks.ToString() -ne $launch.start){throw 'PID reused.'}
    $null=$p.CloseMainWindow()
    if(-not $p.WaitForExit(15000)){throw 'Owned test is awaiting normal close; no forced termination.'}
}
if((Get-FileHash $registration).Hash -ne $plan.manifest_hash){throw 'Registration changed; preserve.'}
Remove-Item -LiteralPath $registration
foreach($entry in $launch.existing_registrations){if((Get-FileHash $entry.path).Hash -ne $entry.hash){throw 'Other registration changed.'}}
foreach($entry in $launch.existing_processes){$p=Get-Process -Id $entry.pid;if($p.StartTime.ToUniversalTime().Ticks.ToString() -ne $entry.start){throw 'Other process changed.'}}
@{passed=$true;closedNormally=$true;existingProcessesPreserved=$true;existingRegistrationsPreserved=$true} | ConvertTo-Json | Set-Content "$folder\cleanup.json"
Write-Output 'Owned native process closed normally; other processes and registrations preserved.'
