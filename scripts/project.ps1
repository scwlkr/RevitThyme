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
            Write-Output 'project.cmd doctor | build | check | package | install | uninstall'
            Write-Output 'project.cmd cli <arguments>'
            Write-Output 'project.cmd cli --version | cli version --json | cli update check --json | cli update download --destination ABSOLUTE_FOLDER | cli update install'
            Write-Output 'project.cmd cli-install [-ToolPath ABSOLUTE_DIRECTORY] installs the global rthyme command from this checkout'
            Write-Output 'project.cmd setup -PackageDirectory ABSOLUTE_PACKAGE'
            Write-Output 'project.cmd mcp <server-options>'
            Write-Output 'project.cmd dev-signing -Action Setup|Status; package -DevelopmentSigning [-DevelopmentSigningConfigPath ABSOLUTE_JSON]'
            Write-Output 'project.cmd dev-prepare -FixtureSource ABSOLUTE_RVT [-PackageDirectory SIGNED_PACKAGE] | dev-start [-RunFolder ABSOLUTE_RUN]'
            Write-Output 'project.cmd dev-status|dev-stop|dev-mcp -RunFolder ABSOLUTE_RUN'
            Write-Output 'project.cmd dev-query|dev-modify -RunFolder ABSOLUTE_RUN -Source ABSOLUTE_CSX | dev-outcome -RunFolder ABSOLUTE_RUN [-RequestId GUID]'
            Write-Output 'Build accepts MSBuild arguments such as -p:RevitApiPath=ABSOLUTE_PATH.'
        }
        'doctor' {
            dotnet --version
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            $sdk = 'C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll'
            [pscustomobject]@{ root=$root; sdk_installed=(Test-Path -LiteralPath $sdk); native_proof='Separate disposable Revit session required'; stage='Built-in Agent' } | ConvertTo-Json
        }
        'build' { dotnet build RevitThyme.sln -c Release --nologo @Rest; exit $LASTEXITCODE }
        'cli' { dotnet run --project src/RevitThyme.Cli -c Release -- @Rest; exit $LASTEXITCODE }
        'cli-install' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-cli.ps1') @Rest; exit $LASTEXITCODE }
        'mcp' {
            # stdout must contain only JSON-RPC, never dotnet build messages.
            dotnet build src/RevitThyme.Mcp -c Release --nologo | ForEach-Object { [Console]::Error.WriteLine($_) }
            if ($LASTEXITCODE) { exit $LASTEXITCODE }
            dotnet src/RevitThyme.Mcp/bin/Release/net10.0/revitthyme-mcp.dll @Rest; exit $LASTEXITCODE
        }
        'dev-signing' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'dev-signing.ps1') @Rest; exit $LASTEXITCODE }
        { $_ -in 'dev-prepare','dev-start','dev-status','dev-query','dev-modify','dev-outcome','dev-mcp','dev-stop' } {
            pwsh -NoProfile -File (Join-Path $PSScriptRoot 'dev-session.ps1') -Action $Command.Substring(4) @Rest; exit $LASTEXITCODE
        }
        'check' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-foundation.ps1') @Rest; exit $LASTEXITCODE }
        'setup' { pwsh -NoProfile -File (Join-Path $root 'installer\build-setup.ps1') @Rest; exit $LASTEXITCODE }
        'package' { pwsh -NoProfile -File (Join-Path $root 'installer\package.ps1') @Rest; exit $LASTEXITCODE }
        'install' { pwsh -NoProfile -File (Join-Path $root 'installer\install.ps1') @Rest; exit $LASTEXITCODE }
        'uninstall' { pwsh -NoProfile -File (Join-Path $root 'installer\uninstall.ps1') @Rest; exit $LASTEXITCODE }
        default { throw "Unknown command: $Command. Run project.cmd help." }
    }
} finally { Pop-Location }
