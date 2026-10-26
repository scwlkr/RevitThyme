$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$command = if ($args.Count) { [string]$args[0] } else { 'help' }
[string[]]$rest = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }
$definition = Join-Path $root 'tools\Quality\toolchain.json'
$chain = Get-Content -LiteralPath $definition -Raw | ConvertFrom-Json
$digest = (Get-FileHash -LiteralPath $definition -Algorithm SHA256).Hash.ToLowerInvariant()
$cache = Join-Path $root ('artifacts\quality\toolchains\' + $digest.Substring(0, 12))
$python = Join-Path $cache 'python\Scripts\python.exe'
$coverage = Join-Path $cache 'coverage\dotnet-coverage.exe'
$tool = Join-Path $root 'tools\Quality\bin\Release\net10.0\revitthyme-quality.dll'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE) { throw "$Executable exited $LASTEXITCODE." }
}

function Prepare-Dependencies {
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $lease = [IO.File]::Open((Join-Path $cache 'prepare.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $receipt = Join-Path $cache 'ready.json'
        if ((Test-Path -LiteralPath $receipt) -and (Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath $coverage)) {
            $ready = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
            if ($ready.toolchainSha256 -eq $digest) { return }
        }
        if (-not (Test-Path -LiteralPath $python)) {
            Invoke-Checked 'uv' @('venv', '--python', '3.13', (Join-Path $cache 'python'))
        }
        Invoke-Checked 'uv' @('pip', 'install', '--python', $python,
            ('crapper @ git+' + $chain.crapperRepository + '@' + $chain.crapperRevision),
            ('tree-sitter==' + $chain.treeSitter), ('tree-sitter-language-pack==' + $chain.treeSitterLanguagePack))
        if (-not (Test-Path -LiteralPath $coverage)) {
            Invoke-Checked 'dotnet' @('tool', 'install', 'dotnet-coverage', '--version', $chain.dotnetCoverage,
                '--tool-path', (Join-Path $cache 'coverage'), '--allow-roll-forward')
        }
        [ordered]@{toolchainSha256=$digest;sourceRoot=$root;preparedUtc=[DateTime]::UtcNow.ToString('o')} |
            ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding utf8
    } finally { $lease.Dispose() }
}

Push-Location $root
try {
    if ($command -in 'help','--help') {
        Write-Output 'project.cmd quality prepare | test | collect | crapper | dryer | mutator | coverage-status'
        Write-Output 'collect builds the source and runs offline suites under pinned .NET coverage.'
        Write-Output 'crapper validates the coverage receipt before scoring; --no-coverage is syntax-only.'
        Write-Output 'dryer/mutator options: dotnet run --project tools/Quality -- --help'
        exit 0
    }
    if ($command -in 'prepare','collect','crapper') { Prepare-Dependencies }
    if (($command -in 'test','collect') -and (Test-Path -LiteralPath $coverage)) { $env:REVITTHYME_QUALITY_COLLECTOR = $coverage }
    if ($command -eq 'test') {
        & dotnet run --project tests/Quality -c Release -- @rest
        exit $LASTEXITCODE
    }
    Invoke-Checked 'dotnet' @('build', 'tools/Quality', '-c', 'Release', '--nologo')
    switch ($command) {
        'prepare' { Write-Output "Pinned quality tools prepared: $cache"; exit 0 }
        'collect' { & dotnet $tool collect --root $root --collector $coverage @rest; exit $LASTEXITCODE }
        'crapper' {
            if ($rest -contains '--root' -or $rest -contains '--coverage-command') {
                throw 'Use the project root and quality collect; raw collection commands cannot bypass the retained receipt.'
            }
            $env:PYTHONUTF8 = '1'
            if ($rest -contains '--no-coverage') {
                & $python -m crapper --root $root --source-root src --source-root tools/Quality @rest
                exit $LASTEXITCODE
            }
            Invoke-Checked 'dotnet' @($tool, 'coverage-status', '--root', $root)
            & $python -m crapper --root $root --source-root src --source-root tools/Quality --use-existing-coverage --threshold 9.999 @rest
            exit $LASTEXITCODE
        }
        { $_ -in 'dryer','mutator','coverage-status' } {
            & dotnet $tool $command --root $root @rest
            exit $LASTEXITCODE
        }
        default { throw 'Unknown quality command. Run project.cmd quality help.' }
    }
} finally { Pop-Location }
