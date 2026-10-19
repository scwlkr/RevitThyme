$ErrorActionPreference = 'Stop'
$hostScript = Join-Path $PSScriptRoot 'host-path.ps1'
if (-not (Test-Path -LiteralPath $hostScript)) { $hostScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts\host-path.ps1' }
. $hostScript
. (Join-Path $PSScriptRoot 'prerequisites.ps1')
function Get-FoundationRegistration([string]$Directory) {
    if (-not $Directory) { $Directory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Roaming\Autodesk\Revit\Addins\2027' }
    return Assert-NativeHostPath (Join-Path $Directory 'RevitThyme.Foundation.addin')
}
function Assert-FoundationPackage([string]$Folder) {
    $manifestPath = Join-Path $Folder 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -notin @('0.4.0-foundation','0.5.0-mcp','0.6.0-utilities','0.7.0-dynamic','0.8.0-install','0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1') -or $manifest.revit_year -ne '2027') { throw 'Unsupported RevitThyme package.' }
    $required = @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','cli/revitthyme.exe','cli/revitthyme.dll','cli/revitthyme.deps.json','cli/revitthyme.runtimeconfig.json','cli/RevitThyme.Core.dll','install.ps1','uninstall.ps1','common.ps1','host-path.ps1','LICENSE','README.md')
    if ($manifest.version -in @('0.5.0-mcp','0.6.0-utilities','0.7.0-dynamic','0.8.0-install','0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) {
        . (Join-Path $PSScriptRoot 'mcp-files.ps1')
        $required += @('mcp-files.ps1','THIRD-PARTY-NOTICES.md','MCP.md','notices/MCP-LICENSE.txt','notices/DOTNET-LICENSE.txt')
        $required += @(Get-McpPackageFiles | ForEach-Object { 'mcp/'+$_ })
    }
    if ($manifest.version -in @('0.6.0-utilities','0.7.0-dynamic','0.8.0-install','0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) { $required += 'NATIVE-UTILITIES.md' }
    if ($manifest.version -in @('0.7.0-dynamic','0.8.0-install','0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) { $required += @('RevitThyme.Addin.deps.json','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','DYNAMIC-EXECUTION.md') }
    if ($manifest.version -in @('0.8.0-install','0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) {
        if ((@($manifest.supported_revit_years) -join '|') -ne '2027') { throw 'Unsupported Revit version matrix.' }
        $required += @('prerequisites.ps1','setup-entry.ps1')
    }
    if ($manifest.version -in @('0.9.0-agent','0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) {
        . (Join-Path $PSScriptRoot 'agent-files.ps1')
        $required += Get-AgentPackageFiles
        if ($manifest.codex_version -ne '0.162.1') { throw 'Unsupported Codex runtime.' }
    }
    if ($manifest.version -in @('0.9.1-cli','0.9.2-ribbon','0.9.3-identity','0.10.0-timberfold.1')) { $required += @('global-command.ps1','cli/rthyme.exe') }
    if ($manifest.version -eq '0.10.0-timberfold.1') { $required += 'TIMBERFOLD.md' }
    $names = @($manifest.files.PSObject.Properties.Name)
    if ((($names | Sort-Object) -join '|') -ne (($required | Sort-Object) -join '|')) { throw 'Package file allowlist changed.' }
    $actual = @(Get-ChildItem -LiteralPath $Folder -File -Recurse | ForEach-Object { $_.FullName.Substring($Folder.Length+1).Replace('\','/') } | Where-Object {$_ -ne 'manifest.json'})
    if ((($actual | Sort-Object) -join '|') -ne (($required | Sort-Object) -join '|')) { throw 'Unexpected or missing package files.' }
    foreach ($entry in $manifest.files.PSObject.Properties) {
        $path = Assert-NativeHostPath (Join-Path $Folder $entry.Name)
        if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Package file modified: $($entry.Name)" }
    }
    return $manifest
}
function Read-FoundationRegistration([string]$Path) {
    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $entry = $xml.RevitAddIns.AddIn
    if (@($entry).Count -ne 1 -or $entry.AddInId -ne 'B09CCF69-B916-40B7-8A57-5923E45915D6' -or
        $entry.FullClassName -ne 'RevitThyme.Addin.FoundationApplication' -or $entry.Type -ne 'Application') { throw 'Existing registration is not owned Foundation.' }
    $assembly = Assert-NativeHostPath ([string]$entry.Assembly)
    if ((Split-Path $assembly -Leaf) -ne 'RevitThyme.Addin.dll') { throw 'Unexpected registered assembly.' }
    return Split-Path $assembly -Parent
}
function Assert-IsolatedInstallTest([string]$InstallDirectory, [string]$AddinsDirectory) {
    if (-not $InstallDirectory -or -not $AddinsDirectory) { throw 'Isolated install tests require both exact directories.' }
    $install = Assert-NativeHostPath $InstallDirectory
    $addins = Assert-NativeHostPath $AddinsDirectory
    $run = Split-Path $install -Parent
    $id = [Guid]::Empty
    if ((Split-Path $install -Leaf) -ne 'installed' -or $addins -ne (Join-Path $run 'addins') -or
        (Split-Path (Split-Path $run -Parent) -Leaf) -ne 'install-test' -or -not [Guid]::TryParse((Split-Path $run -Leaf), [ref]$id)) {
        throw 'Isolated tests require one exact UUID/install-test folder, with installed and addins children.'
    }
    return $run
}
