param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [string]$User = "root",
    [int]$SshPort = 22,
    [int]$LocalPort = 6190,
    [int]$RemotePort = 6190
)

$ErrorActionPreference = "Stop"
Write-Host "Emergency/debug tunnel only." -ForegroundColor Yellow
Write-Host "AstrBar normally manages this tunnel through SSH.NET." -ForegroundColor Yellow
Write-Host "Forwarding 127.0.0.1:$LocalPort -> server 127.0.0.1:$RemotePort"

ssh -N `
    -p $SshPort `
    -L "${LocalPort}:127.0.0.1:${RemotePort}" `
    "${User}@${Server}"
