AstrBar v0.3.0 diagnostic scripts

Normal use no longer requires a terminal SSH tunnel. AstrBar now starts and
maintains its own local port forwarding through SSH.NET.

start-tunnel.ps1
  Emergency/debug fallback for comparing the built-in tunnel with OpenSSH.
  DO NOT keep it running while AstrBar is using the same local port.

test-astrbot.ps1
  Tests OpenAPI, file scope and Chat SSE.
  Example:
    .\scripts\test-astrbot.ps1 -ApiKey "abk_xxx" -WakePrefix "/chat"

build.ps1
  Restores NuGet packages and builds the Visual Studio solution with .NET 8.

Required API key scopes:
  -chat
  -file
