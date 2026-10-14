function Get-PinnedBootstrap {
    param([Parameter(Mandatory)][string]$Version)
    if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$') {
        throw 'A release version such as 0.8.1 is required.'
    }
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'bootstrap.ps1'))
    $parameter = 'param([string]$Version,'
    if ([regex]::Matches($source,[regex]::Escape($parameter)).Count -ne 1) { throw 'Bootstrap version parameter changed; preserve its version pin.' }
    $pinned = $source.Replace($parameter, ('param([string]$Version = ''{0}'',' -f $Version))
    # Invoke-Expression uses its caller's scope; keep installer variables in a child scope.
    return "#Requires -Version 5.1`n& {`n" + $pinned + "`n} @args`n"
}
