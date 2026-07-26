# AstrBar v1.0.0

AstrBar 是一个面向 AstrBot 的轻量 Windows 桌面客户端

## 组成

AstrBar v1.0 需要同时部署：

```text
Windows
└─ AstrBar v1.0 客户端

AstrBot 服务器
└─ astrbot_plugin_astrbar_essential v1.0
   └─ AstrBar 平台适配器与 Protocol 网关
```
## 部署

### 1. 安装 Essential 插件

将插件目录放入 AstrBot：

```text
AstrBot/data/plugins/astrbot_plugin_astrbar_essential
```

然后在 AstrBot WebUI 中重载插件

建议配置：

```text
id: astrbar-main
host: 0.0.0.0
port: 61**
token: 自定义文本
heartbeat_interval: 20
max_attachment_mb: 128
attachment_ttl_hours: 24
delivery_ttl_hours: 72
```

### 2. Docker 映射

若 AstrBot 在 Docker 中运行，需映射到服务器回环地址：

```yaml
ports:
  - "127.0.0.1:6190:6190"
```

## Windows 客户端初始化

首次运行填写：

```text
服务器公网 IP 或域名
SSH 端口，默认 22
SSH 用户名与密码
AstrBar Protocol 服务器端口，默认 61**
本地转发端口，默认 61**
AstrBar Protocol Token
user_id
session_id
etc.
```
