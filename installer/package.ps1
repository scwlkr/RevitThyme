param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'packages' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $root 'project.cmd') build
if ($LASTEXITCODE) { throw 'Native build failed.' }
$name = 'RevitThyme-0.4.0-foundation-' + [Guid]::NewGuid().ToString('N')
$folder = Join-Path $output $name
New-Item -ItemType Directory -Path (Join-Path $folder 'cli') -Force | Out-Null
$native = Join-Path $root 'src\RevitThyme.Addin\bin\Release\net10.0-windows'
foreach ($file in @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll')) {
    Copy-Item -LiteralPath (Join-Path $native $file) -Destination (Join-Path $folder $file)
}
$publish = Join-Path $root ('artifacts\foundation\publish-'+[Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $root 'src\RevitThyme.Cli') -c Release --no-self-contained -o $publish --nologo
if ($LASTEXITCODE) { throw 'CLI publish failed.' }
foreach ($file in @('revitthyme.exe','revitthyme.dll','revitthyme.deps.json','revitthyme.runtimeconfig.json','RevitThyme.Core.dll')) {
    Copy-Item -LiteralPath (Join-Path $publish $file) -Destination (Join-Path (Join-Path $folder 'cli') $file)
}
foreach ($file in @('install.ps1','uninstall.ps1','common.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $folder $file) }
Copy-Item -LiteralPath (Join-Path $root 'scripts\host-path.ps1') -Destination (Join-Path $folder 'host-path.ps1')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $folder 'LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'docs\INSTALLATION.md') -Destination (Join-Path $folder 'README.md')
$files = @{}; Get-ChildItem -LiteralPath $folder -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($folder.Length+1).Replace('\','/')
    if ($_.Name -match '^(RevitAPI|RevitAPIUI|RevitThyme.RevitIntegration)' -or $_.Extension -in '.rvt','.rfa','.rte') { throw 'Proprietary/test/model artifact in package.' }
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
}
@{version='0.4.0-foundation';revit_year='2027';files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
$zip = $folder+'.zip'; Compress-Archive -LiteralPath $folder -DestinationPath $zip
(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()+'  '+(Split-Path $zip -Leaf) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output $folder
Write-Output $zip
