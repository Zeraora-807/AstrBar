param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "release\AstrBar-v$Version-$Runtime"
$zip = "$output.zip"

Push-Location $root
try {
    Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue

    dotnet restore .\AstrBar.sln
    dotnet publish .\src\AstrBar.App\AstrBar.App.csproj `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $output

    Compress-Archive -Path "$output\*" -DestinationPath $zip -CompressionLevel Optimal
    $hash = Get-FileHash $zip -Algorithm SHA256
    $hash | Format-List
    Write-Host "Published: $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}
