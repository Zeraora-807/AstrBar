# AstrBar v0.3.1 架构

## 总体结构

```text
Windows
└─ AstrBar.exe
   ├─ SetupWindow                首次初始化
   ├─ ChatPopupWindow            聊天与附件上传
   ├─ SettingsWindow             连接、聊天、外观、系统设置
   ├─ FloatingOrbWindow          悬浮球
   ├─ ThemeService               动态主题与圆球颜色
   ├─ CredentialService          DPAPI 凭据存储
   ├─ SshTunnelService           内置 SSH 本地转发
   ├─ AstrBotClient              上传、聊天 SSE、权限测试
   ├─ AttachmentService          服务端附件下载和缓存
   ├─ NotificationService        Windows 通知
   └─ TrayIconService            托盘入口
          │
          │ 127.0.0.1:<LocalForwardPort>
          ▼
   SSH.NET ForwardedPortLocal
          │ SSH
          ▼
服务器 127.0.0.1:<AstrBotRemotePort>
└─ AstrBot OpenAPI
   ├─ POST /api/v1/file
   ├─ POST /api/v1/chat
   └─ GET  /api/v1/file
```

## 启动状态机

```text
读取 SettingsService.Current
        │
        ├─ IsInitialized = false
        │      ↓
        │   SetupWindow
        │      ↓ 成功
        │   保存设置与凭据
        │
        └─ IsInitialized = true
               ↓
       SshTunnelService.StartStoredAsync
               ↓
       创建托盘、聊天窗口和悬浮球
```

首次向导只有在 SSH 隧道和 AstrBot OpenAPI 都通过测试后才保存配置。

## 配置与秘密分离

```text
settings.json
├─ SSH 主机和端口
├─ SSH 用户名
├─ 主机指纹
├─ AstrBot 本地/远程端口
├─ username / session_id
├─ 主题
└─ 窗口状态

api-key.bin          DPAPI CurrentUser
ssh-password.bin     DPAPI CurrentUser
```

机密值不进入 `AppSettings`，避免 JSON 序列化时意外泄露。

## SshTunnelService

职责：

- 使用密码认证建立 SSH 连接
- 只在 `127.0.0.1` 监听本地转发端口
- 将流量转发到服务器侧 `127.0.0.1:<AstrBotRemotePort>`
- 保存并验证 SHA256 主机指纹
- 每 12 秒检查断线状态
- 开启 KeepAlive
- 通过 `StatusChanged` 把连接状态交给 UI

并发控制使用 `SemaphoreSlim`，确保启动、重连和停止不会同时修改同一个 SSH 客户端。

## 上传链路

```text
PendingUploadAttachment
        ↓
AstrBotClient.UploadFileAsync
        ↓ multipart/form-data
POST /api/v1/file
        ↓
UploadedAttachment
  attachment_id
  filename
  type
        ↓
AstrBotClient.StreamChatAsync
        ↓
message: [plain?, image/file/record/video...]
```

## 主题架构

`App.xaml` 定义固定的资源键，所有界面通过 `DynamicResource` 引用：

```text
WindowBackgroundBrush
PanelBrush
AccentBrush
AccentHoverBrush
AccentSoftBrush
TextPrimaryBrush
TextSecondaryBrush
BorderBrush
InputBrush
AssistantBubbleBrush
OrbBrush
OrbHoverBrush
```

`ThemeService.Apply` 只替换这些资源实例，因此已打开窗口会同步换肤。悬浮球颜色可以跟随主题，也可以独立覆盖。

## 消息模型

```text
ChatMessage
└─ ObservableCollection<MessagePart>
   ├─ TextMessagePart
   └─ AttachmentMessagePart
```

上传前使用 `PendingUploadAttachment`，上传成功后转换为 AstrBot 请求部件。接收时按 SSE 事件构建消息部件。

## 所有权与释放

- `App` 拥有并释放 `AstrBotClient` 与 `SshTunnelService`。
- `ChatPopupWindow` 拥有并释放 `AttachmentService` 与快捷键服务。

## 后续提醒扩展

`IClientEventSource` 保持不变。之后会发生什么之后再说~
