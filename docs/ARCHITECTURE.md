# AstrBar v1.0 架构

## 总览

```text
AstrBar WPF Client
├─ UI / Tray / Orb / Theme
├─ Windows Notification
├─ SSH.NET Tunnel
└─ AstrBarProtocolClient
   ├─ WebSocket message channel
   ├─ HTTP attachment channel
   ├─ ACK and deduplication
   ├─ reconnect and heartbeat
   └─ presence and causal notification metadata
        │
        ▼
astrbot_plugin_astrbar_essential
├─ AstrBarGateway
├─ AstrBarPlatformAdapter
├─ InboundConverter
├─ OutboundConverter
├─ delivery storage
└─ attachment storage
        │
        ▼
AstrBot standard pipeline
├─ command handlers
├─ third-party plugins
├─ LLM / Agent
└─ proactive send_by_session
```

## 透明边界

AstrBar Protocol 只存在于 Windows 客户端和 `astrbar` 平台适配器之间。

进入 AstrBot 后，客户端消息已经被转换为标准 `AstrBotMessage`。第三方插件读取标准 `AstrMessageEvent`，并返回标准 `MessageChain`。平台事件再把消息链转换为 AstrBar Protocol，因此协议对插件层透明。

## 客户端服务

### AstrBarProtocolClient

负责：

- HTTP 状态探测
- WebSocket 握手与持久连接
- 请求关联和流式消息分发
- 主动消息事件
- ACK、去重、心跳和重连
- Presence 上报
- 附件上传

### AttachmentService

负责带 Bearer Token 下载 Essential 发布的附件，并保存到 LocalAppData 缓存目录。

### SshTunnelService

通过 SSH.NET 创建：

```text
127.0.0.1:<local port>
→ SSH server
→ 127.0.0.1:<remote protocol port>
```

### SettingsService 与 CredentialService

- 普通设置保存为 JSON
- Protocol Token 与 SSH 密码使用 DPAPI
- device_id 在第一次运行时生成，之后保持稳定
- v0.3.x 配置被标记为需要重新初始化

## 请求关联

客户端发送 `message.send` 时生成 request ID，并创建本地 `PendingRequest`。

```text
message.send id=msg_A
        ↓
message.accepted correlation_id=msg_A
message.start    correlation_id=msg_A
message.delta    correlation_id=msg_A
message.complete correlation_id=msg_A
```

没有对应本地请求的 `message.complete` 被视为主动或离线重放消息，进入 `ProactiveMessageReceived`。

## 通知分层

服务端负责标记消息因果关系：

```text
origin=response
origin=proactive
```

客户端负责结合用户状态做最终判断：

```text
消息因果关系
+ 窗口可见性
+ 窗口焦点
+ 勿扰设置
+ 任务耗时
= 是否弹 Windows 通知
```

这种设计不要求第三方插件了解 AstrBar。
