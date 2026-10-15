param([string]$ToolPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$developmentFile = $null; $settings = $null
if (-not $ToolPath) {
    . (Join-Path $PSScriptRoot 'host-path.ps1')
    $developmentFile = Assert-NativeHostPath (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\RevitThyme\cli\development.json')
    $settings = @{schema_version=1;kind='revitthyme-development';checkout=$root}
    if (Test-Path -LiteralPath $developmentFile) {
        $settings = Get-Content -LiteralPath $developmentFile -Raw | ConvertFrom-Json -AsHashtable
        if ($settings.kind -ne 'revitthyme-development' -or $settings.schema_version -ne 1) { throw 'Unknown development-link configuration; preserve it before installing.' }
        $settings.checkout = $root
    }
}
$revision = git -C $root rev-parse --short=12 HEAD
if ($LASTEXITCODE) { throw 'Cannot identify the development revision.' }
[xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
$packageVersion = $version + '-dev.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '.' + $revision
$feed = Join-Path $root ('artifacts\cli-tools\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $feed -Force | Out-Null
dotnet pack (Join-Path $root 'src\RevitThyme.Cli') -c Release -o $feed "-p:PackageVersion=$packageVersion" --nologo
if ($LASTEXITCODE) { throw 'CLI tool package failed.' }
# This local feed is the only package source; do not resolve a similarly named public package.
$config = Join-Path $feed 'NuGet.Config'
[IO.File]::WriteAllText($config, '<configuration><packageSources><clear /></packageSources></configuration>')
[string[]]$location = if ($ToolPath) { @('--tool-path', [IO.Path]::GetFullPath($ToolPath)) } else { @('--global') }
dotnet tool update RevitThyme.Cli @location --version $packageVersion --add-source $feed --configfile $config --allow-downgrade
if ($LASTEXITCODE) { throw 'CLI tool installation failed. Existing tool state is managed by dotnet tool.' }
if (-not $ToolPath) {
    New-Item -ItemType Directory -Path (Split-Path $developmentFile -Parent) -Force | Out-Null
    $settings | ConvertTo-Json | Set-Content -LiteralPath $developmentFile -Encoding utf8
}
$commandDirectory = if ($ToolPath) { [IO.Path]::GetFullPath($ToolPath) } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet\tools' }
Write-Output "rthyme installed at $commandDirectory"
if (-not $ToolPath -and -not (($env:PATH -split ';').TrimEnd('\') -contains $commandDirectory.TrimEnd('\'))) {
    Write-Output 'Open a new PowerShell window to pick up the .NET tools PATH entry.'
}
