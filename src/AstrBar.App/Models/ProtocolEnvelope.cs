using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstrBar.Models;

public sealed class ProtocolEnvelope
{
    public const string CurrentProtocol = "astrbar/1.0";

    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = CurrentProtocol;

    [JsonPropertyName("id")]
    public string Id { get; init; } = NewId("evt");

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("session_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("device_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceId { get; init; }

    [JsonPropertyName("correlation_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("requires_ack")]
    public bool RequiresAck { get; init; }

    [JsonPropertyName("sequence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Sequence { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; } = JsonSerializer.SerializeToElement(new { });

    public static ProtocolEnvelope Create(
        string type,
        object? payload = null,
        string? sessionId = null,
        string? userId = null,
        string? deviceId = null,
        string? correlationId = null,
        bool requiresAck = false,
        long? sequence = null,
        string? id = null)
    {
        return new ProtocolEnvelope
        {
            Id = id ?? NewId(type.Replace('.', '_')),
            Type = type,
            SessionId = sessionId,
            UserId = userId,
            DeviceId = deviceId,
            CorrelationId = correlationId,
            RequiresAck = requiresAck,
            Sequence = sequence,
            Payload = JsonSerializer.SerializeToElement(payload ?? new { })
        };
    }

    public static string NewId(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}";
}

public sealed record ProtocolMessagePart(
    string Type,
    string Text = "",
    string AttachmentId = "",
    string FileName = "",
    string MimeType = "",
    long Size = 0,
    string MessageId = "",
    string UserId = "",
    string Name = "");

public sealed record ProtocolInboundMessage(
    string EventId,
    string SessionId,
    string Origin,
    string NotifyMode,
    string Priority,
    int? ElapsedMilliseconds,
    IReadOnlyList<ProtocolMessagePart> Parts)
{
    public bool IsProactive =>
        string.Equals(Origin, "proactive", StringComparison.OrdinalIgnoreCase);
}

public sealed class ProtocolMessageEventArgs(ProtocolInboundMessage message) : EventArgs
{
    public ProtocolInboundMessage Message { get; } = message;
}

public sealed class ProtocolConnectionStatusEventArgs(
    bool isConnected,
    string status,
    string? serverVersion = null) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
    public string Status { get; } = status;
    public string? ServerVersion { get; } = serverVersion;
}
