# AstrBar Protocol v1 客户端约定

协议版本：

```text
astrbar/1.0
```

## 传输

```text
WebSocket  GET  /astrbar/v1/ws
状态       GET  /astrbar/v1/state
上传       POST /astrbar/v1/attachments
下载       GET  /astrbar/v1/attachments/{attachment_id}
```

除健康检查外，请求使用：

```http
Authorization: Bearer <protocol-token>
```

## 统一信封

```json
{
  "protocol": "astrbar/1.0",
  "id": "msg_...",
  "type": "message.send",
  "timestamp": "2026-07-25T00:00:00.0000000+00:00",
  "session_id": "astrbar-main",
  "user_id": "astrbar-local",
  "device_id": "windows-...",
  "correlation_id": null,
  "requires_ack": true,
  "sequence": 1,
  "payload": {}
}
```

## 握手

客户端连接后第一帧必须是 `client.hello`：

```json
{
  "protocol": "astrbar/1.0",
  "id": "hello_...",
  "type": "client.hello",
  "payload": {
    "device_id": "windows-...",
    "device_name": "KAGE",
    "user_id": "astrbar-local",
    "client_version": "1.0.0",
    "sessions": ["astrbar-main"],
    "capabilities": [
      "message.streaming",
      "attachment.http",
      "delivery.ack",
      "delivery.resume",
      "presence",
      "notification.windows"
    ],
    "presence": {
      "window_visible": false,
      "window_focused": false,
      "do_not_disturb": false
    }
  }
}
```

服务端返回 `server.welcome`。

## 发送消息

```json
{
  "type": "message.send",
  "session_id": "astrbar-main",
  "payload": {
    "user_name": "astrbar-local",
    "parts": [
      {"type": "text", "text": "/help"},
      {
        "type": "image",
        "attachment_id": "att_...",
        "filename": "image.png"
      }
    ]
  }
}
```

## 服务端响应事件

```text
message.accepted
message.start
message.delta
message.complete
typing.start
typing.stop
error
```

流式文字来自 `message.delta.payload.parts`。最终 `message.complete` 可能包含完整正文，客户端在已经收到文字增量时不会重复追加最终正文。

## 主动消息

没有匹配当前请求的 `message.complete` 进入主动消息管线：

```json
{
  "type": "message.complete",
  "session_id": "astrbar-main",
  "payload": {
    "origin": "proactive",
    "reply_to": null,
    "parts": [{"type": "text", "text": "提醒内容"}],
    "delivery": {
      "notify": "auto",
      "priority": "normal"
    }
  }
}
```

## 消息组件

```text
text
image
audio
video
file
reply
mention
mention_all
```

媒体组件通过 `attachment_id` 引用 HTTP 附件存储。

## ACK

收到 `requires_ack=true` 的事件后，客户端立即回传：

```json
{
  "type": "ack",
  "correlation_id": "被确认事件 ID",
  "payload": {
    "event_id": "被确认事件 ID"
  }
}
```

ACK 在去重检查前发送，因此离线重放的重复事件也能被服务端正确确认。

## Presence

```json
{
  "type": "client.presence",
  "payload": {
    "window_visible": true,
    "window_focused": false,
    "do_not_disturb": false,
    "idle_seconds": 0
  }
}
```

Presence 是通知决策的辅助信息，不影响第三方插件执行。

## 心跳

服务端定期发送 `ping`，客户端返回 `pong`。连接断开后，客户端按以下间隔自动重试：

```text
1 秒、2 秒、5 秒、10 秒、30 秒，之后保持 30 秒
```

## 取消

客户端会尽力发送 `message.cancel`。Essential v1.0 会返回 `CANCEL_NOT_AVAILABLE`，客户端忽略该兼容性错误并停止本地等待。
