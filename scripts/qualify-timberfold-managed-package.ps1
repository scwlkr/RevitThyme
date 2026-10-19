#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstalledDirectory,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($path in @($InstalledDirectory, $PackageDirectory, $Destination)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Use explicit absolute payload/package/evidence paths.' }
}
$InstalledDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($InstalledDirectory))
$PackageDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($PackageDirectory))
$Destination = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Destination))
if (Test-Path -LiteralPath $Destination) { throw 'Use a fresh evidence directory; existing evidence is preserved.' }
$fixtureDirectory = Join-Path $repo 'tests\Unit\Fixtures'
foreach ($root in @($InstalledDirectory, $PackageDirectory, $fixtureDirectory)) {
    $relative = [IO.Path]::GetRelativePath($root, $Destination)
    if ($relative -eq '.' -or (-not [IO.Path]::IsPathRooted($relative) -and $relative -ne '..' -and -not $relative.StartsWith('..\'))) {
        throw 'Evidence must remain outside installed/package/fixture roots.'
    }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Missing input directory: $root" }
}
$signatures = foreach ($root in @($InstalledDirectory, $PackageDirectory)) {
    $manifest = Join-Path $root 'manifest.json'
    if ((Get-FileHash -LiteralPath $manifest).Hash -ne $ExpectedManifestSha256) { throw "Manifest does not match explicit pin: $manifest" }
    $core = Join-Path $root 'RevitThyme.Core.dll'
    $signature = Get-AuthenticodeSignature -LiteralPath $core
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) { throw "Core Authenticode signature is not valid: $core" }
    [pscustomobject]@{Path=$core;Status=$signature.Status.ToString();Subject=$signature.SignerCertificate.Subject;Thumbprint=$signature.SignerCertificate.Thumbprint}
}
if ($signatures[0].Thumbprint -ne $signatures[1].Thumbprint) { throw 'Package and installed Core publishers differ.' }
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$project = Join-Path $repo 'tests\TimberFoldManaged\RevitThyme.TimberFold.Managed.csproj'
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$buildOutput = & $dotnet build $project -c Release --nologo --ignore-failed-sources "-p:ManagedPayloadDirectory=$InstalledDirectory" 2>&1
$buildCode = $LASTEXITCODE
$buildOutput | Set-Content -LiteralPath (Join-Path $Destination 'build.log') -Encoding utf8NoBOM
if ($buildCode -ne 0) { throw "Managed verifier build failed; inspect $Destination\build.log" }
$executable = Join-Path $repo 'tests\TimberFoldManaged\bin\Release\net10.0\RevitThyme.TimberFold.Managed.dll'
$restrictedPath = @($env:WINDIR, (Join-Path $env:WINDIR 'System32'), (Join-Path $env:WINDIR 'System32\Wbem')) -join ';'
$start = [Diagnostics.ProcessStartInfo]::new($dotnet)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['PATH'] = $restrictedPath
foreach ($argument in @($executable, $InstalledDirectory, $PackageDirectory, $ExpectedManifestSha256, $fixtureDirectory, (Join-Path $Destination 'replay'))) {
    $start.ArgumentList.Add($argument)
}
$launcher = [ordered]@{schema=1;dotnet=$dotnet;verifier=$executable;verifierSha256=(Get-FileHash -LiteralPath $executable).Hash;
    expectedManifestSha256=$ExpectedManifestSha256;runtimePath=$restrictedPath;authenticode=$signatures;
    actualRevitNativeQualificationOutstanding=$true;physicalOutstanding=$true}
$launcher | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'launcher.json') -Encoding utf8NoBOM
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) { throw "Managed replay exceeded 180 seconds; PID $($process.Id), retain its evidence and inspect before retrying." }
    $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $Destination 'stdout.log') -Encoding utf8NoBOM
    $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $Destination 'stderr.log') -Encoding utf8NoBOM
    if ($process.ExitCode -ne 0) { throw "Managed replay failed with exit $($process.ExitCode); inspect $Destination" }
} finally { $process.Dispose() }
Get-Content -LiteralPath (Join-Path $Destination 'stdout.log')
Write-Output (Join-Path $Destination 'replay\qualification.json')
