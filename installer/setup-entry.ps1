param(
    [ValidateSet('Check','Install','CheckUninstall','Uninstall')][string]$Action = 'Check',
    [Parameter(Mandatory)][string]$LogPath,
    [string]$ReceiptPath,
    [string]$TestRoot
)
$ErrorActionPreference = 'Stop'
try {
    # Inno can inherit a PowerShell 7 module path; load the Windows PowerShell utility module explicitly.
    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
    . (Join-Path $PSScriptRoot 'common.ps1')
    $options = @{}
    if ($TestRoot) {
        $options = @{InstallDirectory=(Join-Path $TestRoot 'installed');AddinsDirectory=(Join-Path $TestRoot 'addins');Isolated=$true}
        $null = Assert-IsolatedInstallTest $options.InstallDirectory $options.AddinsDirectory
    }
    if ($Action -in @('Uninstall','CheckUninstall') -and $ReceiptPath) {
        $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
        $registration = Get-FoundationRegistration $options.AddinsDirectory
        if ((Test-Path -LiteralPath $registration) -and (Read-FoundationRegistration $registration) -ne $receipt.installed) {
            throw 'A different bundle is now registered. Preserve its registration and uninstall assets.'
        }
    }
    if ($Action -in @('Check','Install')) {
        $json = & (Join-Path $PSScriptRoot 'install.ps1') -PackageDirectory $PSScriptRoot @options -CheckOnly:($Action -eq 'Check') -ReceiptPath $ReceiptPath
    } else {
        $json = & (Join-Path $PSScriptRoot 'uninstall.ps1') @options -CheckOnly:($Action -eq 'CheckUninstall')
    }
    [IO.File]::WriteAllText($LogPath, ($json -join [Environment]::NewLine))
    exit 0
} catch {
    [IO.File]::WriteAllText($LogPath, $_.Exception.Message)
    Write-Error $_ -ErrorAction Continue
    exit 1
}
