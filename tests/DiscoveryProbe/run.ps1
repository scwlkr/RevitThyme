param(
    [ValidateSet('prepare','register','start','ready','close','unregister')][string]$Action,
    [ValidateSet('normal','fixture')][string]$Mode = 'normal',
    [ValidateSet('basic','restricted')][string]$Profile = 'basic',
    [ValidateSet('lifecycle_unconfirmed','read_only_file')][string]$ExpectedSaveCode = 'read_only_file',
    [string]$RunFolder,
    [string]$FixtureSource,
    [string]$FamilyTemplate = 'C:\ProgramData\Autodesk\RVT 2027\Family Templates\English\Metric Generic Model.rft'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $root $(if($Profile -eq 'restricted'){'artifacts\restricted-documents'}else{'artifacts\document-boundaries'})
$probeClass = $(if($Profile -eq 'restricted'){'RestrictedDiscoveryProbe'}else{'DiscoveryProbe'})
function Hash([string]$Path) {
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
}
function NoRevit { if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Preserve existing Revit; no registration change or fallback.' } }
if ($Action -eq 'prepare') {
    NoRevit
    $RunFolder = Join-Path $evidence ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $RunFolder | Out-Null
    if (-not [IO.Path]::IsPathFullyQualified($FixtureSource) -or -not [IO.Path]::IsPathFullyQualified($FamilyTemplate)) { throw 'Select explicit absolute disposable RVT and family template paths.' }
    $source = $FixtureSource
    $template = $FamilyTemplate
    $receipt = Get-Content (Join-Path $env:LOCALAPPDATA 'RevitThyme\setup\receipt.json') -Raw | ConvertFrom-Json
    if ($Profile -eq 'restricted' -and $Mode -ne 'normal') { throw 'Restricted fixtures use a normal isolated session.' }
    $plan = [ordered]@{folder=$RunFolder;mode=$Mode;profile=$Profile;source=$source;sourceSha256=(Hash $source);template=$template;templateSha256=(Hash $template)
        a=(Join-Path $RunFolder 'A.rvt');b=(Join-Path $RunFolder 'B.rvt');bundle=$receipt.installed
        manifestSha256=(Hash (Join-Path $receipt.installed 'manifest.json'));registration=$receipt.registration;registrationSha256=(Hash $receipt.registration)}
    foreach ($key in @('a','b')) { Copy-Item -LiteralPath $source -Destination $plan[$key]; if ((Hash $plan[$key]) -ne $plan.sourceSha256) { throw 'Copy changed.' } }
    if ($Profile -eq 'restricted') {
        $plan.w=Join-Path $RunFolder 'W.rvt'; $plan.r=Join-Path $RunFolder 'R.rvt'; $plan.link=Join-Path $RunFolder 'Link.rvt'; $plan.family=Join-Path $RunFolder 'Family.rfa'
        $plan.expectSaveCode=$ExpectedSaveCode; Copy-Item -LiteralPath $source -Destination $plan.r
        if ((Hash $plan.r) -ne $plan.sourceSha256) { throw 'Read-only fixture copy changed.' }
    }
    $plan | ConvertTo-Json | Set-Content (Join-Path $RunFolder 'plan.json') -Encoding utf8
    $plan | ConvertTo-Json; return
}
$registrationFile = Join-Path $evidence 'probe-registration.json'
if ($Action -eq 'register') {
    NoRevit
    if (Test-Path $registrationFile) {
        $prior=Get-Content $registrationFile -Raw|ConvertFrom-Json
        if (Test-Path -LiteralPath $prior.path) { throw 'Inspect existing live registration ownership before reuse.' }
        Move-Item -LiteralPath $registrationFile -Destination (Join-Path $evidence ('probe-registration-retired-'+[Guid]::NewGuid().ToString()+'.json'))
    }
    $binary = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\RevitThyme.DiscoveryProbe.dll'
    $signature = Get-AuthenticodeSignature -LiteralPath $binary
    $config = Get-Content (Join-Path $env:LOCALAPPDATA 'RevitThyme\development\signing.json') -Raw | ConvertFrom-Json
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $config.thumbprint) { throw 'Sign the probe with pinned persistent identity first.' }
    $id = [Guid]::NewGuid().ToString()
    $staged = Join-Path $evidence ('probe-'+$id); New-Item -ItemType Directory $staged | Out-Null
    $assembly = Join-Path $staged 'RevitThyme.DiscoveryProbe.dll'; Copy-Item -LiteralPath $binary -Destination $assembly
    $path = Join-Path $env:APPDATA ('Autodesk\Revit\Addins\2027\RevitThyme.DiscoveryProbe-'+$id+'.addin')
    $escaped = [Security.SecurityElement]::Escape($assembly)
    $xml = '<RevitAddIns><AddIn Type="Application"><Name>RevitThyme Discovery Qualification</Name><Assembly>'+$escaped+'</Assembly><AddInId>'+$id+'</AddInId><FullClassName>RevitThyme.DiscoveryProbe.'+$probeClass+'</FullClassName><VendorId>WLKR</VendorId><VendorDescription>Disposable native test fixture author only</VendorDescription></AddIn></RevitAddIns>'
    $stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $bytes=[Text.Encoding]::UTF8.GetBytes($xml); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    @{path=$path;sha256=(Hash $path);assembly=$assembly;assemblySha256=(Hash $assembly);source=(git -C $root rev-parse HEAD);certificate=$config.thumbprint} |
        ConvertTo-Json | Set-Content $registrationFile -Encoding utf8
    Get-Content $registrationFile; return
}
$probe = Get-Content $registrationFile -Raw | ConvertFrom-Json
if ((Hash $probe.path) -ne $probe.sha256 -or (Hash $probe.assembly) -ne $probe.assemblySha256) { throw 'Probe registration or signed binary changed.' }
if ($Action -eq 'unregister') {
    NoRevit
    $expectedParent = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'
    if ((Split-Path $probe.path -Parent) -ne $expectedParent -or (Split-Path $probe.path -Leaf) -notlike 'RevitThyme.DiscoveryProbe-*.addin') { throw 'Unowned registration.' }
    Remove-Item -LiteralPath $probe.path
    @{removed=$probe.path;hashChecked=$true;utc=[DateTime]::UtcNow} | ConvertTo-Json | Set-Content (Join-Path $evidence 'probe-removed.json')
    return
}
if (-not [IO.Path]::IsPathFullyQualified($RunFolder) -or (Split-Path $RunFolder -Parent) -ne $evidence) { throw 'Use exact owned absolute run folder.' }
$plan = Get-Content (Join-Path $RunFolder 'plan.json') -Raw | ConvertFrom-Json
if ($plan.folder -ne $RunFolder -or (Hash $plan.source) -ne $plan.sourceSha256 -or (Hash $plan.template) -ne $plan.templateSha256 -or
    (Hash $plan.registration) -ne $plan.registrationSha256 -or (Hash (Join-Path $plan.bundle 'manifest.json')) -ne $plan.manifestSha256) { throw 'Pinned inputs changed.' }
if ($Action -eq 'start') {
    NoRevit
    if ((Hash $plan.a) -ne $plan.sourceSha256 -or (Hash $plan.b) -ne $plan.sourceSha256) { throw 'Initial copies changed.' }
    $sessions = Join-Path $RunFolder 'sessions'; New-Item -ItemType Directory $sessions | Out-Null
    $environment = @{REVITTHYME_DISCOVERY_PROBE=$RunFolder;REVITTHYME_TRUSTED_CODE='0';REVITTHYME_FOUNDATION_SESSIONS=$sessions
        REVITTHYME_AGENT_HOME=(Join-Path $RunFolder 'agent');REVITTHYME_FOUNDATION_FIXTURE=$(if($plan.mode -eq 'fixture'){$plan.a}else{$null})}
    foreach ($key in @('REVITTHYME_LIFECYCLE_PROBE','REVITTHYME_DYNAMIC_TEST_RUN','REVITTHYME_FOUNDATION_TEST_RUN','REVITTHYME_MCP_TEST_RUN','REVITTHYME_UTILITIES_TEST_RUN','REVITTHYME_AGENT_TEST_RUN','REVITTHYME_UPDATE_TEST_RUN','REVITTHYME_RIBBON_TEST_RUN','REVITTHYME_IMAGE_TEST_RUN')) { $environment[$key]=$null }
    $p = Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2027\Revit.exe' -ArgumentList '/nosplash' -Environment $environment -WindowStyle Hidden -PassThru
    @{pid=$p.Id;start=$p.StartTime.ToUniversalTime().Ticks.ToString();sessions=$sessions;probeSha256=$probe.assemblySha256;probeRegistrationSha256=$probe.sha256} |
        ConvertTo-Json | Set-Content (Join-Path $RunFolder 'launch.json') -Encoding utf8
    Get-Content (Join-Path $RunFolder 'launch.json'); return
}
$launch = Get-Content (Join-Path $RunFolder 'launch.json') -Raw | ConvertFrom-Json
$p = Get-Process -Id $launch.pid
if ($p.ProcessName -ne 'Revit' -or $p.StartTime.ToUniversalTime().Ticks.ToString() -ne $launch.start) { throw 'Owned process binding changed.' }
if (Test-Path (Join-Path $RunFolder 'probe-error.json')) { throw (Get-Content (Join-Path $RunFolder 'probe-error.json') -Raw) }
$sessions = @(Get-ChildItem -LiteralPath $launch.sessions -Filter '*.json')
if ($sessions.Count -ne 1) { throw 'Exact binding not ready.' }
$binding = Get-Content $sessions[0].FullName -Raw | ConvertFrom-Json
if ($binding.protocolVersion -ne 13 -or $binding.processId -ne $launch.pid -or $binding.processStart -ne $launch.start) { throw 'Session differs from owned launch.' }
$ready = Get-Content (Join-Path $RunFolder 'ready.json') -Raw | ConvertFrom-Json
$expectedDocuments=$(if($Profile -eq 'restricted'){5}else{4})
if ($ready.pid -ne $launch.pid -or $ready.start -ne $launch.start -or $ready.activePath -ne $plan.a -or $ready.documents.Count -ne $expectedDocuments) { throw 'Independent fixture preparation not ready.' }
$context = & (Join-Path $plan.bundle 'cli\revitthyme.exe') inspect --session $sessions[0].FullName --json | ConvertFrom-Json
if (-not $context.succeeded -or $context.snapshot.documentPath -ne $plan.a -or $context.snapshot.isModified) { throw 'Exact clean active A required.' }
if ($Action -eq 'ready') {
    $input = $plan | ConvertTo-Json | ConvertFrom-Json -AsHashtable
    $input.pid=$launch.pid; $input.start=$launch.start; $input.session=$sessions[0].FullName
    $input | ConvertTo-Json | Set-Content (Join-Path $RunFolder 'input.json') -Encoding utf8
    $context | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $RunFolder 'initial-context.json') -Encoding utf8
    @{ready=$true;folder=$RunFolder;pid=$p.Id;mode=$plan.mode} | ConvertTo-Json; return
}
$complete = Get-Content (Join-Path $RunFolder 'complete.json') -Raw | ConvertFrom-Json
if (-not $complete.passed) { throw 'Inspect failed run before close; no forced cleanup.' }
$inventory = & (Join-Path $plan.bundle 'cli\revitthyme.exe') list-documents --session $sessions[0].FullName --request-id ([Guid]::NewGuid().ToString()) --json | ConvertFrom-Json
$expectedFinal=$(if($Profile -eq 'restricted'){2}else{1})
if (-not $inventory.succeeded -or $inventory.documents.total -ne $expectedFinal -or
    @($inventory.documents.items | Where-Object {$_.path -ne $plan.a -and ($Profile -ne 'restricted' -or $_.path -ne $plan.link -or -not $_.isLinked)}).Count) { throw 'Fixture teardown is incomplete.' }
$null = $p.CloseMainWindow()
if (-not $p.WaitForExit(15000)) { throw 'Normal close pending; preserve process.' }
if ($Profile -eq 'restricted') {
    foreach ($entry in $ready.hashes.psobject.Properties) { if ((Hash $entry.Name) -ne $entry.Value) { throw 'Authored saved files changed.' } }
    $original=Get-Content (Join-Path $RunFolder 'original-attributes.json') -Raw|ConvertFrom-Json
    if ([int][IO.File]::GetAttributes($plan.r) -ne $original.attributes) { throw 'Read-only fixture attributes not restored.' }
} elseif ((Hash $plan.a) -ne $plan.sourceSha256 -or (Hash $plan.b) -ne $plan.sourceSha256) { throw 'Saved copies changed.' }
@{closedNormally=$true;pid=$launch.pid;sourcePreserved=((Hash $plan.source) -eq $plan.sourceSha256);copiesPreserved=$true;templatePreserved=((Hash $plan.template) -eq $plan.templateSha256)} |
    ConvertTo-Json | Set-Content (Join-Path $RunFolder 'cleanup.json') -Encoding utf8
Get-Content (Join-Path $RunFolder 'cleanup.json')
