<#
.SYNOPSIS
  US-27 / NFR-12 recovery drill: kill the server and measure downtime until /health/ready is healthy again.

.DESCRIPTION
  Mode "docker" kills the nvc-server container's process (restart policy brings it back).
  Mode "process" kills a local NhatVuong.Server process; the supervisor under test (systemd, a service wrapper,
  or -Restart below for a local rehearsal) must bring it back.
  Pass criterion (docs/03-release-plan.md, release gate): downtime <= 15 minutes.

.EXAMPLE
  ./deploy/scripts/recovery-drill.ps1 -Mode docker -BaseUrl http://localhost:8080
.EXAMPLE
  ./deploy/scripts/recovery-drill.ps1 -Mode process -BaseUrl http://localhost:5180 -Restart
#>
param(
    [ValidateSet("docker", "process")] [string] $Mode = "docker",
    [string] $BaseUrl = "http://localhost:8080",
    [int] $BudgetMinutes = 15,
    [switch] $Restart
)

function Test-Ready {
    try { (Invoke-WebRequest -Uri "$BaseUrl/health/ready" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $false }
}

if (-not (Test-Ready)) { throw "Server at $BaseUrl is not ready before the drill; start it first." }

$killedAt = Get-Date
if ($Mode -eq "docker") {
    docker kill --signal=KILL nvc-server | Out-Null
} else {
    Get-Process -Name "NhatVuong.Server" -ErrorAction Stop | Stop-Process -Force
    if ($Restart) {
        $root = Resolve-Path "$PSScriptRoot/../.."
        Start-Process -FilePath "dotnet" -ArgumentList "run --no-build --project `"$root/src/Server`" --urls $BaseUrl" -WindowStyle Hidden
    }
}
Write-Host "Killed server at $($killedAt.ToString('HH:mm:ss')); waiting for readiness..."

$deadline = $killedAt.AddMinutes($BudgetMinutes)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if (Test-Ready) {
        $downtime = (Get-Date) - $killedAt
        Write-Host ("RECOVERED after {0:mm\:ss} (budget {1} min) - PASS" -f $downtime, $BudgetMinutes)
        exit 0
    }
}

Write-Host "NOT RECOVERED within $BudgetMinutes minutes - FAIL"
exit 1
