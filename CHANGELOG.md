# Changelog

## 0.3.1

### Fixed

- 修复主题与悬浮球颜色 ComboBox 暴露 record 类型文本的问题
- 初始化向导与设置页统一使用显式主题显示模板

### Changed

- 设置页分页改为圆角胶囊式导航和卡片式内容区域
- 主题及悬浮球选项增加颜色圆点预览

## 0.3.0

### Added

- 首次启动初始化向导
- 服务器公网地址、SSH 登录信息、AstrBot 端口和本地端口配置
- 内置 SSH 本地端口转发，不再依赖外部终端
- SSH KeepAlive、断线自动重连和状态提示
- SSH 主机 SHA256 指纹首次信任与变更拦截
- 本地附件上传到 AstrBot OpenAPI
- 图片缩略图、多选文件与拖拽添加
- 图片、音频、视频、文件四种上传消息部件
- 6 套窗口配色
- 7 种悬浮球颜色，包括跟随主题
- 设置窗口中的连接、聊天、外观和系统分页

### Changed

- 设置、API Key 和 SSH 密码统一存放在 `%LOCALAPPDATA%\AstrBar`
- `AstrBotClient` 同时负责文件上传、聊天 SSE 与连接权限测试
- 主题资源改为动态资源，切换后现有窗口和消息立即更新
- v0.2.0 用户首次启动 v0.3.0 时执行一次初始化迁移

### Security

- API Key 与 SSH 密码使用 Windows DPAPI `CurrentUser` 分别加密
- SSH 密码不会写入 `settings.json` 或程序目录
- 保存新的连接设置前先尝试建立隧道，失败时保留上一次有效配置
- SSH 主机指纹发生变化时拒绝连接
- 文件名由 AstrBot 服务端和客户端共同净化，附件不会自动执行

## 0.2.0

### Added

- AstrBot 图片、音频、文件和视频响应
- 附件下载与本地缓存
- 图片内嵌预览窗口
- 插件命令自动路由
- 自动 / 对话 / 命令发送模式
- 可拖动悬浮圆球
- 边缘吸附、位置保存和未读红点
- `IClientEventSource` 提醒扩展接口
- chat + file scope 测试
