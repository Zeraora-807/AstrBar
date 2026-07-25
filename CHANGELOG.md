# Changelog

## 1.0.0

### 通信架构

- 从默认 WebChat/OpenAPI 迁移到 AstrBar Protocol v1
- 新增持久 WebSocket 连接、握手、心跳与指数退避重连
- 新增 HTTP 附件上传与下载
- 新增消息 ACK、事件去重、离线投递重放兼容
- 新增稳定的 user、device、session 和 request 标识

### 消息

- 支持 `Plain`、`Image`、`Record`、`Video`、`File`、`Reply`、`At` 与 `AtAll` 的协议映射
- 支持 AstrBot 流式 `message.delta` 与最终 `message.complete`
- 支持 AstrBot 标准主动消息
- 支持连接状态、输入状态与协议错误显示

### 通知

- 新增因果通知路由
- 区分请求回复与主动消息
- 新增长任务完成阈值
- 新增主动消息通知、错误通知和勿扰模式
- 新增客户端 Presence 上报

### 设置与安全

- 首次启动改为填写 AstrBar Essential Token
- 默认网关端口改为 6190
- 新增 device_id 与设备名称
- Protocol Token 保存到 `protocol-token.bin`
- 保留 DPAPI、SSH 主机指纹校验和内置隧道
- v0.3.x 配置自动进入迁移向导

### 保留功能

- 多主题与独立悬浮球配色
- 托盘、悬浮球、快捷键
- 图片预览、附件上传、下载与保存
- 自动、对话、命令三种发送模式
