param(
    [string]$BaseUrl = "http://127.0.0.1:6190",

    [Parameter(Mandatory = $true)]
    [string]$Token,

    [string]$UserId = "astrbar-test",
    [string]$DeviceId = "astrbar-powershell-probe"
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }
$root = $BaseUrl.TrimEnd('/')

Write-Host "[1/2] Testing management state endpoint..." -ForegroundColor Cyan
$state = Invoke-RestMethod `
    -Method Get `
    -Uri "$root/astrbar/v1/state" `
    -Headers $headers
Write-Host "State endpoint OK." -ForegroundColor Green

Write-Host "[2/2] Testing AstrBar Protocol WebSocket handshake..." -ForegroundColor Cyan
$uri = [Uri]$root
$scheme = if ($uri.Scheme -eq "https") { "wss" } else { "ws" }
$wsUri = [Uri]::new("${scheme}://$($uri.Authority)/astrbar/v1/ws")

$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$socket.Options.SetRequestHeader("Authorization", "Bearer $Token")
$cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))

try {
    $socket.ConnectAsync($wsUri, $cts.Token).GetAwaiter().GetResult()

    $hello = @{
        protocol = "astrbar/1.0"
        id = "hello_$([Guid]::NewGuid().ToString('N'))"
        type = "client.hello"
        timestamp = [DateTimeOffset]::UtcNow.ToString("O")
        user_id = $UserId
        device_id = "$DeviceId-$([Guid]::NewGuid().ToString('N'))"
        requires_ack = $false
        sequence = 1
        payload = @{
            device_id = "$DeviceId-$([Guid]::NewGuid().ToString('N'))"
            device_name = "PowerShell Protocol Probe"
            user_id = $UserId
            client_version = "1.0.0-script"
            sessions = @()
            capabilities = @("delivery.ack")
            presence = @{
                window_visible = $false
                window_focused = $false
                do_not_disturb = $true
            }
        }
    }
    # Envelope and payload device IDs must match.
    $hello.payload.device_id = $hello.device_id
    $bytes = [Text.Encoding]::UTF8.GetBytes(($hello | ConvertTo-Json -Depth 8 -Compress))
    $segment = [ArraySegment[byte]]::new($bytes)
    $socket.SendAsync(
        $segment,
        [System.Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        $cts.Token
    ).GetAwaiter().GetResult()

    $buffer = New-Object byte[] 65536
    $stream = [IO.MemoryStream]::new()
    do {
        $result = $socket.ReceiveAsync(
            [ArraySegment[byte]]::new($buffer),
            $cts.Token
        ).GetAwaiter().GetResult()
        if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw "Gateway closed before server.welcome."
        }
        $stream.Write($buffer, 0, $result.Count)
    } while (-not $result.EndOfMessage)

    $json = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
    if ($json.type -ne "server.welcome") {
        throw "Expected server.welcome, received $($json.type)."
    }
    Write-Host "Handshake OK. Server version: $($json.payload.server_version)" -ForegroundColor Green
}
finally {
    if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $socket.CloseAsync(
            [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
            "probe complete",
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()
    }
    $socket.Dispose()
    $cts.Dispose()
}

Write-Host "AstrBar Essential state and protocol handshake are available." -ForegroundColor Green
