param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'packages' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $root 'project.cmd') build
if ($LASTEXITCODE) { throw 'Native build failed.' }
$name = 'RevitThyme-0.9.0-agent-' + [Guid]::NewGuid().ToString('N')
$folder = Join-Path $output $name
New-Item -ItemType Directory -Path (Join-Path $folder 'cli') -Force | Out-Null
$native = Join-Path $root 'src\RevitThyme.Addin\bin\Release\net10.0-windows'
foreach ($file in @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','RevitThyme.Codex.dll','RevitThyme.Addin.deps.json','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
    Copy-Item -LiteralPath (Join-Path $native $file) -Destination (Join-Path $folder $file)
}
$publish = Join-Path $root ('artifacts\foundation\publish-'+[Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $root 'src\RevitThyme.Cli') -c Release --no-self-contained -o $publish --nologo
if ($LASTEXITCODE) { throw 'CLI publish failed.' }
foreach ($file in @('revitthyme.exe','revitthyme.dll','revitthyme.deps.json','revitthyme.runtimeconfig.json','RevitThyme.Core.dll','update.ps1')) {
    Copy-Item -LiteralPath (Join-Path $publish $file) -Destination (Join-Path (Join-Path $folder 'cli') $file)
}
$mcpPublish = Join-Path $root ('artifacts\mcp\publish-'+[Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $root 'src\RevitThyme.Mcp') -c Release --no-self-contained -o $mcpPublish --nologo
if ($LASTEXITCODE) { throw 'MCP publish failed.' }
. (Join-Path $PSScriptRoot 'mcp-files.ps1')
New-Item -ItemType Directory -Path (Join-Path $folder 'mcp') -Force | Out-Null
foreach ($file in Get-McpPackageFiles) { Copy-Item -LiteralPath (Join-Path $mcpPublish $file) -Destination (Join-Path (Join-Path $folder 'mcp') $file) }
foreach ($file in @('install.ps1','uninstall.ps1','common.ps1','mcp-files.ps1','agent-files.ps1','prerequisites.ps1','setup-entry.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $folder $file) }
& (Join-Path $PSScriptRoot 'codex-runtime.ps1') -Destination (Join-Path $folder 'codex')
Copy-Item -LiteralPath (Join-Path $root 'skills') -Destination (Join-Path $folder 'skills') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs\AGENT.md') -Destination (Join-Path $folder 'AGENT.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\UPDATES.md') -Destination (Join-Path $folder 'UPDATES.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bootstrap.ps1') -Destination (Join-Path $folder 'update.ps1')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $folder 'THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\MCP.md') -Destination (Join-Path $folder 'MCP.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\NATIVE-UTILITIES.md') -Destination (Join-Path $folder 'NATIVE-UTILITIES.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\DYNAMIC-EXECUTION.md') -Destination (Join-Path $folder 'DYNAMIC-EXECUTION.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'notices') -Destination (Join-Path $folder 'notices') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'scripts\host-path.ps1') -Destination (Join-Path $folder 'host-path.ps1')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $folder 'LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'docs\INSTALLATION.md') -Destination (Join-Path $folder 'README.md')
$files = @{}; Get-ChildItem -LiteralPath $folder -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($folder.Length+1).Replace('\','/')
    if ($_.Name -match '^(RevitAPI|RevitAPIUI|RevitThyme.RevitIntegration)' -or $_.Extension -in '.rvt','.rfa','.rte') { throw 'Proprietary/test/model artifact in package.' }
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
}
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
@{version='0.9.0-agent';product_version=[string]$buildProperties.Project.PropertyGroup.Version;revit_year='2027';supported_revit_years=@('2027');codex_version='0.162.1';files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
$zip = $folder+'.zip'; Compress-Archive -LiteralPath $folder -DestinationPath $zip
(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()+'  '+(Split-Path $zip -Leaf) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output $folder
Write-Output $zip
