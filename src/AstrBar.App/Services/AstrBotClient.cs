using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstrBar.Models;

namespace AstrBar.Services;

public sealed class AstrBotClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly MessageRoutingService _messageRoutingService = new();

    public async Task TestConnectionAsync(
        string baseUrl,
        string apiKey,
        string username,
        CancellationToken cancellationToken = default)
    {
        var sessionPath =
            $"api/v1/chat/sessions?username={Uri.EscapeDataString(username)}&page=1&page_size=1";
        await SendScopeProbeAsync(baseUrl, apiKey, sessionPath, cancellationToken);

        // A fake attachment ID is intentional. A key with file scope reaches the
        // endpoint and receives an AstrBot error envelope; a key without the scope
        // is rejected before the handler runs.
        await SendScopeProbeAsync(
            baseUrl,
            apiKey,
            "api/v1/file?attachment_id=astrbar-scope-probe",
            cancellationToken,
            allowApplicationErrorEnvelope: true);
    }


    public async Task<UploadedAttachment> UploadFileAsync(
        AppSettings settings,
        string apiKey,
        PendingUploadAttachment attachment,
        CancellationToken cancellationToken = default)
    {
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
            bufferSize: 81920,
            useAsync: true);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            GuessContentType(attachment.LocalPath));
        form.Add(fileContent, "file", attachment.FileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(settings.BaseUrl, "api/v1/file"))
        {
            Content = form
        };
        AddAuthentication(request, apiKey);

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
            var data = root.TryGetProperty("data", out var dataElement)
                ? dataElement
                : root;
            var attachmentId = GetString(data, "attachment_id") ?? string.Empty;
            var filename = GetString(data, "filename") ?? attachment.FileName;
            var type = GetString(data, "type") ?? KindToMessageType(attachment.Kind);
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                throw new InvalidOperationException("AstrBot 没有返回 attachment_id。");
            }

            return new UploadedAttachment(
                attachmentId,
                filename,
                type,
                attachment.LocalPath);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"无法解析 AstrBot 上传响应：{Trim(detail, 500)}",
                ex);
        }
    }

    private async Task SendScopeProbeAsync(
        string baseUrl,
        string apiKey,
        string relativePath,
        CancellationToken cancellationToken,
        bool allowApplicationErrorEnvelope = false)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(baseUrl, relativePath));
        AddAuthentication(request, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response, detail);
        }

        if (!allowApplicationErrorEnvelope &&
            detail.Contains("\"status\":\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AstrBot 返回错误：{Trim(detail, 500)}");
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        AppSettings settings,
        string apiKey,
        string message,
        SendMode sendMode,
        IReadOnlyCollection<UploadedAttachment>? attachments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var outgoingMessage = _messageRoutingService.BuildOutgoingMessage(
            settings,
            message,
            sendMode);

        var messageParts = new List<ChatRequestPart>();
        if (!string.IsNullOrWhiteSpace(outgoingMessage))
        {
            messageParts.Add(new ChatRequestPart
            {
                Type = "plain",
                Text = outgoingMessage
            });
        }

        foreach (var attachment in attachments ?? Array.Empty<UploadedAttachment>())
        {
            messageParts.Add(new ChatRequestPart
            {
                Type = attachment.Type,
                AttachmentId = attachment.AttachmentId,
                Filename = attachment.FileName
            });
        }

        if (messageParts.Count == 0)
        {
            throw new InvalidOperationException("消息和附件不能同时为空。");
        }

        var payload = new ChatRequest
        {
            Username = settings.Username,
            SessionId = settings.SessionId,
            Message = messageParts.ToArray(),
            Flags = new ChatRequestFlags
            {
                EnableInlineGenUi = true,
                EnableDefaultSystemPrompt = true,
                EnableStreaming = true
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(settings.BaseUrl, "api/v1/chat"))
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
        AddAuthentication(request, apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateHttpException(response, detail);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"AstrBot 没有返回 SSE 流。Content-Type={mediaType}，响应：{Trim(detail, 500)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var dataLines = new List<string>();
        var sawText = false;
        var sawAttachment = false;
        var sawEnd = false;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                foreach (var chunk in ParseEvent(dataLines, sawText, sawAttachment))
                {
                    if (chunk.Kind == ChatStreamChunkKind.Text)
                    {
                        sawText = true;
                    }
                    else if (chunk.Kind == ChatStreamChunkKind.Attachment)
                    {
                        sawAttachment = true;
                    }

                    if (chunk.Kind == ChatStreamChunkKind.End)
                    {
                        sawEnd = true;
                    }

                    yield return chunk;
                }

                dataLines.Clear();
                continue;
            }

            // Heartbeats and comments begin with ':'. They intentionally do not
            // enter the event buffer.
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line[5..].TrimStart());
            }
        }

        if (dataLines.Count > 0)
        {
            foreach (var chunk in ParseEvent(dataLines, sawText, sawAttachment))
            {
                if (chunk.Kind == ChatStreamChunkKind.Text)
                {
                    sawText = true;
                }
                else if (chunk.Kind == ChatStreamChunkKind.Attachment)
                {
                    sawAttachment = true;
                }

                if (chunk.Kind == ChatStreamChunkKind.End)
                {
                    sawEnd = true;
                }

                yield return chunk;
            }
        }

        if (!sawEnd)
        {
            yield return new ChatStreamChunk(ChatStreamChunkKind.End);
        }
    }

    private static IReadOnlyList<ChatStreamChunk> ParseEvent(
        IReadOnlyCollection<string> dataLines,
        bool sawText,
        bool sawAttachment)
    {
        var chunks = new List<ChatStreamChunk>();
        if (dataLines.Count == 0)
        {
            return chunks;
        }

        var raw = string.Join("\n", dataLines);
        if (raw.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
        {
            chunks.Add(new ChatStreamChunk(ChatStreamChunkKind.End));
            return chunks;
        }

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                chunks.Add(new ChatStreamChunk(ChatStreamChunkKind.Text, raw));
            }

            return chunks;
        }

        using (document)
        {
            var root = document.RootElement;
            var messageType = GetString(root, "type") ?? GetString(root, "t") ?? string.Empty;
            var chainType = GetString(root, "chain_type") ?? string.Empty;
            var data = root.TryGetProperty("data", out var dataElement)
                ? dataElement
                : default;

            switch (messageType)
            {
                case "session_id":
                case "session_bound":
                {
                    var sessionId = PayloadText(data);
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        sessionId = GetString(root, "session_id") ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        chunks.Add(new ChatStreamChunk(
                            ChatStreamChunkKind.Session,
                            sessionId));
                    }

                    break;
                }

                case "run_started":
                    chunks.Add(new ChatStreamChunk(
                        ChatStreamChunkKind.Status,
                        "AstrBot 正在处理…"));
                    break;

                case "plain":
                    ParsePlainChunk(chunks, root, data, chainType);
                    break;

                case "image":
                case "record":
                case "file":
                case "video":
                    chunks.Add(new ChatStreamChunk(
                        ChatStreamChunkKind.Attachment,
                        NormalizeAttachmentPayload(PayloadText(data), messageType),
                        AttachmentType: messageType));
                    break;

                case "attachment_saved":
                {
                    var attachmentId = GetString(data, "id") ?? string.Empty;
                    var attachmentType = GetString(data, "type") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(attachmentId))
                    {
                        chunks.Add(new ChatStreamChunk(
                            ChatStreamChunkKind.AttachmentSaved,
                            AttachmentType: attachmentType,
                            AttachmentId: attachmentId));
                    }

                    break;
                }

                case "complete":
                case "break":
                {
                    var finalText = PayloadText(data);
                    if (!sawText && !string.IsNullOrWhiteSpace(finalText))
                    {
                        chunks.Add(new ChatStreamChunk(
                            ChatStreamChunkKind.Text,
                            finalText,
                            ReplaceExisting: true));
                    }

                    break;
                }

                case "error":
                    chunks.Add(new ChatStreamChunk(
                        ChatStreamChunkKind.Text,
                        $"\n\nAstrBot 错误：{PayloadText(data)}"));
                    break;

                case "end":
                    if (!sawText && !sawAttachment)
                    {
                        chunks.Add(new ChatStreamChunk(
                            ChatStreamChunkKind.Text,
                            "AstrBot 已结束处理，但没有返回可显示内容。请检查模型、配置档案或插件命令。"));
                    }

                    chunks.Add(new ChatStreamChunk(ChatStreamChunkKind.End));
                    break;

                case "message_saved":
                    chunks.Add(new ChatStreamChunk(
                        ChatStreamChunkKind.Status,
                        "回复即将完成…"));
                    break;
            }
        }

        return chunks;
    }

    private static void ParsePlainChunk(
        ICollection<ChatStreamChunk> chunks,
        JsonElement root,
        JsonElement data,
        string chainType)
    {
        if (chainType == "reasoning")
        {
            chunks.Add(new ChatStreamChunk(
                ChatStreamChunkKind.Status,
                "AstrBot 正在思考…"));
            return;
        }

        if (chainType == "tool_call")
        {
            chunks.Add(new ChatStreamChunk(
                ChatStreamChunkKind.Status,
                BuildToolStatus(data)));
            return;
        }

        if (chainType == "tool_call_result")
        {
            chunks.Add(new ChatStreamChunk(
                ChatStreamChunkKind.Status,
                "工具或插件执行完成，正在整理结果…"));
            return;
        }

        var text = PayloadText(data);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var replace = root.TryGetProperty("streaming", out var streaming) &&
                      streaming.ValueKind == JsonValueKind.False;
        chunks.Add(new ChatStreamChunk(
            ChatStreamChunkKind.Text,
            text,
            ReplaceExisting: replace));
    }

    private static string NormalizeAttachmentPayload(string raw, string type)
    {
        var prefix = $"[{type.ToUpperInvariant()}]";
        var normalized = raw.Replace(prefix, string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized.Trim();
    }

    private static string BuildToolStatus(JsonElement data)
    {
        try
        {
            JsonElement tool = data;
            if (data.ValueKind == JsonValueKind.String)
            {
                var raw = data.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    using var parsed = JsonDocument.Parse(raw);
                    tool = parsed.RootElement.Clone();
                }
            }

            var name = GetString(tool, "name");
            return string.IsNullOrWhiteSpace(name)
                ? "AstrBot 正在调用工具或插件…"
                : $"正在调用：{name}";
        }
        catch
        {
            return "AstrBot 正在调用工具或插件…";
        }
    }

    private static string PayloadText(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "text", "content", "message" })
            {
                if (value.TryGetProperty(key, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString() ?? string.Empty;
                }
            }
        }

        return value.ToString();
    }

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


    private static string KindToMessageType(AttachmentKind kind)
    {
        return kind switch
        {
            AttachmentKind.Image => "image",
            AttachmentKind.Audio => "record",
            AttachmentKind.Video => "video",
            _ => "file"
        };
    }

    private static string GuessContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
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
    }

    private static void AddAuthentication(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root))
        {
            throw new InvalidOperationException("AstrBot 地址不是有效 URL。");
        }

        return new Uri(root, relativePath);
    }

    private static Exception CreateHttpException(
        HttpResponseMessage response,
        string detail)
    {
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "AstrBot 拒绝认证。请检查 API Key。",
            System.Net.HttpStatusCode.Forbidden =>
                "API Key 权限不足，请确认它包含 chat 和 file scope。",
            System.Net.HttpStatusCode.NotFound =>
                "找不到 AstrBot OpenAPI。请确认服务器版本与地址。",
            _ =>
                $"AstrBot 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}"
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $"\n{Trim(detail, 500)}";
        }

        return new HttpRequestException(message);
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "…";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("username")]
        public required string Username { get; init; }

        [JsonPropertyName("session_id")]
        public required string SessionId { get; init; }

        [JsonPropertyName("message")]
        public required ChatRequestPart[] Message { get; init; }

        [JsonPropertyName("flags")]
        public required ChatRequestFlags Flags { get; init; }
    }

    private sealed class ChatRequestPart
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; init; }

        [JsonPropertyName("attachment_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AttachmentId { get; init; }

        [JsonPropertyName("filename")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Filename { get; init; }
    }

    private sealed class ChatRequestFlags
    {
        [JsonPropertyName("enable_inline_genui")]
        public bool EnableInlineGenUi { get; init; }

        [JsonPropertyName("enable_default_system_prompt")]
        public bool EnableDefaultSystemPrompt { get; init; }

        [JsonPropertyName("enable_streaming")]
        public bool EnableStreaming { get; init; }
    }
}
