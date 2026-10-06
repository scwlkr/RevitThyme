param(
    [string]$PyRevitRoot = "$env:APPDATA\pyRevit-Master",
    [string]$ReportRoot = "$PSScriptRoot\..\artifacts\view-range-ui"
)
$ErrorActionPreference = 'Stop'
# Run with Windows PowerShell -STA. This uses installed assemblies without Revit.
$engineRoot = Join-Path $PyRevitRoot 'bin\netfx\engines\IPY2712PR'
foreach ($name in @('pyRevitLabs.Microsoft.Scripting.dll', 'pyRevitLabs.Microsoft.Dynamic.dll',
                    'pyRevitLabs.IronPython.dll', 'pyRevitLabs.IronPython.Modules.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $engineRoot $name)) | Out-Null
}
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$engine = [IronPython.Hosting.Python]::CreateEngine()
foreach ($name in @('pyRevitLabs.IronPython.Modules.dll')) {
    $engine.Runtime.LoadAssembly([Reflection.Assembly]::LoadFrom((Join-Path $engineRoot $name)))
}
$engine.Runtime.LoadAssembly([System.Windows.Window].Assembly)
$engine.Runtime.LoadAssembly([System.Windows.Media.Brush].Assembly)
$engine.Runtime.LoadAssembly([System.Windows.Thickness].Assembly)
$engine.Runtime.LoadAssembly([System.Windows.Size].Assembly)
$scope = $engine.CreateScope()
$scope.SetVariable('library_path', (Join-Path $PSScriptRoot '..\extensions\RevitThyme.extension\lib'))
$scope.SetVariable('fixture_output', (Join-Path $ReportRoot 'view-range-window.png'))
$null = New-Item -ItemType Directory -Path $ReportRoot -Force
try {
    $engine.ExecuteFile((Join-Path $PSScriptRoot 'fixtures\view-range-ui-fixture.py'), $scope) | Out-Null
    $checks = @($scope.GetVariable('checks'))
    $result = @{scope='native_wpf_synthetic_fixture'; passed=$true; checks=$checks;
                live_revit_checked=$false; screenshot=(Join-Path $ReportRoot 'view-range-window.png')}
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReportRoot 'report.json') -Encoding UTF8
    Write-Output ($result | ConvertTo-Json -Depth 8)
} catch {
    $failure = @{scope='native_wpf_synthetic_fixture'; passed=$false; error=$_.Exception.ToString();
                 live_revit_checked=$false}
    $failure | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportRoot 'report.json') -Encoding UTF8
    throw
}
