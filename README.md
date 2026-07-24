# AstrBar v0.3.1

AstrBar 是一个轻量的 AstrBot 桌面客户端。它驻留在任务栏通知区域，也可以缩成悬浮圆球，并通过 SSH 隧道安全访问远程服务器上的 AstrBot。

## v0.3.0 新增

- 首次启动向导
  - 服务器公网 IP 或域名
  - SSH 端口、用户名与密码
  - AstrBot 服务器端口与本地转发端口
  - AstrBot API Key、username、session_id
  - 界面主题和悬浮球颜色
  - 一键建立隧道并测试 `chat`、`file` 权限
- 内置 SSH 隧道
  - 程序启动时自动连接
  - KeepAlive 与断线自动重连
  - 首次连接记录 SSH 主机 SHA256 指纹
  - 指纹变化时拒绝连接，防止误连到其他服务器
- 上传附件
  - 点击输入框左侧的 `＋` 选择文件
  - 支持拖拽文件到输入区域
  - 支持图片、音频及其他 AstrBot 可接收文件
  - 每条消息最多 8 个附件，单个文件上限 250 MB
  - 图片在发送前显示缩略图
- 多套主题
- 独立悬浮球颜色

## 技术栈

```text
.NET 8
C# 12
WPF + XAML
HttpClient + HTTP SSE
SSH.NET 2025.1.0
System.Text.Json
Windows DPAPI
Microsoft Windows App SDK 通知
System.Windows.Forms.NotifyIcon
```

## 环境要求

### Windows 客户端

- Windows 10 2004 或更高版本，推荐 Windows 11
- Visual Studio 2022
- “.NET 桌面开发”工作负载
- .NET 8 SDK

### 服务器

- SSH 可通过公网访问
- 用户名与密码可以正常登录 SSH
- AstrBot 正在服务器上运行
- AstrBot OpenAPI 在服务器本机可访问，例如：

```text
http://127.0.0.1:6185 （Astrbot默认端口）
```

- API Key 至少具有：

```text
chat
file
```

## 第一次运行

1. 填写以下信息
```text
服务器公网 IP 或域名（只填 IP 或域名，不用加 http://）
SSH 端口：通常为 22
SSH 用户名：例如 root
SSH 密码：服务器登录密码
AstrBot 服务器端口：默认 6185
本地转发端口：默认 6185
API Key：需要 chat 与 file scope
```

2. 点击“测试并进入 AstrBar”。
3. SSH 和 AstrBot 测试都成功后，设置会保存，聊天窗口启动。

## 上传附件

### 选择文件

点击消息输入框左侧的 `＋`，可多选文件。也可以直接把文件拖进输入区域。

### 发送链路（技术细节）

```text
本地文件
  ↓ multipart/form-data
POST /api/v1/file
  ↓
AstrBot 返回 attachment_id 和附件类型
  ↓
POST /api/v1/chat
message 中携带 attachment_id
```

图片、音频、视频会按 MIME 类型分别作为 `image`、`record`、`video` 发送，其他格式作为 `file` 发送。是否能被模型直接理解，还取决于 AstrBot、模型和插件的能力。

## 主题与悬浮球

右键托盘图标，打开“设置 → 外观”

- 修改选项时会即时预览，点击“取消”会恢复原主题。

## 配置与凭据位置

所有持久数据都位于用户的 LocalAppData，而非程序目录：

```text
%LOCALAPPDATA%\AstrBar\settings.json
%LOCALAPPDATA%\AstrBar\api-key.bin
%LOCALAPPDATA%\AstrBar\ssh-password.bin
%LOCALAPPDATA%\AstrBar\Cache\Attachments\
```

- `settings.json` 保存非机密设置，例如服务器地址、端口、用户名、主题和主机指纹。
- API Key 与 SSH 密码分别通过 Windows DPAPI 的 `CurrentUser` 范围加密。
- 注意：加密文件只适合由当前 Windows 用户在本机读取，不应复制到其他电脑作为凭据备份。

## SSH 主机指纹

第一次成功连接时，AstrBar 会记录 SSH 主机 SHA256 指纹。以后连接必须与已记录指纹一致。

服务器重装或 SSH 主机密钥确实发生改变时：

1. 先在服务器控制台确认变更是预期的。
2. 打开 AstrBar 设置。
3. 点击“重新信任主机”。
4. 再点击“测试并重连”。

不要在没有确认服务器状态时盲目清除指纹。

## 发送模式

- **自动**：自动识别消息类型。
- **对话**：强制按 LLM 对话发送。
- **命令**：完全原样发送，适合插件命令。

```
