# AstrBar v1.0 开发说明

## 项目

```text
src/AstrBar.App
├─ Models
├─ Services
├─ Views
└─ Resources
```

## 关键代码

```text
Models/ProtocolEnvelope.cs
Services/AstrBarProtocolClient.cs
Services/AttachmentService.cs
Services/SshTunnelService.cs
Views/ChatPopupWindow.xaml.cs
Views/SetupWindow.xaml.cs
Views/SettingsWindow.xaml.cs
```

## 开发原则

1. 第三方插件兼容性以 AstrBot 标准平台接口为边界。
2. 不在客户端模拟 QQ、Telegram 等平台私有 API。
3. 大文件始终通过 HTTP，不放进 WebSocket JSON。
4. 任何需要重放的服务端事件都应 ACK。
5. 通知判断只观察标准消息输出、因果字段与 Presence，不扫描插件源码。
6. 密码和 Token 不写入项目目录。
7. UI 线程更新必须通过 Dispatcher。

## 新增协议事件

同时修改：

```text
ProtocolEnvelope 模型
AstrBarProtocolClient 的 HandleEnvelopeAsync
Essential 的 protocol/constants.py
Essential 的 gateway.py
双方协议文档与测试
```

对未知事件，客户端默认忽略，而不是断开连接，以便小版本向前兼容。

## 调试

### 检查服务器网关

```powershell
.\scripts\test-astrbot.ps1 -Token "你的 token"
```

### 检查 SSH 本地转发

```powershell
Get-NetTCPConnection -LocalPort 6190
```

### 清除首次运行状态

关闭 AstrBar 后备份或删除：

```text
%LOCALAPPDATA%\AstrBar\settings.json
```

### 清除 Protocol Token

删除：

```text
%LOCALAPPDATA%\AstrBar\protocol-token.bin
```

## 兼容性

v1.0 使用 net8.0-windows10.0.19041.0。不要启用 PublishTrimmed，WPF、XAML 与 Windows App SDK 的反射路径可能被错误裁剪。
