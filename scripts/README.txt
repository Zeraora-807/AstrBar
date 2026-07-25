AstrBar v1.0 scripts

build.ps1
  Restores NuGet packages and builds the Debug configuration.

publish.ps1
  Creates a self-contained Windows x64 release folder and ZIP.
  Example:
    .\scripts\publish.ps1 -Version 1.0.0

start-tunnel.ps1
  Emergency/debug OpenSSH tunnel. Normal use should rely on AstrBar's embedded
  SSH.NET tunnel. Do not run both on the same local port.

test-astrbot.ps1
  Tests the AstrBar Essential state endpoint and WebSocket client.hello /
  server.welcome handshake.
  Example:
    .\scripts\test-astrbot.ps1 -Token "your-protocol-token"

Default AstrBar Protocol port: 6190
