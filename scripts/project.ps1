$Command = if ($args.Count) { [string]$args[0] } else { 'help' }
[string[]]$Rest = @()
if ($args.Count -gt 1) { $Rest = $args[1..($args.Count-1)] }
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    switch ($Command) {
        { $_ -in 'help','--help' } {
            Write-Output 'RevitThyme C# Foundation development'
            Write-Output 'project.cmd doctor | build | test | check | package | install | uninstall'
            Write-Output 'project.cmd cli <arguments> | live-prepare | live-start <run> | live-check <run> | live-cleanup <run>'
            Write-Output 'project.cmd check-installer -PackageDirectory ABSOLUTE_PACKAGE'
            Write-Output 'Build accepts MSBuild arguments such as -p:RevitApiPath=ABSOLUTE_PATH.'
        }
        'doctor' {
            dotnet --version
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            $sdk = 'C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll'
            [pscustomobject]@{ root=$root; sdk_installed=(Test-Path -LiteralPath $sdk); native_tests='Separate disposable Revit session required'; stage='Foundation' } | ConvertTo-Json
        }
        'build' { dotnet build RevitThyme.sln -c Release --nologo @Rest; exit $LASTEXITCODE }
        'test' { dotnet run --project tests/Unit -c Release @Rest; exit $LASTEXITCODE }
        'cli' { dotnet run --project src/RevitThyme.Cli -c Release -- @Rest; exit $LASTEXITCODE }
        'check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-foundation.ps1') @Rest; exit $LASTEXITCODE }
        'check-installer' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-installer.ps1') @Rest; exit $LASTEXITCODE }
        'package' { pwsh -NoProfile -File (Join-Path $root 'installer\package.ps1') @Rest; exit $LASTEXITCODE }
        'install' { pwsh -NoProfile -File (Join-Path $root 'installer\install.ps1') @Rest; exit $LASTEXITCODE }
        'uninstall' { pwsh -NoProfile -File (Join-Path $root 'installer\uninstall.ps1') @Rest; exit $LASTEXITCODE }
        'live-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-foundation.ps1') -Action prepare @Rest; exit $LASTEXITCODE }
        'live-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-foundation.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'live-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-foundation.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'live-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-foundation.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        default { throw "Unknown command: $Command. Run project.cmd help." }
    }
} finally { Pop-Location }
