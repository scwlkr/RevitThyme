#Requires -Version 5.1
# Offline behavior checks: network and process calls are scoped mocks.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$bootstrap = Join-Path (Split-Path $PSScriptRoot -Parent) 'installer\bootstrap.ps1'
$run = Join-Path ([IO.Path]::GetTempPath()) ('RevitThyme-bootstrap-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$checkCounter = @{Count=0}
function Assert([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Bootstrap check failed: $Name" }
    $checkCounter.Count++
}
function Invoke-Case([hashtable]$Options = @{}, [hashtable]$Arguments = @{}) {
    $payload = [Text.Encoding]::UTF8.GetBytes('scoped fake setup; never execute')
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($payload)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $version = '0.8.0'
    if ($Arguments.Version) { $version = $Arguments.Version }
    if ($Options.TagVersion) { $version = $Options.TagVersion }
    $name = "RevitThyme-$version-win-x64.exe"
    $baseUrl = "https://github.com/scwlkr/RevitThyme/releases/download/v$version/"
    $exe = @{name=$name; browser_download_url=($baseUrl+$name); digest=('sha256:'+$hash)}
    $checksum = @{name=($name+'.sha256'); browser_download_url=($baseUrl+$name+'.sha256'); digest=$null}
    $assets = @($exe,$checksum)
    if ($Options.MissingExe) { $assets = @($checksum) }
    if ($Options.DuplicateExe) { $assets += $exe }
    if ($Options.MissingChecksum) { $assets = @($exe) }
    if ($Options.DuplicateChecksum) { $assets += $checksum }
    if ($Options.ExeUrl) { $exe.browser_download_url = $Options.ExeUrl }
    if ($Options.ChecksumUrl) { $checksum.browser_download_url = $Options.ChecksumUrl }
    if ($Options.BadDigest) { $exe.digest = 'sha256:' + ('0' * 64) }
    if ($Options.NoDigest) { $exe.digest = $null }
    if ($Options.ChecksumDigest) { $checksum.digest = 'sha256:' + ('0' * 64) }
    $state = @{api=@(); downloads=@(); started=@(); error=$null; output=@(); paths=@(); warnings=@();
        fixture=@{version=$version;name=$name;hash=$hash;payload=$payload;assets=$assets;options=$Options}}
    function Invoke-RestMethod {
        param($Uri,$Headers,$UserAgent,$MaximumRedirection,$TimeoutSec)
        $state.api += $Uri
        Assert ($MaximumRedirection -eq 0) 'API redirects are disabled'
        return @{tag_name=('v'+$state.fixture.version);assets=$state.fixture.assets;draft=[bool]$state.fixture.options.Draft;prerelease=[bool]$state.fixture.options.Prerelease}
    }
    function Invoke-WebRequest {
        [CmdletBinding()]
        param($Uri,$OutFile,[switch]$UseBasicParsing,[switch]$PassThru,$MaximumRedirection,$TimeoutSec)
        Assert ($MaximumRedirection -eq 0) 'asset redirects are disabled'
        $state.downloads += $Uri; $state.paths += $OutFile
        if ($state.fixture.options.Redirect -and $Uri -like 'https://github.com/*') {
            if ($state.fixture.options.RedirectNonTerminating) { Write-Error 'The maximum redirection count has been exceeded.' -ErrorId MaximumRedirectExceeded }
            if ($state.fixture.options.RedirectException) {
                Add-Type -AssemblyName System.Net.Http
                $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::Redirect)
                $response.Headers.Location = [Uri]$state.fixture.options.Redirect
                $failure = [Exception]::new('mock redirect response')
                $failure | Add-Member -NotePropertyName Response -NotePropertyValue $response
                throw $failure
            }
            return @{StatusCode=302;Headers=@{Location=$state.fixture.options.Redirect}}
        }
        if ($OutFile.EndsWith('.sha256')) {
            $text = $state.fixture.hash + '  ' + $state.fixture.name
            if ($state.fixture.options.WrongChecksumName) { $text = $state.fixture.hash + '  another.exe' }
            if ($state.fixture.options.MultipleChecksums) { $text += "`n" + $state.fixture.hash + '  ' + $state.fixture.name }
            [IO.File]::WriteAllText($OutFile,$text)
        } else {
            $bytes = $state.fixture.payload
            if ($state.fixture.options.Corrupt) { $bytes = [byte[]](1,2,3) }
            [IO.File]::WriteAllBytes($OutFile,$bytes)
        }
        return @{StatusCode=200;Headers=@{}}
    }
    function Start-Process {
        param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru)
        Assert ($Wait -and $PassThru) 'setup is synchronous'
        Assert ((Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash -ieq $state.fixture.hash) 'only verified setup executes'
        $state.started += @{path=$FilePath;arguments=$ArgumentList}
        if ($state.fixture.options.SetupLog) {
            $logArgument = @($ArgumentList | Where-Object { $_ -like '/LOG=*' })[0]
            [IO.File]::WriteAllText($logArgument.Substring(6).TrimEnd('"'),'mock setup failure')
        }
        return @{ExitCode=([int]$state.fixture.options.ExitCode)}
    }
    try {
        $command = $bootstrap
        if ($Options.Inline) { $command = [scriptblock]::Create([IO.File]::ReadAllText($bootstrap)) }
        if ($Options.Pipe) { $state.output = @([IO.File]::ReadAllText($bootstrap) | Invoke-Expression -WarningVariable capturedWarnings 3>$null) }
        else { $state.output = @(& $command @Arguments -WarningVariable capturedWarnings 3>$null) }
    }
    catch { $state.error = $_.Exception.Message }
    $state.warnings = @($capturedWarnings)
    return $state
}
function Assert-Rejected($State,[string]$Pattern,[string]$Name) {
    Assert ($State.error -like $Pattern -and $State.started.Count -eq 0) $Name
    foreach ($path in $State.paths) { Assert (-not (Test-Path -LiteralPath $path)) "$Name cleans temporary downloads" }
}
try {
    . (Join-Path (Split-Path $PSScriptRoot -Parent) 'installer\pinned-bootstrap.ps1')
    $originalBootstrap = $bootstrap
    $pinned = Join-Path $run 'install.ps1'
    [IO.File]::WriteAllText($pinned,(Get-PinnedBootstrap -Version '0.8.1'))
    try {
        $bootstrap = $pinned
        $state = Invoke-Case @{Pipe=$true;TagVersion='0.8.1'}
        Assert (-not $state.error -and $state.started.Count -eq 1 -and $state.api[0].EndsWith('/tags/v0.8.1')) "piped release bootstrap pins v0.8.1 without arguments: $($state.error)"
        Assert-Rejected (Invoke-Case @{Pipe=$true;TagVersion='0.9.0'}) '*different version*' 'piped release bootstrap cannot drift to a newer version'
        Assert-Rejected (Invoke-Case @{Pipe=$true;TagVersion='0.8.1';Corrupt=$true}) '*checksum mismatch*' 'piped release bootstrap rejects modified installer'
    } finally { $bootstrap = $originalBootstrap }
    $state = Invoke-Case
    Assert (-not $state.error -and $state.started.Count -eq 1) 'latest setup succeeds'
    Assert ($state.api[0] -ceq 'https://api.github.com/repos/scwlkr/RevitThyme/releases/latest') 'latest endpoint'
    Assert ($state.downloads.Count -eq 2 -and $state.downloads[0].EndsWith('.exe.sha256') -and $state.downloads[1].EndsWith('.exe')) 'exact checksum and installer names'
    Assert ($state.started[0].arguments -contains '/NORESTART' -and $state.started[0].arguments -notcontains '/VERYSILENT') 'interactive default'
    Assert (-not (Test-Path -LiteralPath $state.started[0].path)) 'success cleans owned temporary setup'
    $state = Invoke-Case @{Inline=$true} @{Silent=$true}
    Assert (-not $state.error -and $state.started[0].arguments -contains '/VERYSILENT') 'one-line scriptblock invocation accepts installer options'
    $state = Invoke-Case @{} @{Version='0.8.0';Silent=$true}
    Assert (-not $state.error -and $state.api[0].EndsWith('/tags/v0.8.0')) 'pinned version endpoint'
    foreach ($flag in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-')) {
        Assert ($state.started[0].arguments -contains $flag) "silent flag $flag"
    }
    $state = Invoke-Case @{Prerelease=$true} @{Version='0.8.0-rc.1+build.2'}
    Assert (-not $state.error -and $state.started.Count -eq 1) 'explicit semantic prerelease supported'
    $state = Invoke-Case @{NoDigest=$true}
    Assert (-not $state.error -and $state.started.Count -eq 1) 'checksum protects older releases without API digest'
    Assert-Rejected (Invoke-Case @{Corrupt=$true}) '*checksum mismatch*' 'corrupt installer never executes'
    Assert-Rejected (Invoke-Case @{BadDigest=$true}) '*digest mismatch*' 'GitHub installer digest enforced'
    Assert-Rejected (Invoke-Case @{ChecksumDigest=$true}) '*digest mismatch*' 'GitHub checksum digest enforced'
    Assert-Rejected (Invoke-Case @{MissingExe=$true}) '*exactly one asset*' 'missing installer refused'
    Assert-Rejected (Invoke-Case @{DuplicateExe=$true}) '*exactly one asset*' 'ambiguous installer refused'
    Assert-Rejected (Invoke-Case @{MissingChecksum=$true}) '*exactly one asset*' 'missing checksum refused'
    Assert-Rejected (Invoke-Case @{DuplicateChecksum=$true}) '*exactly one asset*' 'ambiguous checksum refused'
    Assert-Rejected (Invoke-Case @{WrongChecksumName=$true}) '*one SHA256 hash*' 'checksum filename enforced'
    Assert-Rejected (Invoke-Case @{MultipleChecksums=$true}) '*one SHA256 hash*' 'ambiguous checksum contents refused'
    Assert-Rejected (Invoke-Case @{TagVersion='0.9.0'} @{Version='0.8.0'}) '*different version*' 'release version mismatch refused'
    Assert-Rejected (Invoke-Case @{Draft=$true}) '*unpublished*' 'draft release refused'
    Assert-Rejected (Invoke-Case @{Prerelease=$true}) '*prerelease*' 'latest prerelease refused'
    foreach ($value in @('v0.8.0','0.8','01.8.0','0.8.0;start-process calc','0.8.0-01')) {
        $state = Invoke-Case @{} @{Version=$value}
        Assert-Rejected $state '*Invalid release version*' "invalid version $value"
        Assert ($state.api.Count -eq 0) 'invalid version refused before network'
    }
    foreach ($url in @('http://github.com/scwlkr/RevitThyme/releases/download/v0.8.0/RevitThyme-0.8.0-win-x64.exe',
        'https://example.com/setup.exe','https://github.com/other/RevitThyme/releases/download/v0.8.0/RevitThyme-0.8.0-win-x64.exe',
        'https://github.com/scwlkr/RevitThyme/releases/download/v0.8.0/RevitThyme-0.8.0-win-x64.exe?other=1')) {
        $state = Invoke-Case @{ExeUrl=$url}
        Assert-Rejected $state '*unexpected release URL*' 'noncanonical installer URL refused'
        Assert ($state.downloads.Count -eq 0) 'invalid URL refused before downloads'
    }
    Assert-Rejected (Invoke-Case @{ChecksumUrl='https://example.com/checksum'}) '*unexpected release URL*' 'checksum origin enforced'
    foreach ($redirect in @('http://release-assets.githubusercontent.com/github-production-release-asset/file',
        'https://example.com/setup.exe','https://github.com/other/repo/setup.exe')) {
        $state = Invoke-Case @{Redirect=$redirect}
        Assert-Rejected $state '*unexpected release URL*' 'unsafe redirect refused'
        Assert ($state.downloads.Count -eq 1) 'unsafe redirect never fetched'
    }
    $state = Invoke-Case @{Redirect='https://release-assets.githubusercontent.com/github-production-release-asset/1/setup?signature=mock'}
    Assert (-not $state.error -and $state.started.Count -eq 1 -and $state.downloads.Count -eq 4) 'official HTTPS CDN redirects supported'
    $state = Invoke-Case @{Redirect='https://release-assets.githubusercontent.com/github-production-release-asset/1/setup';RedirectException=$true}
    Assert (-not $state.error -and $state.started.Count -eq 1) 'redirect exceptions and HTTP response headers supported'
    $state = Invoke-Case @{Redirect='https://release-assets.githubusercontent.com/github-production-release-asset/1/setup';RedirectNonTerminating=$true}
    Assert (-not $state.error -and $state.started.Count -eq 1 -and $state.downloads.Count -eq 4) 'Windows PowerShell redirect response survives its nonterminating error'
    $destination = Join-Path $run 'offline deployment'
    $state = Invoke-Case @{} @{DownloadOnly=$true;Destination=$destination}
    $download = Join-Path $destination 'RevitThyme-0.8.0-win-x64.exe'
    Assert (-not $state.error -and $state.started.Count -eq 0 -and (Test-Path -LiteralPath $download)) 'download-only retains verified setup without executing'
    Assert (Test-Path -LiteralPath ($download+'.sha256')) 'download-only retains checksum'
    $before = (Get-FileHash -LiteralPath $download).Hash
    Assert-Rejected (Invoke-Case @{} @{DownloadOnly=$true;Destination=$destination}) '*existing files were preserved*' 'existing offline download preserved'
    Assert ((Get-FileHash -LiteralPath $download).Hash -eq $before) 'existing offline installer unchanged'
    Assert-Rejected (Invoke-Case @{} @{Destination=$destination}) '*requires -DownloadOnly*' 'invalid destination option refused'
    $state = Invoke-Case @{ExitCode=23;SetupLog=$true}
    Assert ($state.error -like '*exit code 23*' -and $state.started.Count -eq 1) 'setup failure code propagated'
    Assert (-not (Test-Path -LiteralPath $state.started[0].path)) 'failed setup cleans downloaded exe'
    $warning = [string]@($state.warnings | Where-Object { [string]$_ -like 'Setup log retained at *' })[0]
    $retainedLog = $warning.Substring('Setup log retained at '.Length)
    Assert ((Test-Path -LiteralPath $retainedLog) -and [IO.File]::ReadAllText($retainedLog) -eq 'mock setup failure') 'failed setup log retained'
    Remove-Item -LiteralPath $retainedLog
    Write-Output "Passed $($checkCounter.Count) bootstrap checks under PowerShell $($PSVersionTable.PSVersion); no network or real installer executed."
} finally {
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($run)) -eq $expectedParent -and
        (Split-Path $run -Leaf) -match '^RevitThyme-bootstrap-check-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $run -Recurse -Force
    }
}
