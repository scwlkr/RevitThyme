#Requires -Version 5.1
<#
.SYNOPSIS
Downloads, verifies, and runs the official RevitThyme Windows installer.
.EXAMPLE
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/scwlkr/RevitThyme/main/installer/bootstrap.ps1))) -Silent
.EXAMPLE
.\bootstrap.ps1 -Version 0.8.0 -DownloadOnly -Destination C:\Offline
#>
[CmdletBinding()]
param([string]$Version, [switch]$Silent, [switch]$DownloadOnly, [string]$Destination)
$ErrorActionPreference = 'Stop'

function Assert-RevitThymeVersion([string]$Value) {
    $number = '(0|[1-9][0-9]*)'
    $identifier = '(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    if ($Value.Length -gt 128 -or $Value -cnotmatch "^$number\.$number\.$number(-$identifier(\.$identifier)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$") {
        throw "Invalid release version. Use a semantic version such as 0.8.0, without a leading v."
    }
}

function Assert-RevitThymeUrl([string]$Value, [string]$Expected) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Fragment -or
        ($Expected -and $uri.AbsoluteUri -cne $Expected) -or
        (-not $Expected -and ($uri.Host -ne 'release-assets.githubusercontent.com' -or
            -not $uri.AbsolutePath.StartsWith('/github-production-release-asset/')))) {
        throw 'Refused an unexpected release URL. Only official HTTPS GitHub release downloads are allowed.'
    }
    return $uri
}

function Get-RevitThymeAsset($Release, [string]$Name, [string]$Tag) {
    $matches = @($Release.assets | Where-Object { $_.name -ceq $Name })
    if ($matches.Count -ne 1) { throw "Release must contain exactly one asset named $Name." }
    $expected = "https://github.com/scwlkr/RevitThyme/releases/download/$Tag/$Name"
    $null = Assert-RevitThymeUrl $matches[0].browser_download_url $expected
    return $matches[0]
}

function Save-RevitThymeAsset($Asset, [string]$Path) {
    # GitHub's asset endpoint redirects to its signed CDN URL. Inspect every hop;
    # automatic redirects could otherwise change the scheme or origin unnoticed.
    $url = [string]$Asset.browser_download_url
    for ($hop = 0; $hop -lt 6; $hop++) {
        $response = $null
        $requestErrors = @()
        try {
            # Windows PowerShell emits the redirect response plus a nonterminating error.
            $response = Invoke-WebRequest -Uri $url -OutFile $Path -UseBasicParsing -PassThru -MaximumRedirection 0 -TimeoutSec 120 -ErrorAction SilentlyContinue -ErrorVariable requestErrors
        } catch {
            $response = $_.Exception.Response
            if (-not $response -or [int]$response.StatusCode -notin @(301,302,303,307,308)) { throw }
        }
        if (-not $response -and $requestErrors.Count) { $response = $requestErrors[0].Exception.Response }
        if ($requestErrors.Count -and (-not $response -or [int]$response.StatusCode -notin @(301,302,303,307,308))) { throw $requestErrors[0] }
        if ([int]$response.StatusCode -eq 200) { return }
        if ([int]$response.StatusCode -notin @(301,302,303,307,308)) { throw 'GitHub release download did not return HTTP 200.' }
        $location = $response.Headers['Location']
        if (-not $location) { $location = $response.Headers.Location }
        $next = [Uri]::new([Uri]$url, [string]$location)
        $null = Assert-RevitThymeUrl $next.AbsoluteUri
        $url = $next.AbsoluteUri
    }
    throw 'GitHub release download exceeded the redirect limit.'
}

function Assert-RevitThymeDigest($Asset, [string]$Path) {
    if (-not $Asset.digest) { return }
    if ([string]$Asset.digest -notmatch '^sha256:([0-9A-Fa-f]{64})$') { throw 'Unsupported or invalid GitHub asset digest.' }
    $expected = $Matches[1]
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $expected) {
        throw "GitHub SHA256 digest mismatch for $($Asset.name). Installation stopped."
    }
}

