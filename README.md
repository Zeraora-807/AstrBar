# AstrBar v1.0.0

AstrBar 是一个面向 AstrBot 的轻量 Windows 桌面客户端。

注意：v1.0 之后，不再借用 AstrBot 默认 WebChat/OpenAPI 作为聊天入口，而是与 `astrbot_plugin_astrbar_essential` 提供的原生 `astrbar` 平台适配器通信。第三方插件仍然面对 AstrBot 标准事件和消息链，因此**不需要**为了 AstrBar 修改代码。

## 组成

AstrBar v1.0 需要同时部署：

```text
Windows
└─ AstrBar v1.0 客户端

AstrBot 服务器
└─ astrbot_plugin_astrbar_essential v1.0
   └─ AstrBar 平台适配器与 Protocol 网关
```

## 环境

- Windows 10 2004 或更高版本，推荐 Windows 11
- Visual Studio 2022，安装“.NET 桌面开发”工作负载
- .NET 8 SDK
- 可正常运行的 AstrBot
- `astrbot_plugin_astrbar_essential` v1.0.0

## 服务器部署

### 1. 安装 Essential 插件

将插件目录放入 AstrBot：

```text
AstrBot/data/plugins/astrbot_plugin_astrbar_essential
```

在 AstrBot WebUI 中重载插件，然后进入消息平台配置，新增 `AstrBar` 平台。

建议配置：

```text
id: astrbar-main
host: 0.0.0.0
port: 6190
token: 自定义文本
heartbeat_interval: 20
max_attachment_mb: 128
attachment_ttl_hours: 24
delivery_ttl_hours: 72
```

### 2. Docker 映射端口

若 AstrBot 在 Docker 中运行，需要把网关映射到服务器回环地址：

```yaml
ports:
  - "127.0.0.1:6190:6190"
```

AstrBar 默认通过 SSH 隧道访问。

## Windows 客户端初始化

首次运行填写：

```text
服务器公网 IP 或域名
SSH 端口，默认 22
SSH 用户名与密码
AstrBar Protocol 服务器端口，默认 6190
本地转发端口，默认 6190
AstrBar Protocol Token
user_id
session_id
etc.
```

连接链路：

```text
AstrBar
→ 内置 SSH 隧道
→ 服务器 127.0.0.1:6190
→ AstrBar Essential
→ AstrBot 标准事件管道
→ etc.
```

## 通知逻辑

AstrBar 不要求第三方插件发送专用通知信号。

- 由当前客户端请求产生的消息标记为 `response`
- 经 AstrBot 标准主动发送路径产生的消息标记为 `proactive`
- 客户端根据窗口是否可见、是否聚焦、是否勿扰以及任务耗时决定是否显示 Windows 通知

默认规则：

```text
主动消息 + 窗口未聚焦       → 通知
普通回复 + 窗口未聚焦       → 通知
长任务完成 + 窗口未聚焦     → “后台任务已完成”通知
窗口正在聚焦               → 只更新聊天界面
勿扰模式                   → 不弹通知
```

## 文档

- `docs/ASTRBAR_PROTOCOL.md`：客户端使用的协议字段与事件
- `docs/ARCHITECTURE.md`：客户端与平台适配器架构
- `docs/DEVELOPMENT.md`：代码结构与开发约束
- `docs/TESTING.md`：v1.0 回归测试清单
- `docs/ROADMAP.md`：后续版本方向
