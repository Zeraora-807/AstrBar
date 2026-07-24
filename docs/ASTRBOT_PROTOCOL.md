# AstrBot OpenAPI 协议说明

## 认证

AstrBar 使用开发者 API Key：

```http
Authorization: Bearer abk_xxx
```

目前至少需要：

```text
chat
file
```

## 1. 上传文件

```http
POST /api/v1/file
Authorization: Bearer abk_xxx
Content-Type: multipart/form-data
```

表单字段：

```text
file=<binary>
```

典型响应：

```json
{
  "status": "ok",
  "data": {
    "attachment_id": "attachment-uuid",
    "filename": "photo.png",
    "type": "image"
  }
}
```

服务端按 MIME 类型返回以下类别之一：

```text
image
record
video
file
```

## 2. 带附件聊天

```http
POST /api/v1/chat
Authorization: Bearer abk_xxx
Accept: text/event-stream
Content-Type: application/json
```

示例：

```json
{
  "username": "astrbar-local",
  "session_id": "astrbar-main",
  "message": [
    {
      "type": "plain",
      "text": "请看这张图片"
    },
    {
      "type": "image",
      "attachment_id": "attachment-uuid",
      "filename": "photo.png"
    }
  ],
  "flags": {
    "enable_inline_genui": true,
    "enable_default_system_prompt": true,
    "enable_streaming": true
  }
}
```

附件消息部件必须使用上传接口返回的 `attachment_id`。客户端本地路径不能放进请求。

纯附件消息也允许发送，因为 `message` 中至少要有一个有效部件，没有 `plain` 也能接受。

## 3. SSE 响应

基本格式：

```text
data: {JSON}

```

心跳：

```text
: heartbeat
```

AstrBar 忽略心跳。

### 常用事件

| 类型 | 用途 |
|---|---|
| `session_id` | 当前会话 ID |
| `user_message_saved` | 用户消息已保存 |
| `plain` | 流式文本、推理或工具状态 |
| `image` | 图片结果 |
| `record` | 音频结果 |
| `file` | 文件结果 |
| `video` | 视频结果 |
| `attachment_saved` | 返回可下载的附件 ID |
| `agent_stats` | Token 与耗时统计 |
| `complete` | 完整结果完成，不应重复追加已流式文字 |
| `message_saved` | Bot 消息已保存 |
| `end` | 本轮结束 |

### 插件与工具状态

`plain` 的 `chain_type` 可能为：

```text
reasoning
tool_call
tool_call_result
```

这些内容由 AstrBar 转换为状态提示，不直接混入正文。

## 4. 下载服务端附件

```http
GET /api/v1/file?attachment_id=<id>
Authorization: Bearer abk_xxx
```

图片自动下载用于预览，其他附件由用户点击后下载。下载完成后不会自动执行。

## 5. 插件命令

AstrBar 不调用独立插件管理 API。消息仍进入 AstrBot 的正常 WebChat 消息管道：

```text
/command
  ↓
/api/v1/chat
  ↓
插件过滤器与 Handler
```
