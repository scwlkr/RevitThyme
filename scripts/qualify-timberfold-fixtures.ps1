[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FixtureSet,
    [Parameter(Mandatory)][string[]]$Fixtures
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathFullyQualified($FixtureSet)) { throw 'Supply the absolute independently authored fixture-set receipt.' }
$set = Get-Content -LiteralPath $FixtureSet -Raw | ConvertFrom-Json
$devSession = Join-Path $PSScriptRoot 'dev-session.ps1'
$qualification = Join-Path $PSScriptRoot 'qualify-timberfold-inspection.ps1'
$results = [Collections.Generic.List[object]]::new()
foreach ($fixtureName in $Fixtures) {
    $fixture = @($set.qualifiedOriginalFixtures | Where-Object fixture -eq $fixtureName | Select-Object -First 1)
    if ($fixture.Count -ne 1) { throw "No independently qualified original fixture: $fixtureName" }
    $receipt = Get-Content -LiteralPath $fixture[0].receipt -Raw | ConvertFrom-Json
    if (-not $receipt.nativeFixtureChecksPassed -or (Get-FileHash -LiteralPath $receipt.modelPath).Hash -ne $receipt.rvtSha256) {
        throw 'Original fixture is missing, changed or not independently qualified.'
    }
    $plan = & $devSession -Action prepare -FixtureSource $receipt.modelPath | Out-String | ConvertFrom-Json
    $startLog = Join-Path $plan.folder 'timberfold-start.log'
    & $devSession -Action start -RunFolder $plan.folder *> $startLog
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $status = $null
    while ($timer.Elapsed.TotalSeconds -lt 180) {
        try {
            $status = & $devSession -Action status -RunFolder $plan.folder | Out-String | ConvertFrom-Json
            if ($status.state -eq 'ready') { break }
            if ($status.state -eq 'exited') { throw 'Owned Revit process exited before qualification.' }
        }
        catch { $_ | Out-String | Set-Content -LiteralPath (Join-Path $plan.folder 'timberfold-startup-status.txt') }
        Start-Sleep -Seconds 2
    }
    if ($status.state -ne 'ready') { throw "Native host did not become ready; inspect the retained run: $($plan.folder)" }
    $checkLog = Join-Path $plan.folder 'timberfold-qualification.log'
    $checkError = $null
    try { & $qualification -RunFolder $plan.folder -FixtureReceipt $fixture[0].receipt *> $checkLog }
    catch { $checkError = $_; $_ | Out-String | Add-Content -LiteralPath $checkLog }
    # Close only this exact owned run after proving that no model edit or pending
    # request needs recovery. Never terminate a process or dismiss a save prompt.
    $status = & $devSession -Action status -RunFolder $plan.folder | Out-String | ConvertFrom-Json
    if ($status.pending_request -or $status.context.snapshot.isModified) {
        throw "Retain owned host for recovery; pending/modified state in $($plan.folder)"
    }
    & $devSession -Action stop -RunFolder $plan.folder *> (Join-Path $plan.folder 'timberfold-stop.json')
    $ownedLaunch = Get-Content -LiteralPath (Join-Path $plan.folder 'launch.json') -Raw | ConvertFrom-Json
    $timer.Restart()
    do {
        Start-Sleep -Seconds 1
        # Do not queue an API request while the host is shutting down.
        $ownedProcess = Get-Process -Id $ownedLaunch.pid -ErrorAction SilentlyContinue
        if ($ownedProcess -and ($ownedProcess.StartTime.ToUniversalTime().Ticks.ToString() -ne $ownedLaunch.start -or
            $ownedProcess.Path -ne $ownedLaunch.revit)) { throw 'Owned PID was reused during close; no process was addressed.' }
    } while ($ownedProcess -and $timer.Elapsed.TotalSeconds -lt 60)
    if ($ownedProcess) { throw "Owned host awaits normal close; inspect $($plan.folder)" }
    $closedHash = (Get-FileHash -LiteralPath $plan.fixture).Hash
    $preserved = $closedHash -eq $receipt.rvtSha256 -and (Get-FileHash -LiteralPath $receipt.modelPath).Hash -eq $receipt.rvtSha256
    @{ sourceAndCopyUnchangedAfterNormalClose = $preserved; closedCopySha256 = $closedHash; fixtureReceipt = $fixture[0].receipt } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.folder 'timberfold-closed-preservation.json') -Encoding utf8
    if (-not $preserved) { throw 'Source/copy preservation failed after normal native close.' }
    if ($checkError) { throw $checkError }
    $results.Add(@{ fixture = $fixtureName; run = $plan.folder; passed = $true; closedPreservation = $true })
    Write-Output "PASS native inspection and closed preservation: $fixtureName; $($plan.folder)"
}
$results.ToArray() | ConvertTo-Json -Depth 5
