param(
    [string]$BaseUrl = "http://127.0.0.1:6185",

    [Parameter(Mandatory = $true)]
    [string]$ApiKey,

    [string]$SessionId = "astrbar-connectivity-test",

    [string]$Username = "astrbar-local",

    [string]$WakePrefix = ""
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")

Write-Host "Testing OpenAPI endpoint..." -ForegroundColor Cyan
Invoke-WebRequest `
    -Uri "$BaseUrl/api/v1/openapi.json" `
    -Headers @{ Authorization = "Bearer $ApiKey" } `
    -UseBasicParsing | Out-Null
Write-Host "OpenAPI endpoint is reachable." -ForegroundColor Green

Write-Host "Testing file scope..." -ForegroundColor Cyan
try {
    Invoke-WebRequest `
        -Uri "$BaseUrl/api/v1/file?attachment_id=astrbar-scope-probe" `
        -Headers @{ Authorization = "Bearer $ApiKey" } `
        -UseBasicParsing | Out-Null
    Write-Host "File endpoint is reachable. The probe attachment is intentionally nonexistent." -ForegroundColor Green
}
catch {
    throw "file scope test failed: $($_.Exception.Message)"
}

$payload = @{
    username = $Username
    session_id = $SessionId
    message = @(
        @{
            type = "plain"
            text = $(if ([string]::IsNullOrWhiteSpace($WakePrefix)) {
                "Reply exactly with: AstrBar connection successful"
            } else {
                "$($WakePrefix.Trim()) Reply exactly with: AstrBar connection successful"
            })
        }
    )
    flags = @{
        enable_inline_genui = $true
        enable_default_system_prompt = $true
        enable_streaming = $true
    }
}

$body = $payload | ConvertTo-Json -Depth 10 -Compress
$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("astrbar-chat-{0}.json" -f [Guid]::NewGuid())

try {
    # PowerShell 5.1 may strip JSON quotes when a JSON string is passed directly
    # to a native executable. Writing the body to a UTF-8 file avoids that issue.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($tempFile, $body, $utf8NoBom)

    Write-Host "`nTesting Chat SSE..." -ForegroundColor Cyan
    curl.exe -N `
        "$BaseUrl/api/v1/chat" `
        -H "Authorization: Bearer $ApiKey" `
        -H "Accept: text/event-stream" `
        -H "Content-Type: application/json" `
        --data-binary "@$tempFile"
}
finally {
    Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
}
