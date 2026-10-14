param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'scripts\host-path.ps1')
$version = '6.7.3'
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$uri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'

function Assert-CompilerPublisher([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)CN=Pyrsys B\.V\.(?:,|$)') { throw "Expected a valid Pyrsys B.V. signature: $Path" }
}

function Assert-Compiler([string]$Path) {
    $full = Assert-NativeHostPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf) -or (Split-Path $full -Leaf) -ne 'ISCC.exe') { throw 'Expected an existing Inno Setup ISCC.exe.' }
    Assert-CompilerPublisher $full
    # Official ISCC resources contain 0.0.0.0; the signed compiler reports its engine version when parsing.
    # An intentional preprocessing error obtains the header without creating output or executing setup.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $full
    $start.Arguments = '-'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $probe = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $probe.StandardOutput.ReadToEndAsync()
        $stderr = $probe.StandardError.ReadToEndAsync()
        $probe.StandardInput.WriteLine('#error RevitThyme compiler version probe')
        $probe.StandardInput.Close()
        if (-not $probe.WaitForExit(15000)) { throw "Compiler version probe exceeded 15 seconds (PID $($probe.Id))." }
        $header = $stdout.GetAwaiter().GetResult()+[Environment]::NewLine+$stderr.GetAwaiter().GetResult()
        if ($header -notmatch '(?m)^Compiler engine version: Inno Setup 6\.7\.3\s*$') { throw "Signed compiler did not identify engine version $version." }
    } finally { $probe.Dispose() }
    return $full
}

function Write-CompilerReceipt([string]$Path, [string]$Compiler) {
    @{version=$version;installer_sha256=$expectedHash;compiler_sha256=(Get-FileHash -LiteralPath $Compiler -Algorithm SHA256).Hash.ToLowerInvariant();source=$uri} |
        ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding utf8
}

if ($CompilerPath) { Assert-Compiler $CompilerPath; return }
$tools = Assert-NativeHostPath (Join-Path $root 'artifacts\buildtools')
$install = Assert-NativeHostPath (Join-Path $tools 'innosetup-6.7.3')
$compiler = Join-Path $install 'ISCC.exe'
$receipt = Join-Path $install 'revitthyme-tool.json'
if (Test-Path -LiteralPath $compiler) {
    $null = Assert-Compiler $compiler
    if (-not (Test-Path -LiteralPath $receipt -PathType Leaf)) {
        # Recovery is bounded to the project-local pinned installer retained by an interrupted acquisition.
        $downloads = @(Get-ChildItem -LiteralPath $tools -File -Filter 'innosetup-6.7.3-*.exe' | Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expectedHash })
        if (-not $downloads.Count) { throw 'Project-local compiler lacks an acquisition receipt and the exact pinned installer. Supply -CompilerPath or review the preserved folder.' }
        Assert-CompilerPublisher $downloads[0].FullName
        Write-CompilerReceipt $receipt $compiler
    }
    $record = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if ($record.version -ne $version -or $record.installer_sha256 -ne $expectedHash -or
        $record.compiler_sha256 -ne (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Project-local compiler differs from its verified acquisition receipt.' }
    Write-Output $compiler
    return
}
if (Test-Path -LiteralPath $install) { throw "Preserve incomplete or unknown tool folder: $install. Supply -CompilerPath or review the folder before retrying." }
New-Item -ItemType Directory -Path $tools -Force | Out-Null
$download = Assert-NativeHostPath (Join-Path $tools ('innosetup-6.7.3-'+[Guid]::NewGuid().ToString('N')+'.exe'))
Invoke-WebRequest -Uri $uri -OutFile $download -UseBasicParsing
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw "Inno Setup download checksum mismatch. Preserved: $download" }
Assert-CompilerPublisher $download
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS','/TASKS=""',('/DIR="'+$install+'"'))
$process = Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { throw "Project-local compiler installation exceeded two minutes (PID $($process.Id)); inspect it before retrying." }
if ($process.ExitCode -ne 0) { throw "Project-local compiler installation failed: $($process.ExitCode)." }
$null = Assert-Compiler $compiler
Write-CompilerReceipt $receipt $compiler
Write-Output $compiler
