param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
if (-not [IO.Path]::IsPathFullyQualified($Executable)) { throw 'Use an absolute rthyme executable path.' }
$outside = Join-Path ([IO.Path]::GetTempPath()) ('RevitThyme-Cli-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outside | Out-Null
$checks = 0
function Invoke-Tool([string[]]$Arguments) {
    $start=[Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8; $start.StandardErrorEncoding=[Text.Encoding]::UTF8
    $start.WorkingDirectory=$outside
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { throw 'CLI check timed out.' }
        return @{exit=$process.ExitCode;output=$stdout.GetAwaiter().GetResult();error=$stderr.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}
try {
    $version = Invoke-Tool @('version','--json'); $data=$version.output | ConvertFrom-Json
    if ($version.exit -ne 0 -or -not $data.cli.version -or -not $data.installed.status -or -not $data.PSObject.Properties['development']) { throw 'Global structured version failed.' }; $checks++
    $text = Invoke-Tool @('--version')
    if ($text.exit -ne 0 -or $text.output -notmatch 'CLI:' -or $text.output -notmatch 'Installed RevitThyme') { throw 'Global text version failed.' }; $checks++
    foreach ($argument in @('', '--space and value', '--quotes="yes" & | < > ^ %', '--unicode=é壁', '--slash=C:\path\')) {
        $result=Invoke-Tool @('update','check',$argument)
        if ($result.exit -ne 2 -or $result.error.TrimEnd() -cne ('Unknown update option: '+$argument).TrimEnd()) { throw "CLI argument/exit round-trip failed: $argument" }; $checks++
    }
    Write-Output "Passed $checks native global CLI checks from outside the checkout."
} finally {
    # The fresh empty directory is the only filesystem target owned by this check.
    [IO.Directory]::Delete($outside)
}
