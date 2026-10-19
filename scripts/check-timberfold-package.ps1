param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'installer\common.ps1')
. (Join-Path $root 'installer\mcp-files.ps1')
. (Join-Path $root 'installer\agent-files.ps1')
$run = Join-Path $root ('artifacts\foundation\package-check\'+[Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $run | Out-Null
$checks = [Collections.Generic.List[object]]::new()

function Check([string]$Name, [scriptblock]$Body) {
    & $Body
    $checks.Add(@{name=$Name;passed=$true})
    Write-Output "PASS $Name"
}
function Refused([scriptblock]$Body, [string]$Pattern) {
    $failure = $null
    try { $null = & $Body } catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notmatch $Pattern) { throw "Expected refusal $Pattern; got: $failure" }
}
function New-PackageFixture([string]$Version, [switch]$TimberFold, [switch]$Fabrication, [switch]$CutFiles) {
    $folder = Join-Path $run ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $folder | Out-Null
    # Nonexecutable payloads exercise the package boundary without installation or native execution.
    $files = @('RevitThyme.Addin.dll','RevitThyme.Core.dll','RevitThyme.UI.dll','cli/revitthyme.exe','cli/revitthyme.dll',
        'cli/revitthyme.deps.json','cli/revitthyme.runtimeconfig.json','cli/RevitThyme.Core.dll','install.ps1','uninstall.ps1',
        'common.ps1','host-path.ps1','LICENSE','README.md','mcp-files.ps1','THIRD-PARTY-NOTICES.md','MCP.md',
        'notices/MCP-LICENSE.txt','notices/DOTNET-LICENSE.txt','NATIVE-UTILITIES.md','RevitThyme.Addin.deps.json',
        'Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','DYNAMIC-EXECUTION.md','prerequisites.ps1',
        'setup-entry.ps1','global-command.ps1','cli/rthyme.exe')
    $files += @(Get-McpPackageFiles | ForEach-Object { 'mcp/'+$_ })
    $files += Get-AgentPackageFiles
    if ($TimberFold) { $files += 'TIMBERFOLD.md' }
    if ($Fabrication) { $files += @('Clipper2Lib.dll','cli/Clipper2Lib.dll','mcp/Clipper2Lib.dll','notices/CLIPPER2-LICENSE.txt') }
    if ($CutFiles) { $files += @('IxMilia.Dxf.dll','cli/IxMilia.Dxf.dll','mcp/IxMilia.Dxf.dll','notices/IXMILIA-DXF-LICENSE.txt') }
    $hashes = @{}
    foreach ($name in $files) {
        $path = Join-Path $folder $name
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'Offline package validator fixture; not executable.')
        $hashes[$name] = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    }
    @{version=$Version;product_version='0.10.0';revit_year='2027';supported_revit_years=@('2027');codex_version='0.162.1';files=$hashes} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
    return $folder
}

Check 'native-kit candidate keeps the complete managed geometry and DXF payload' {
    $candidate = New-PackageFixture '0.10.0-timberfold.4' -TimberFold -Fabrication -CutFiles
    $manifest = Assert-FoundationPackage $candidate
    if ($manifest.version -ne '0.10.0-timberfold.4') { throw 'Wrong native-kit candidate accepted.' }
}
Check 'native-kit candidate refuses a missing managed DXF dependency' {
    $candidate = New-PackageFixture '0.10.0-timberfold.4' -TimberFold -Fabrication -CutFiles
    Remove-Item -LiteralPath (Join-Path $candidate 'mcp/IxMilia.Dxf.dll')
    Refused { Assert-FoundationPackage $candidate } 'Unexpected or missing'
}
Check 'TimberFold cut-file candidate includes managed DXF in all execution payloads' {
    $candidate = New-PackageFixture '0.10.0-timberfold.3' -TimberFold -Fabrication -CutFiles
    $manifest = Assert-FoundationPackage $candidate
    if ($manifest.version -ne '0.10.0-timberfold.3') { throw 'Wrong cut-file candidate accepted.' }
}
foreach ($dependency in @('IxMilia.Dxf.dll','cli/IxMilia.Dxf.dll','mcp/IxMilia.Dxf.dll','notices/IXMILIA-DXF-LICENSE.txt')) {
    Check "cut-file candidate refuses missing $dependency" {
        $candidate = New-PackageFixture '0.10.0-timberfold.3' -TimberFold -Fabrication -CutFiles
        Remove-Item -LiteralPath (Join-Path $candidate $dependency)
        Refused { Assert-FoundationPackage $candidate } 'Unexpected or missing'
    }
    Check "cut-file candidate refuses modified $dependency" {
        $candidate = New-PackageFixture '0.10.0-timberfold.3' -TimberFold -Fabrication -CutFiles
        [IO.File]::WriteAllText((Join-Path $candidate $dependency), 'Modified DXF dependency fixture.')
        Refused { Assert-FoundationPackage $candidate } 'modified'
    }
}
Check 'prior geometry candidate does not silently gain a DXF payload requirement' {
    $prior = New-PackageFixture '0.10.0-timberfold.2' -TimberFold -Fabrication -CutFiles
    Refused { Assert-FoundationPackage $prior } 'allowlist'
}

