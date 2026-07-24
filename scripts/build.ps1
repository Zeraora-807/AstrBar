$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet restore .\AstrBar.sln
    dotnet build .\AstrBar.sln -c Debug
    Write-Host "`nBuild completed." -ForegroundColor Green
    Write-Host "Run: .\src\AstrBar.App\bin\Debug\net8.0-windows10.0.19041.0\AstrBar.exe"
}
finally {
    Pop-Location
}
