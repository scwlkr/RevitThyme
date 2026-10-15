$Command = if ($args.Count) { [string]$args[0] } else { 'help' }
[string[]]$Rest = @()
if ($args.Count -gt 1) { $Rest = $args[1..($args.Count-1)] }
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    switch ($Command) {
        { $_ -in 'help','--help' } {
            Write-Output 'RevitThyme C# toolkit through Built-in Agent development'
            Write-Output 'project.cmd doctor | build | test | check | package | install | uninstall'
            Write-Output 'project.cmd cli <arguments> | live-prepare | live-start <run> | live-check <run> | live-cleanup <run>'
            Write-Output 'project.cmd cli --version | cli version --json | cli update check --json | cli update download --destination ABSOLUTE_FOLDER | cli update install'
            Write-Output 'project.cmd cli-install [-ToolPath ABSOLUTE_TEST_DIRECTORY] installs the global rthyme command from this checkout'
            Write-Output 'project.cmd check-cli-tool -Executable ABSOLUTE_RTHYME_EXE checks the native global CLI'
            Write-Output 'project.cmd check-installer -PackageDirectory ABSOLUTE_PACKAGE'
            Write-Output 'project.cmd setup -PackageDirectory ABSOLUTE_PACKAGE | check-bootstrap | check-setup -PackageDirectory ABSOLUTE_PACKAGE -SetupExecutable ISOLATED_EXE'
            Write-Output 'project.cmd mcp <server-options> | test-mcp | mcp-prepare | mcp-start <run> | mcp-check <run> | mcp-codex <run> | mcp-cleanup <run>'
            Write-Output 'project.cmd utilities-prepare | utilities-start <run> | utilities-check <run> | utilities-cleanup <run>'
            Write-Output 'project.cmd dynamic-prepare | dynamic-start <run> | dynamic-check <run> | dynamic-cleanup <run>'
            Write-Output 'project.cmd agent-prepare | agent-start <run> | agent-check <run> | agent-cleanup <run>'
            Write-Output 'project.cmd ribbon-prepare | ribbon-start <run> | ribbon-check <run> | ribbon-cleanup <run>'
            Write-Output 'Build accepts MSBuild arguments such as -p:RevitApiPath=ABSOLUTE_PATH.'
        }
        'doctor' {
            dotnet --version
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            $sdk = 'C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll'
            [pscustomobject]@{ root=$root; sdk_installed=(Test-Path -LiteralPath $sdk); native_tests='Separate disposable Revit session required'; stage='Built-in Agent' } | ConvertTo-Json
        }
        'build' { dotnet build RevitThyme.sln -c Release --nologo @Rest; exit $LASTEXITCODE }
        'test' { dotnet run --project tests/Unit -c Release @Rest; exit $LASTEXITCODE }
        'cli' { dotnet run --project src/RevitThyme.Cli -c Release -- @Rest; exit $LASTEXITCODE }
        'cli-install' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-cli.ps1') @Rest; exit $LASTEXITCODE }
        'check-cli-tool' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-cli-tool.ps1') @Rest; exit $LASTEXITCODE }
        'mcp' {
            # stdout must contain only JSON-RPC, never dotnet build messages.
            dotnet build src/RevitThyme.Mcp -c Release --nologo | ForEach-Object { [Console]::Error.WriteLine($_) }
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            dotnet src/RevitThyme.Mcp/bin/Release/net10.0/revitthyme-mcp.dll @Rest; exit $LASTEXITCODE
        }
        'test-mcp' { dotnet run --project tests/Mcp -c Release @Rest; exit $LASTEXITCODE }
        'mcp-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-mcp.ps1') -Action prepare @Rest; exit $LASTEXITCODE }
        'mcp-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-mcp.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'mcp-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-mcp.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'mcp-codex' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'codex-mcp.ps1') @Rest; exit $LASTEXITCODE }
        'mcp-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-mcp.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        'utilities-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-utilities.ps1') -Action prepare @Rest; exit $LASTEXITCODE }
        'utilities-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-utilities.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'utilities-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-utilities.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'utilities-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-utilities.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        'dynamic-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-dynamic.ps1') -Action prepare @Rest; exit $LASTEXITCODE }
        'dynamic-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-dynamic.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'dynamic-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-dynamic.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'dynamic-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-dynamic.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        'agent-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action prepare @Rest; exit $LASTEXITCODE }
        'ribbon-prepare' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action prepare -RibbonOnly @Rest; exit $LASTEXITCODE }
        'ribbon-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'ribbon-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'ribbon-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        'agent-start' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action start @Rest; exit $LASTEXITCODE }
        'agent-check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action check @Rest; exit $LASTEXITCODE }
        'agent-cleanup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'live-agent.ps1') -Action cleanup @Rest; exit $LASTEXITCODE }
        'check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-foundation.ps1') @Rest; exit $LASTEXITCODE }
        'check-installer' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-installer.ps1') @Rest; exit $LASTEXITCODE }
        'check-bootstrap' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-bootstrap.ps1') @Rest; exit $LASTEXITCODE }
        'check-setup' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-setup.ps1') @Rest; exit $LASTEXITCODE }
        'setup' { pwsh -NoProfile -File (Join-Path $root 'installer\build-setup.ps1') @Rest; exit $LASTEXITCODE }
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
