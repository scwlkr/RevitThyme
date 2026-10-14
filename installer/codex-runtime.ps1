param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'artifacts\agent\runtime'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'codex.zip'
$hash = '4b17206ed3045de64064b2016627d51fba3bde386d03838312054882a64d781d'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest 'https://github.com/openai/codex/releases/download/rust-v0.162.1/codex-x86_64-pc-windows-msvc.exe.zip' -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $hash) { throw 'Pinned Codex archive checksum mismatch; cached evidence retained.' }
$unpacked = Join-Path $cache ('verified-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
New-Item -ItemType Directory -Path (Join-Path $Destination 'codex-path') -Force | Out-Null
foreach ($name in @('codex-code-mode-host.exe','codex-command-runner.exe','codex-windows-sandbox-setup.exe','codex-path\rg.exe')) {
    Copy-Item -LiteralPath (Join-Path $unpacked $name) -Destination (Join-Path $Destination $name)
}
Copy-Item -LiteralPath (Join-Path $unpacked 'codex-x86_64-pc-windows-msvc.exe') -Destination (Join-Path $Destination 'codex.exe')
foreach ($entry in @(@('LICENSE','d17f227e4df5da1600391338865ce0f3055211760a36688f816941d58232d8dc'),@('NOTICE','9d71575ecfd9a843fc1677b0efb08053c6ba9fd686a0de1a6f5382fd3c220915'))) {
    $file = Join-Path $cache ($entry[0]+'.txt')
    if (-not (Test-Path -LiteralPath $file)) { Invoke-WebRequest ('https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/'+$entry[0]) -OutFile $file }
    if ((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ne $entry[1]) { throw 'Codex notice checksum mismatch.' }
    Copy-Item -LiteralPath $file -Destination (Join-Path $Destination ($entry[0]+'.txt'))
}