Check 'TimberFold fabrication candidate requires managed clipping in every execution payload' {
    $candidate = New-PackageFixture '0.10.0-timberfold.2' -TimberFold -Fabrication
    $manifest = Assert-FoundationPackage $candidate
    if ($manifest.version -ne '0.10.0-timberfold.2') { throw 'Wrong fabrication candidate accepted.' }
}
foreach ($dependency in @('Clipper2Lib.dll','cli/Clipper2Lib.dll','mcp/Clipper2Lib.dll','notices/CLIPPER2-LICENSE.txt')) {
    Check "fabrication candidate refuses missing $dependency" {
        $candidate = New-PackageFixture '0.10.0-timberfold.2' -TimberFold -Fabrication
        Remove-Item -LiteralPath (Join-Path $candidate $dependency)
        Refused { Assert-FoundationPackage $candidate } 'Unexpected or missing'
    }
    Check "fabrication candidate refuses modified $dependency" {
        $candidate = New-PackageFixture '0.10.0-timberfold.2' -TimberFold -Fabrication
        [IO.File]::WriteAllText((Join-Path $candidate $dependency), 'Modified dependency fixture.')
        Refused { Assert-FoundationPackage $candidate } 'modified'
    }
}
Check 'TimberFold inspection candidate accepts prior product contents plus its guide' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    $manifest = Assert-FoundationPackage $candidate
    if ($manifest.version -ne '0.10.0-timberfold.1') { throw 'Wrong candidate accepted.' }
}
Check 'candidate requires TimberFold guide' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1'
    Refused { Assert-FoundationPackage $candidate } 'allowlist'
}
Check 'modified TimberFold guide is refused' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    [IO.File]::WriteAllText((Join-Path $candidate 'TIMBERFOLD.md'), 'Changed fixture.')
    Refused { Assert-FoundationPackage $candidate } 'modified'
}
Check 'RevitAPIIFC remains excluded from candidate' {
    $candidate = New-PackageFixture '0.10.0-timberfold.1' -TimberFold
    [IO.File]::WriteAllText((Join-Path $candidate 'RevitAPIIFC.dll'), 'Forbidden proprietary fixture.')
    Refused { Assert-FoundationPackage $candidate } 'Unexpected or missing'
}
Check 'previous identity package remains valid for recovery without TimberFold guide' {
    $previous = New-PackageFixture '0.9.3-identity'
    $manifest = Assert-FoundationPackage $previous
    if ($manifest.version -ne '0.9.3-identity') { throw 'Previous package was not accepted.' }
}
Check 'Clipper2 upstream package and exact managed payload can be verified before packaging' {
    . (Join-Path $root 'installer\clipper2.ps1')
    $packageRoot = & dotnet nuget locals global-packages --list --force-english-output
    if ($LASTEXITCODE -ne 0 -or $packageRoot -notmatch '^global-packages: (.+)$') { throw 'Cannot locate NuGet package cache.' }
    $archive = Join-Path $Matches[1].Trim() 'clipper2\2.0.0\clipper2.2.0.0.nupkg'
    $assembly = Join-Path (Split-Path $archive -Parent) 'lib\netstandard2.0\Clipper2Lib.dll'
    $verified = Assert-Clipper2Payload -ArchivePath $archive -LicensePath (Join-Path $root 'installer\notices\CLIPPER2-LICENSE.txt') -AssemblyPaths @($assembly)
    if ($verified.package_sha256 -ne 'f07dfcf6adccd035fde042f0ff6fd6d746b989e9bb361cda38216f185304172b') { throw 'Unexpected upstream package accepted.' }
    $verified | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'clipper2-provenance.json') -Encoding utf8
    $script:ClipperArchive = $archive
    $script:ClipperAssembly = $assembly
}
. (Join-Path $root 'installer\clipper2.ps1')
Check 'build-time verification refuses a substituted managed Clipper2 assembly' {
    $changed = Join-Path $run 'substituted-Clipper2Lib.dll'
    [IO.File]::WriteAllBytes($changed, [byte[]](1,2,3))
    Refused { Assert-Clipper2Payload -ArchivePath $script:ClipperArchive -LicensePath (Join-Path $root 'installer\notices\CLIPPER2-LICENSE.txt') -AssemblyPaths @($changed) } 'managed payload differs'
}
Check 'build-time verification refuses a changed Clipper2 license' {
    $changed = Join-Path $run 'changed-CLIPPER2-LICENSE.txt'
    [IO.File]::WriteAllText($changed, 'Truncated license fixture.')
    Refused { Assert-Clipper2Payload -ArchivePath $script:ClipperArchive -LicensePath $changed -AssemblyPaths @($script:ClipperAssembly) } 'license notice differs'
}
Check 'build-time verification refuses a changed upstream archive before reading its payload' {
    $changed = Join-Path $run 'changed-clipper2.2.0.0.nupkg'
    Copy-Item -LiteralPath $script:ClipperArchive -Destination $changed
    [IO.File]::AppendAllText($changed, 'Changed archive fixture.')
    Refused { Assert-Clipper2Payload -ArchivePath $changed -LicensePath (Join-Path $root 'installer\notices\CLIPPER2-LICENSE.txt') -AssemblyPaths @($script:ClipperAssembly) } 'package checksum mismatch'
}
. (Join-Path $root 'installer\ixmilia-dxf.ps1')
Check 'IxMilia.Dxf upstream package assembly and complete notice verify before packaging' {
    $packageRoot = & dotnet nuget locals global-packages --list --force-english-output
    if ($LASTEXITCODE -ne 0 -or $packageRoot -notmatch '^global-packages: (.+)$') { throw 'Cannot locate NuGet package cache.' }
    $script:DxfArchive = Join-Path $Matches[1].Trim() 'ixmilia.dxf\0.8.4\ixmilia.dxf.0.8.4.nupkg'
    $script:DxfAssembly = Join-Path (Split-Path $script:DxfArchive -Parent) 'lib\netstandard2.0\IxMilia.Dxf.dll'
    $verified = Assert-IxMiliaDxfPayload -ArchivePath $script:DxfArchive -LicensePath (Join-Path $root 'installer\notices\IXMILIA-DXF-LICENSE.txt') -AssemblyPaths @($script:DxfAssembly)
    if ($verified.assembly_sha256 -ne '70460eae6fd10ca6ca35653cb52eb03c2cbba2240b0afbda98afc744ad5dc67f') { throw 'Unexpected DXF assembly accepted.' }
    $verified | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'ixmilia-dxf-provenance.json') -Encoding utf8
}
Check 'build-time verification refuses a substituted IxMilia.Dxf assembly' {
    $changed = Join-Path $run 'substituted-IxMilia.Dxf.dll'
    [IO.File]::WriteAllBytes($changed, [byte[]](1,2,3))
    Refused { Assert-IxMiliaDxfPayload -ArchivePath $script:DxfArchive -LicensePath (Join-Path $root 'installer\notices\IXMILIA-DXF-LICENSE.txt') -AssemblyPaths @($changed) } 'managed payload differs'
}
Check 'build-time verification refuses a changed IxMilia.Dxf notice' {
    $changed = Join-Path $run 'changed-IXMILIA-DXF-LICENSE.txt'
    [IO.File]::WriteAllText($changed, 'Truncated MIT notice fixture.')
    Refused { Assert-IxMiliaDxfPayload -ArchivePath $script:DxfArchive -LicensePath $changed -AssemblyPaths @($script:DxfAssembly) } 'license notice differs'
}
Check 'build-time verification accepts only line-ending conversion in the complete DXF notice' {
    $converted = Join-Path $run 'crlf-IXMILIA-DXF-LICENSE.txt'
    $notice = [IO.File]::ReadAllText((Join-Path $root 'installer\notices\IXMILIA-DXF-LICENSE.txt')).Replace("`r`n", "`n").Replace("`n", "`r`n")
    [IO.File]::WriteAllText($converted, $notice)
    $null = Assert-IxMiliaDxfPayload -ArchivePath $script:DxfArchive -LicensePath $converted -AssemblyPaths @($script:DxfAssembly)
}
Check 'build-time verification refuses a changed upstream DXF archive before reading payload' {
    $changed = Join-Path $run 'changed-ixmilia.dxf.0.8.4.nupkg'
    Copy-Item -LiteralPath $script:DxfArchive -Destination $changed
    [IO.File]::AppendAllText($changed, 'Changed archive fixture.')
    Refused { Assert-IxMiliaDxfPayload -ArchivePath $changed -LicensePath (Join-Path $root 'installer\notices\IXMILIA-DXF-LICENSE.txt') -AssemblyPaths @($script:DxfAssembly) } 'package checksum mismatch'
}
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
Write-Output "Passed $($checks.Count) package validator checks in $run; no installation or native execution."
