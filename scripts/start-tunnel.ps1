param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [string]$User = "root",
    [int]$LocalPort = 6185,
    [int]$RemotePort = 6185
)

$ErrorActionPreference = "Stop"
$forward = "${LocalPort}:127.0.0.1:${RemotePort}"

Write-Host "Starting AstrBot SSH tunnel..." -ForegroundColor Cyan
Write-Host "  Local:  http://127.0.0.1:$LocalPort"
Write-Host "  Remote: $User@$Server -> 127.0.0.1:$RemotePort"
Write-Host "Keep this window open. Press Ctrl+C to stop the tunnel." -ForegroundColor Yellow
Write-Host ""

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH client was not found. Install Windows OpenSSH Client first."
}

ssh -N `
    -o ExitOnForwardFailure=yes `
    -o ServerAliveInterval=30 `
    -o ServerAliveCountMax=3 `
    -L $forward `
    "$User@$Server"
