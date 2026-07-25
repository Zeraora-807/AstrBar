using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AstrBar.Models;

namespace AstrBar.Services;

public sealed class AstrBarProtocolClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private readonly MessageRoutingService _messageRoutingService = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
    private readonly object _dedupLock = new();
    private readonly Queue<string> _processedEventOrder = new();
    private readonly HashSet<string> _processedEventIds = new(StringComparer.Ordinal);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCancellation;
    private Task? _receiveTask;
    private Task? _reconnectTask;
    private CancellationTokenSource? _reconnectCancellation;
    private TaskCompletionSource<ProtocolEnvelope>? _welcomeSource;
    private AppSettings? _settings;
    private string _token = string.Empty;
    private long _sequence;
    private bool _disposed;
    private bool _manualDisconnect;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public event EventHandler<ProtocolConnectionStatusEventArgs>? ConnectionStatusChanged;
    public event EventHandler<ProtocolMessageEventArgs>? ProactiveMessageReceived;

    public async Task StartAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken = default)
    {
        _settings = settings;
        _token = token;
        _manualDisconnect = false;
        _reconnectCancellation?.Cancel();
        await EnsureConnectedAsync(settings, token, cancellationToken);
    }

    public async Task TestConnectionAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(settings, token);

        using (var request = new HttpRequestMessage(
                   HttpMethod.Get,
                   BuildHttpUri(settings.BaseUrl, "astrbar/v1/state")))
        {
            AddAuthentication(request, token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpException(response, detail);
            }
        }

        // Probe with an isolated WebSocket so the setup/test button cannot consume
        // offline messages or replace the currently active connection.
        await ProbeWebSocketAsync(settings, token, cancellationToken);
    }

    public async Task ReconnectAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken = default)
    {
        _settings = settings;
        _token = token;
        _manualDisconnect = true;
        _reconnectCancellation?.Cancel();
        await DisconnectCoreAsync();
        _manualDisconnect = false;
        await EnsureConnectedAsync(settings, token, cancellationToken);
    }

    public async Task<UploadedAttachment> UploadFileAsync(
        AppSettings settings,
        string token,
        PendingUploadAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(settings, token);
        if (!File.Exists(attachment.LocalPath))
        {
            throw new FileNotFoundException("待上传的附件不存在。", attachment.LocalPath);
        }

        using var form = new MultipartFormDataContent();
        await using var stream = new FileStream(
            attachment.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            GuessContentType(attachment.LocalPath));
        form.Add(fileContent, "file", attachment.FileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildHttpUri(settings.BaseUrl, "astrbar/v1/attachments"))
        {
            Content = form
        };
        AddAuthentication(request, token);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response, detail);
        }

        try
        {
            using var document = JsonDocument.Parse(detail);
            var root = document.RootElement;
            if (!root.TryGetProperty("attachment", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("AstrBar 网关没有返回 attachment 元数据。");
            }

            var attachmentId = GetString(data, "attachment_id");
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                throw new InvalidOperationException("AstrBar 网关没有返回 attachment_id。");
            }

            return new UploadedAttachment(
                attachmentId,
                GetString(data, "filename") ?? attachment.FileName,
                GetString(data, "part_type") ?? KindToMessageType(attachment.Kind),
                attachment.LocalPath);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"无法解析 AstrBar 附件响应：{Trim(detail, 500)}",
                ex);
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        AppSettings settings,
        string token,
        string message,
        SendMode sendMode,
        IReadOnlyCollection<UploadedAttachment>? attachments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(settings, token, cancellationToken);

        var outgoingMessage = _messageRoutingService.BuildOutgoingMessage(
            settings,
            message,
            sendMode);
        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(outgoingMessage))
        {
            parts.Add(new
            {
                type = "text",
                text = outgoingMessage
            });
        }

        foreach (var attachment in attachments ?? Array.Empty<UploadedAttachment>())
        {
            parts.Add(new
            {
                type = NormalizePartType(attachment.Type),
                attachment_id = attachment.AttachmentId,
                filename = attachment.FileName
            });
        }

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("消息和附件不能同时为空。");
        }

        var requestId = ProtocolEnvelope.NewId("msg");
        var pending = new PendingRequest();
        if (!_pendingRequests.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("无法创建 AstrBar 请求上下文。");
        }

        var envelope = ProtocolEnvelope.Create(
            "message.send",
            payload: new
            {
                user_name = settings.Username,
                parts
            },
            sessionId: settings.SessionId,
            userId: settings.Username,
            deviceId: settings.DeviceId,
            requiresAck: true,
            sequence: Interlocked.Increment(ref _sequence),
            id: requestId);

        try
        {
            await SendEnvelopeAsync(envelope, cancellationToken);
            await foreach (var chunk in pending.Channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
            if (cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await SendEnvelopeAsync(
                        ProtocolEnvelope.Create(
                            "message.cancel",
                            payload: new { request_id = requestId },
                            sessionId: settings.SessionId,
                            userId: settings.Username,
                            deviceId: settings.DeviceId,
                            correlationId: requestId),
                        CancellationToken.None);
                }
                catch
                {
                    // Best effort. The current server advertises cancellation as unavailable.
                }
            }
        }
    }

    public async Task UpdatePresenceAsync(
        bool windowVisible,
        bool windowFocused,
        bool doNotDisturb,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings;
        if (settings is null || !IsConnected)
        {
            return;
        }

        await SendEnvelopeAsync(
            ProtocolEnvelope.Create(
                "client.presence",
                payload: new
                {
                    window_visible = windowVisible,
                    window_focused = windowFocused,
                    do_not_disturb = doNotDisturb,
                    idle_seconds = 0
                },
                sessionId: settings.SessionId,
                userId: settings.Username,
                deviceId: settings.DeviceId,
                sequence: Interlocked.Increment(ref _sequence)),
            cancellationToken);
    }

    private async Task EnsureConnectedAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(settings, token);
        _settings = settings;
        _token = token;

        if (IsConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return;
            }

            await ConnectCoreAsync(settings, token, cancellationToken);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ConnectCoreAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken)
    {
        RaiseConnectionStatus(false, "正在连接 AstrBar Protocol…");
        var connectionCancellation = new CancellationTokenSource();
        _connectionCancellation = connectionCancellation;
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {token.Trim()}");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _webSocket = socket;

        try
        {
            await socket.ConnectAsync(BuildWebSocketUri(settings.BaseUrl), cancellationToken);
            _welcomeSource = new TaskCompletionSource<ProtocolEnvelope>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _receiveTask = Task.Run(
                () => ReceiveLoopAsync(socket, connectionCancellation.Token),
                CancellationToken.None);

            var hello = CreateHelloEnvelope(
                settings,
                Interlocked.Increment(ref _sequence));
            await SendEnvelopeAsync(hello, cancellationToken);

            var welcome = await _welcomeSource.Task.WaitAsync(
                TimeSpan.FromSeconds(12),
                cancellationToken);
            var serverVersion = GetString(welcome.Payload, "server_version");
            RaiseConnectionStatus(
                true,
                $"AstrBar Protocol 已连接 · {serverVersion ?? "server"}",
                serverVersion);
        }
        catch
        {
            _manualDisconnect = true;
            await DisconnectCoreAsync();
            _manualDisconnect = false;
            throw;
        }
    }

    private static async Task ProbeWebSocketAsync(
        AppSettings settings,
        string token,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {token.Trim()}");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(BuildWebSocketUri(settings.BaseUrl), cancellationToken);

        var probeDeviceId = $"{settings.DeviceId}-probe";
        var hello = CreateHelloEnvelope(
            settings,
            sequence: 1,
            deviceId: probeDeviceId,
            sessions: Array.Empty<string>());
        await SendEnvelopeOnSocketAsync(socket, hello, cancellationToken);
        var welcome = await ReceiveEnvelopeFromSocketAsync(
            socket,
            TimeSpan.FromSeconds(12),
            cancellationToken);
        if (!string.Equals(welcome.Type, "server.welcome", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"AstrBar Protocol 握手失败：期望 server.welcome，实际收到 {welcome.Type}。");
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "connection probe complete",
                CancellationToken.None);
        }
    }

    private static ProtocolEnvelope CreateHelloEnvelope(
        AppSettings settings,
        long sequence,
        string? deviceId = null,
        IReadOnlyCollection<string>? sessions = null)
    {
        var resolvedDeviceId = deviceId ?? settings.DeviceId;
        var resolvedSessions = sessions?.ToArray() ?? new[] { settings.SessionId };
        return ProtocolEnvelope.Create(
            "client.hello",
            payload: new
            {
                device_id = resolvedDeviceId,
                device_name = settings.DeviceName,
                user_id = settings.Username,
                client_version = "1.0.0",
                sessions = resolvedSessions,
                capabilities = new[]
                {
                    "message.streaming",
                    "attachment.http",
                    "delivery.ack",
                    "delivery.resume",
                    "presence",
                    "notification.windows"
                },
                presence = new
                {
                    window_visible = false,
                    window_focused = false,
                    do_not_disturb = settings.DoNotDisturb
                }
            },
            userId: settings.Username,
            deviceId: resolvedDeviceId,
            sequence: sequence,
            id: ProtocolEnvelope.NewId("hello"));
    }

    private static async Task SendEnvelopeOnSocketAsync(
        ClientWebSocket socket,
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(envelope, JsonOptions));
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task<ProtocolEnvelope> ReceiveEnvelopeFromSocketAsync(
        ClientWebSocket socket,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                timeoutCancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(
                    "AstrBar Protocol 在完成握手前关闭了连接。");
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }
            await stream.WriteAsync(
                buffer.AsMemory(0, result.Count),
                timeoutCancellation.Token);
        }
        while (!result.EndOfMessage);

        try
        {
            return JsonSerializer.Deserialize<ProtocolEnvelope>(
                       Encoding.UTF8.GetString(
                           stream.GetBuffer(),
                           0,
                           checked((int)stream.Length)),
                       JsonOptions)
                   ?? throw new InvalidOperationException(
                       "AstrBar Protocol 返回了空握手响应。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "AstrBar Protocol 返回了无法解析的握手响应。",
                ex);
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var builder = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State == WebSocketState.Open)
            {
                builder.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }
                    await builder.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                }
                while (!result.EndOfMessage);

                var raw = Encoding.UTF8.GetString(builder.GetBuffer(), 0, (int)builder.Length);
                ProtocolEnvelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(raw, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (envelope is null ||
                    !string.Equals(
                        envelope.Protocol,
                        ProtocolEnvelope.CurrentProtocol,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                await HandleEnvelopeAsync(envelope, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RaiseConnectionStatus(false, $"协议连接中断：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_webSocket, socket))
            {
                _webSocket = null;
            }
            socket.Dispose();
            FailPendingRequests("AstrBar Protocol 连接已断开。");
            if (!_disposed && !_manualDisconnect)
            {
                ScheduleReconnect();
            }
        }
    }

    private async Task HandleEnvelopeAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // 已成功处理过的重放事件不再重复显示，
        // 但仍然回复 ACK，避免服务端反复投递。
        if (IsEventProcessed(envelope.Id))
        {
            await AckEnvelopeIfRequiredAsync(envelope, cancellationToken);
            return;
        }

        try
        {
            // 先完成解析和客户端分发。
            await DispatchEnvelopeAsync(envelope, cancellationToken);

            // 只有处理成功以后，才登记为已处理。
            MarkEventProcessed(envelope.Id);

            // 最后再向服务端确认。
            await AckEnvelopeIfRequiredAsync(envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            // 处理失败时不发送 ACK，也不登记为已处理。
            // 服务端可以在重新连接后重新投递该事件。
            RaiseConnectionStatus(
                IsConnected,
                $"处理服务端事件 {envelope.Type} 失败：{ex.Message}");

            throw;
        }
    }

    private async Task DispatchEnvelopeAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case "server.welcome":
                _welcomeSource?.TrySetResult(envelope);
                break;

            case "ping":
                await SendEnvelopeAsync(
                    ProtocolEnvelope.Create(
                        "pong",
                        payload: new
                        {
                            client_time = DateTimeOffset.UtcNow.ToString("O")
                        },
                        deviceId: _settings?.DeviceId,
                        correlationId: envelope.Id,
                        sequence: Interlocked.Increment(ref _sequence)),
                    cancellationToken);
                break;

            case "pong":
            case "ack":
                break;

            case "message.accepted":
                WriteStatus(envelope, "AstrBot 已接收消息…");
                break;

            case "message.start":
                WriteStatus(envelope, "AstrBot 正在处理…");
                break;

            case "typing.start":
                WriteStatus(envelope, "AstrBot 正在输入…");
                break;

            case "typing.stop":
                WriteStatus(envelope, "正在整理回复…");
                break;

            case "message.delta":
                HandleMessageDelta(envelope);
                break;

            case "message.complete":
                HandleMessageComplete(envelope);
                break;

            case "error":
                HandleError(envelope);
                break;
        }
    }

    private async Task AckEnvelopeIfRequiredAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!envelope.RequiresAck)
        {
            return;
        }

        await SendEnvelopeAsync(
            ProtocolEnvelope.Create(
                "ack",
                payload: new
                {
                    event_id = envelope.Id
                },
                deviceId: _settings?.DeviceId,
                correlationId: envelope.Id,
                sequence: Interlocked.Increment(ref _sequence)),
            cancellationToken);
    }

    private void HandleMessageDelta(ProtocolEnvelope envelope)
    {
        var request = FindPendingRequest(envelope);
        if (request is null)
        {
            return;
        }

        foreach (var part in ParseParts(envelope.Payload))
        {
            EmitPart(request, part, isDelta: true);
        }
    }

    private void HandleMessageComplete(ProtocolEnvelope envelope)
    {
        var request = FindPendingRequest(envelope);
        var message = ParseInboundMessage(envelope);

        // correlation_id 找不到对应请求时，
        // 仍然作为普通入站消息交给主界面显示。
        if (request is null)
        {
            ProactiveMessageReceived?.Invoke(
                this,
                new ProtocolMessageEventArgs(message));
            return;
        }

        var metadataWritten = request.Channel.Writer.TryWrite(
            new ChatStreamChunk(
                ChatStreamChunkKind.Metadata,
                ElapsedMilliseconds: message.ElapsedMilliseconds,
                Origin: message.Origin));

        // 请求的 Channel 已经关闭时，不再静默吞掉回复。
        if (!metadataWritten)
        {
            ProactiveMessageReceived?.Invoke(
                this,
                new ProtocolMessageEventArgs(message));
            return;
        }

        foreach (var part in message.Parts)
        {
            // 已经通过 message.delta 收到过文字时，
            // complete 中的完整正文不再重复追加。
            if (part.Type == "text" && request.SawTextDelta)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(part.AttachmentId) &&
                request.SeenAttachmentIds.Contains(part.AttachmentId))
            {
                continue;
            }

            EmitPart(request, part, isDelta: false);
        }

        request.Channel.Writer.TryWrite(
            new ChatStreamChunk(ChatStreamChunkKind.End));

        request.Channel.Writer.TryComplete();
    }

    private void HandleError(ProtocolEnvelope envelope)
    {
        var code = GetString(envelope.Payload, "code") ?? "PROTOCOL_ERROR";
        var message = GetString(envelope.Payload, "message") ?? "未知协议错误";
        if (string.Equals(code, "CANCEL_NOT_AVAILABLE", StringComparison.Ordinal))
        {
            return;
        }
        var request = FindPendingRequest(envelope);
        if (request is not null)
        {
            request.Channel.Writer.TryComplete(
                new InvalidOperationException(
                    $"AstrBar Protocol 错误 [{code}]：{message}"));
            return;
        }

        RaiseConnectionStatus(IsConnected, $"协议错误 [{code}]：{message}");
    }

    private void EmitPart(
        PendingRequest request,
        ProtocolMessagePart part,
        bool isDelta)
    {
        switch (part.Type)
        {
            case "text":
                if (!string.IsNullOrEmpty(part.Text))
                {
                    request.SawTextDelta |= isDelta;
                    request.Channel.Writer.TryWrite(new ChatStreamChunk(
                        ChatStreamChunkKind.Text,
                        part.Text));
                }
                break;
            case "image":
            case "audio":
            case "record":
            case "video":
            case "file":
                if (!string.IsNullOrWhiteSpace(part.AttachmentId))
                {
                    request.SeenAttachmentIds.Add(part.AttachmentId);
                }
                request.Channel.Writer.TryWrite(new ChatStreamChunk(
                    ChatStreamChunkKind.Attachment,
                    part.FileName,
                    AttachmentType: part.Type));
                request.Channel.Writer.TryWrite(new ChatStreamChunk(
                    ChatStreamChunkKind.AttachmentSaved,
                    AttachmentType: part.Type,
                    AttachmentId: part.AttachmentId));
                break;
            case "reply":
                request.Channel.Writer.TryWrite(new ChatStreamChunk(
                    ChatStreamChunkKind.Text,
                    string.IsNullOrWhiteSpace(part.Text)
                        ? "[引用消息]"
                        : $"[引用：{part.Text}]\n"));
                break;
            case "mention":
                request.Channel.Writer.TryWrite(new ChatStreamChunk(
                    ChatStreamChunkKind.Text,
                    $"@{(string.IsNullOrWhiteSpace(part.Name) ? part.UserId : part.Name)} "));
                break;
            case "mention_all":
                request.Channel.Writer.TryWrite(new ChatStreamChunk(
                    ChatStreamChunkKind.Text,
                    "@所有人 "));
                break;
        }
    }

    private void WriteStatus(ProtocolEnvelope envelope, string status)
    {
        var request = FindPendingRequest(envelope);
        request?.Channel.Writer.TryWrite(new ChatStreamChunk(
            ChatStreamChunkKind.Status,
            status));
    }

    private PendingRequest? FindPendingRequest(ProtocolEnvelope envelope)
    {
        var requestId = envelope.CorrelationId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = GetString(envelope.Payload, "reply_to");
        }
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return null;
        }
        return _pendingRequests.TryGetValue(requestId, out var request)
            ? request
            : null;
    }

    private static ProtocolInboundMessage ParseInboundMessage(ProtocolEnvelope envelope)
    {
        var origin = GetString(envelope.Payload, "origin") ?? "proactive";
        var notifyMode = "auto";
        var priority = "normal";
        if (envelope.Payload.ValueKind == JsonValueKind.Object &&
            envelope.Payload.TryGetProperty("delivery", out var delivery) &&
            delivery.ValueKind == JsonValueKind.Object)
        {
            notifyMode = GetString(delivery, "notify") ?? "auto";
            priority = GetString(delivery, "priority") ?? "normal";
        }

        int? elapsed = null;
        if (envelope.Payload.ValueKind == JsonValueKind.Object &&
            envelope.Payload.TryGetProperty("trace", out var trace) &&
            trace.ValueKind == JsonValueKind.Object &&
            trace.TryGetProperty("elapsed_ms", out var elapsedElement) &&
            elapsedElement.TryGetInt32(out var elapsedValue))
        {
            elapsed = elapsedValue;
        }

        return new ProtocolInboundMessage(
            envelope.Id,
            envelope.SessionId ?? string.Empty,
            origin,
            notifyMode,
            priority,
            elapsed,
            ParseParts(envelope.Payload));
    }

    private static IReadOnlyList<ProtocolMessagePart> ParseParts(JsonElement payload)
    {
        var result = new List<ProtocolMessagePart>();
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            result.Add(new ProtocolMessagePart(
                (GetString(part, "type") ?? "text").ToLowerInvariant(),
                GetString(part, "text") ?? string.Empty,
                GetString(part, "attachment_id") ?? string.Empty,
                GetString(part, "filename") ?? string.Empty,
                GetString(part, "mime_type") ?? string.Empty,
                GetInt64(part, "size"),
                GetString(part, "message_id") ?? string.Empty,
                GetString(part, "user_id") ?? string.Empty,
                GetString(part, "name") ?? string.Empty));
        }
        return result;
    }

    private async Task SendEnvelopeAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var socket = _webSocket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("AstrBar Protocol 尚未连接。");
        }

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void ScheduleReconnect()
    {
        var settings = _settings;
        if (settings is null || !settings.AutoReconnectProtocol || _disposed)
        {
            return;
        }
        if (_reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _reconnectCancellation?.Cancel();
        _reconnectCancellation?.Dispose();
        _reconnectCancellation = new CancellationTokenSource();
        var cancellationToken = _reconnectCancellation.Token;
        _reconnectTask = Task.Run(async () =>
        {
            var delays = new[] { 1, 2, 5, 10, 30 };
            var attempt = 0;
            try
            {
                while (!_disposed && !_manualDisconnect &&
                       !cancellationToken.IsCancellationRequested)
                {
                    var seconds = delays[Math.Min(attempt, delays.Length - 1)];
                    RaiseConnectionStatus(false, $"连接已断开，{seconds} 秒后重试…");
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                    try
                    {
                        if (_settings is not null)
                        {
                            await EnsureConnectedAsync(
                                _settings,
                                _token,
                                cancellationToken);
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        RaiseConnectionStatus(false, $"重连失败：{ex.Message}");
                        attempt++;
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        }, cancellationToken);
    }

    private async Task DisconnectCoreAsync()
    {
        var cancellation = _connectionCancellation;
        _connectionCancellation = null;
        cancellation?.Cancel();

        var socket = _webSocket;
        _webSocket = null;
        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client reconnect",
                        CancellationToken.None);
                }
            }
            catch
            {
            }
            socket.Dispose();
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }
        _receiveTask = null;
        cancellation?.Dispose();
    }

    private void FailPendingRequests(string message)
    {
        foreach (var pending in _pendingRequests.Values)
        {
            pending.Channel.Writer.TryComplete(new IOException(message));
        }
    }

    private bool IsEventProcessed(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        lock (_dedupLock)
        {
            return _processedEventIds.Contains(eventId);
        }
    }

    private bool MarkEventProcessed(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return true;
        }
        lock (_dedupLock)
        {
            if (!_processedEventIds.Add(eventId))
            {
                return false;
            }
            _processedEventOrder.Enqueue(eventId);
            while (_processedEventOrder.Count > 2048)
            {
                _processedEventIds.Remove(_processedEventOrder.Dequeue());
            }
            return true;
        }
    }

    private void RaiseConnectionStatus(
        bool connected,
        string status,
        string? serverVersion = null)
    {
        ConnectionStatusChanged?.Invoke(
            this,
            new ProtocolConnectionStatusEventArgs(connected, status, serverVersion));
    }

    private static void ValidateConfiguration(AppSettings settings, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("请填写 AstrBar Protocol Token。");
        }
        if (string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.SessionId) ||
            string.IsNullOrWhiteSpace(settings.DeviceId))
        {
            throw new InvalidOperationException("user_id、session_id 与 device_id 不能为空。");
        }
        _ = BuildHttpUri(settings.BaseUrl, "astrbar/v1/health");
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var http = BuildHttpUri(baseUrl, "astrbar/v1/ws");
        var builder = new UriBuilder(http)
        {
            Scheme = http.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = http.Port
        };
        return builder.Uri;
    }

    private static Uri BuildHttpUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("AstrBar Protocol 地址不是有效的 HTTP/HTTPS URL。");
        }
        return new Uri(root, relativePath);
    }

    private static void AddAuthentication(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private static Exception CreateHttpException(HttpResponseMessage response, string detail)
    {
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "AstrBar Protocol Token 无效。",
            System.Net.HttpStatusCode.NotFound =>
                "找不到 AstrBar Essential 网关，请确认插件已启用且端口正确。",
            _ =>
                $"AstrBar 网关请求失败：{(int)response.StatusCode} {response.ReasonPhrase}"
        };
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $"\n{Trim(detail, 500)}";
        }
        return new HttpRequestException(message);
    }

    private static string NormalizePartType(string type) =>
        type.ToLowerInvariant() switch
        {
            "record" => "audio",
            "plain" => "text",
            _ => type.ToLowerInvariant()
        };

    private static string KindToMessageType(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => "image",
        AttachmentKind.Audio => "audio",
        AttachmentKind.Video => "video",
        _ => "file"
    };

    private static string GuessContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".txt" or ".md" or ".csv" => "text/plain",
            ".zip" => "application/zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt64(out var value))
        {
            return value;
        }
        return 0;
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _manualDisconnect = true;
        _reconnectCancellation?.Cancel();
        _connectionCancellation?.Cancel();
        _webSocket?.Abort();
        _webSocket?.Dispose();
        _httpClient.Dispose();
        _connectionCancellation?.Dispose();
        _reconnectCancellation?.Dispose();
        _connectLock.Dispose();
        _sendLock.Dispose();
        FailPendingRequests("AstrBar 已退出。");
    }

    private sealed class PendingRequest
    {
        public Channel<ChatStreamChunk> Channel { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<ChatStreamChunk>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
        public bool SawTextDelta { get; set; }
        public HashSet<string> SeenAttachmentIds { get; } = new(StringComparer.Ordinal);
    }
}