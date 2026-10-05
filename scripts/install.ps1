[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Icons')][string]$Action = 'Install',
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ExtensionsRoot = (Join-Path $env:APPDATA 'pyRevit\Extensions')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-path.ps1')
$extensionName = 'RevitThyme.extension'
$parent = Assert-NativeHostPath $ExtensionsRoot
$destination = [IO.Path]::GetFullPath((Join-Path $parent $extensionName))
if ((Split-Path $destination -Parent) -ne $parent) { throw 'Unsafe extension destination' }
$recordPath = Join-Path $destination 'install-record.json'
$backupRoot = Join-Path (Split-Path $parent -Parent) 'RevitThyme-backups'

function Get-ReleaseHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

function Assert-Owned {
    if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) {
        throw 'Existing extension is not managed by this installer. Preserve it manually.'
    }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.product -ne 'RevitThyme') { throw 'Invalid installation ownership' }
    $expected = @('install-record.json') + @($record.files.PSObject.Properties.Name)
    $actual = @(Get-ChildItem -LiteralPath $destination -File -Recurse)
    foreach ($file in $actual) {
        $relative = $file.FullName.Substring($destination.Length + 1).Replace('\', '/')
        if ($relative -notin $expected) { throw "Unmanaged file in extension: $relative" }
        if ($relative -ne 'install-record.json') {
            $hash = Get-ReleaseHash $file.FullName
            if ($hash -ne $record.files.PSObject.Properties[$relative].Value) {
                throw "Modified extension file must be preserved manually: $relative"
            }
        }
    }
    return $record
}

if (Test-Path -LiteralPath $destination) {
    $previous = Assert-Owned
    if ($Action -ne 'Icons' -and (Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
        throw 'Close Revit before updating or uninstalling an existing extension.'
    }
}
if ($Action -eq 'Uninstall') {
    if (-not (Test-Path -LiteralPath $destination)) { Write-Output 'RevitThyme is already uninstalled'; exit 0 }
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ('removed-' + [Guid]::NewGuid().ToString('N'))
    Move-Item -LiteralPath $destination -Destination $backup
    Write-Output "Uninstalled RevitThyme; recoverable code: $backup; user settings retained"
    exit 0
}

$package = [IO.Path]::GetFullPath($PackageRoot)
$release = Get-Content -LiteralPath (Join-Path $package 'release.json') -Raw | ConvertFrom-Json
if ($release.product -ne 'RevitThyme' -or $release.schema_version -ne 1) { throw 'Invalid release manifest' }
$source = Join-Path $package ('extensions\' + $extensionName)
$prefix = 'extensions/' + $extensionName + '/'
$files = @{}
foreach ($item in $release.files.PSObject.Properties) {
    $file = [IO.Path]::GetFullPath((Join-Path $package $item.Name))
    if (-not $file.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release file escapes package'
    }
    $hash = Get-ReleaseHash $file
    if ($hash -ne $item.Value) { throw "Package checksum mismatch: $($item.Name)" }
    if ($item.Name.StartsWith($prefix)) {
        $relative = $item.Name.Substring($prefix.Length)
        if ([IO.Path]::IsPathRooted($relative) -or @($relative -split '[\\/]') -contains '..') {
            throw 'Unsafe extension payload path'
        }
        $files[$relative] = $hash
    }
}
if ($files.Count -eq 0) { throw 'No extension payload in release' }
if ($Action -eq 'Icons') {
    if (-not $previous) { throw 'Install RevitThyme before refreshing its icons' }
    if ($previous.version -ne $release.version) { throw 'Icon refresh requires the installed release version' }
    $icons = @($files.Keys | Where-Object { $_ -match '\.pushbutton/icon(\.dark)?\.png$' })
    if ($icons.Count -eq 0) { throw 'No ribbon icons in package' }
    $existingFiles = @($previous.files.PSObject.Properties.Name)
    $unchanged = @($files.Keys | Where-Object { $_ -notin $icons })
    if (@($existingFiles | Where-Object { $_ -notin $files.Keys }).Count -gt 0) {
        throw 'Icon refresh cannot remove installed files'
    }
    foreach ($name in $unchanged) {
        $installedFile = Join-Path $destination $name
        if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf) -or
            $name -notin $existingFiles -or (Get-ReleaseHash $installedFile) -ne $files[$name]) {
            throw "Icon refresh cannot change extension code: $name"
        }
    }
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ('icons-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    Copy-Item -LiteralPath $recordPath -Destination (Join-Path $backup 'install-record.json')
    foreach ($name in $icons) {
        $target = Join-Path $destination $name
        if (Test-Path -LiteralPath $target) {
            $saved = Join-Path $backup $name
            New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
    }
    try {
        foreach ($name in $icons) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name) }
        @{ product = 'RevitThyme'; version = $release.version; files = $files } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $recordPath -Encoding UTF8
    } catch {
        foreach ($name in $icons) {
            $target = Join-Path $destination $name
            $saved = Join-Path $backup $name
            if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
        }
        Copy-Item -LiteralPath (Join-Path $backup 'install-record.json') -Destination $recordPath
        throw
    }
    Write-Output "Refreshed $($icons.Count) ribbon icons: $destination. Display them on the next normal Revit start. Backup: $backup"
    exit 0
}
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage = Join-Path $parent ('RevitThyme-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in $files.Keys) {
    $target = [IO.Path]::GetFullPath((Join-Path $stage $name))
    if (-not $target.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Extension file escapes staging directory'
    }
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target
}
$installRecord = @{ product = 'RevitThyme'; version = $release.version; files = $files }
$installRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'install-record.json') -Encoding UTF8
$backup = $null
try {
    if (Test-Path -LiteralPath $destination) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backup = Join-Path $backupRoot ($previous.version + '-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $destination -Destination $backup
    }
    Move-Item -LiteralPath $stage -Destination $destination
} catch {
    if ($backup -and -not (Test-Path -LiteralPath $destination)) {
        Move-Item -LiteralPath $backup -Destination $destination
    }
    throw
}
Write-Output "Installed RevitThyme $($release.version): $destination. Load the ribbon on the next normal Revit start."