if ($Destination -and -not $DownloadOnly) { throw '-Destination requires -DownloadOnly.' }
if ($Version) { Assert-RevitThymeVersion $Version }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$temporary = Join-Path $tempRoot ('RevitThyme-bootstrap-' + [Guid]::NewGuid().ToString('N'))
$setupLog = Join-Path $temporary 'setup.log'
$succeeded = $false
$ownsTemporary = $false
$protocol = [Net.ServicePointManager]::SecurityProtocol
try {
    [Net.ServicePointManager]::SecurityProtocol = $protocol -bor [Net.SecurityProtocolType]::Tls12
    $endpoint = 'https://api.github.com/repos/scwlkr/RevitThyme/releases/latest'
    if ($Version) { $endpoint = 'https://api.github.com/repos/scwlkr/RevitThyme/releases/tags/v' + $Version }
    $release = Invoke-RestMethod -Uri $endpoint -Headers @{Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'} -UserAgent 'RevitThyme-bootstrap' -MaximumRedirection 0 -TimeoutSec 60
    $tag = [string]$release.tag_name
    if (-not $tag.StartsWith('v')) { throw 'GitHub release tag must begin with v.' }
    $resolved = $tag.Substring(1)
    Assert-RevitThymeVersion $resolved
    if ($Version -and $resolved -cne $Version) { throw 'GitHub returned a release with a different version.' }
    if ($release.draft -or (-not $Version -and $release.prerelease)) { throw 'Refused an unpublished or unexpected prerelease.' }
    $name = "RevitThyme-$resolved-win-x64.exe"
    $asset = Get-RevitThymeAsset $release $name $tag
    $checksum = Get-RevitThymeAsset $release ($name + '.sha256') $tag
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $ownsTemporary = $true
    $setup = Join-Path $temporary $name
    $hashFile = Join-Path $temporary ($name + '.sha256')
    Save-RevitThymeAsset $checksum $hashFile
    if ((Get-Item -LiteralPath $hashFile).Length -gt 4096) { throw 'Release checksum file is too large.' }
    Assert-RevitThymeDigest $checksum $hashFile
    $hashText = [IO.File]::ReadAllText($hashFile).Trim()
    if ($hashText -notmatch ('^([0-9A-Fa-f]{64})(?:[ \t]+\*?' + [regex]::Escape($name) + ')?$')) {
        throw 'Release checksum must contain one SHA256 hash for the expected installer.'
    }
    $expectedHash = $Matches[1]
    Save-RevitThymeAsset $asset $setup
    if ((Get-Item -LiteralPath $setup).Length -eq 0 -or
        (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ine $expectedHash) {
        throw 'Installer SHA256 checksum mismatch. Installation stopped.'
    }
    Assert-RevitThymeDigest $asset $setup
    if ($DownloadOnly) {
        if (-not $Destination) { $Destination = (Get-Location).Path }
        $folder = [IO.Path]::GetFullPath($Destination)
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $target = Join-Path $folder $name
        $targetHash = Join-Path $folder ($name + '.sha256')
        if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $targetHash)) { throw 'Download destination already contains an installer or checksum; existing files were preserved.' }
        [IO.File]::Copy($setup, $target, $false)
        try { [IO.File]::Copy($hashFile, $targetHash, $false) }
        catch { Remove-Item -LiteralPath $target; throw }
        Write-Output "Verified installer: $target"
    } else {
        $arguments = @('/NORESTART', ('/LOG="' + $setupLog + '"'))
        if ($Silent) { $arguments += @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-') }
        $process = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "RevitThyme setup failed with exit code $($process.ExitCode)." }
        Write-Output "RevitThyme $resolved setup completed."
    }
    $succeeded = $true
} finally {
    [Net.ServicePointManager]::SecurityProtocol = $protocol
    if ($ownsTemporary -and -not $succeeded -and (Test-Path -LiteralPath $setupLog)) {
        $savedLog = Join-Path $tempRoot ('RevitThyme-setup-' + [Guid]::NewGuid().ToString('N') + '.log')
        try { Move-Item -LiteralPath $setupLog -Destination $savedLog; Write-Warning "Setup log retained at $savedLog" }
        catch { Write-Warning "Could not preserve setup log; retained download directory at $temporary"; $temporary = $null }
    }
    # Only remove the fresh direct child of the OS temp directory created above.
    if ($ownsTemporary -and $temporary -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($temporary)) -eq $tempRoot -and
        (Split-Path $temporary -Leaf) -match '^RevitThyme-bootstrap-[0-9a-f]{32}$' -and (Test-Path -LiteralPath $temporary)) {
        try { Remove-Item -LiteralPath $temporary -Recurse -Force }
        catch { Write-Warning "Could not remove temporary download directory at $temporary" }
    }
}
