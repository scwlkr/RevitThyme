param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$Isolated
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'common.ps1')
$package = Assert-NativeHostPath $PackageDirectory
$manifest = Assert-FoundationPackage $package
if ($manifest.version -ne '0.8.0-install') { throw 'Setup requires the complete 0.8.0-install package.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts\installer\'+[Guid]::NewGuid().ToString('N')) }
$output = Assert-NativeHostPath $OutputDirectory
if (Test-Path -LiteralPath $output) { throw "Setup output must be a fresh directory; preserve existing output: $output" }
if ($output.StartsWith($package+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq $package) { throw 'Setup output must remain outside the validated package.' }
$source = Join-Path $PSScriptRoot 'RevitThyme.iss'
$bootstrap = Join-Path $PSScriptRoot 'bootstrap.ps1'
if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or -not (Test-Path -LiteralPath $bootstrap -PathType Leaf)) { throw 'Setup source and bootstrapper are required.' }
$compiler = & (Join-Path $PSScriptRoot 'compiler.ps1') -CompilerPath $CompilerPath
if (@($compiler).Count -ne 1 -or -not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Compiler acquisition did not return exactly one executable.' }
New-Item -ItemType Directory -Path $output | Out-Null
$arguments = @("/DPackageDirectory=$package","/DOutputDirectory=$output",'/DProductVersion=0.8.0')
if ($Isolated) { $arguments += '/DIsolatedBuild=1' }
& $compiler @arguments $source | ForEach-Object { [Console]::Error.WriteLine($_) }
if ($LASTEXITCODE) { throw "Windows setup compilation failed: $LASTEXITCODE" }
$name = if ($Isolated) { 'RevitThyme-0.8.0-win-x64-test.exe' } else { 'RevitThyme-0.8.0-win-x64.exe' }
$exe = Join-Path $output $name
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Compiler did not produce the expected setup: $exe" }
$bootstrapTarget = Join-Path $output 'install-revitthyme.ps1'
Copy-Item -LiteralPath $bootstrap -Destination $bootstrapTarget
$hashes = @{}
foreach ($file in @($exe,$bootstrapTarget)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $leaf = Split-Path $file -Leaf
    $hashes[$leaf] = $hash
    ($hash+'  '+$leaf) | Set-Content -LiteralPath ($file+'.sha256') -Encoding ascii
}
$revision = git -C $root rev-parse HEAD
if ($LASTEXITCODE -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source revision for release metadata.' }
$dirty = @(git -C $root status --porcelain)
if ($LASTEXITCODE) { throw 'Cannot identify source changes for release metadata.' }
@{
    schema_version=1;product='RevitThyme';version='0.8.0';package_version=$manifest.version
    platform='win-x64';isolated_build=[bool]$Isolated;supported_revit_years=@($manifest.supported_revit_years)
    source_revision=[string]$revision;source_dirty=($dirty.Count -gt 0)
    package_manifest_sha256=(Get-FileHash -LiteralPath (Join-Path $package 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    compiler_version='6.7.3';files=$hashes
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding utf8
@{setup=$exe;bootstrap=$bootstrapTarget;metadata=(Join-Path $output 'release.json');isolated_build=[bool]$Isolated} | ConvertTo-Json
