using AstrBar.Models;

namespace AstrBar.Services;

public sealed class MessageRoutingService
{
    public string BuildOutgoingMessage(
        AppSettings settings,
        string message,
        SendMode mode)
    {
        var trimmed = message.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return mode switch
        {
            SendMode.RawCommand => trimmed,
            SendMode.LanguageModel => ApplyWakePrefix(settings.WakePrefix, trimmed),
            _ => LooksLikeCommand(trimmed, settings.CommandPrefixes)
                ? trimmed
                : ApplyWakePrefix(settings.WakePrefix, trimmed)
        };
    }

    private static bool LooksLikeCommand(string message, IEnumerable<string>? prefixes)
    {
        foreach (var prefix in prefixes ?? Array.Empty<string>())
        {
            var normalized = prefix?.Trim();
            if (!string.IsNullOrEmpty(normalized) &&
                message.StartsWith(normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ApplyWakePrefix(string? wakePrefix, string message)
    {
        var prefix = wakePrefix?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(prefix) ||
            message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return message;
        }

        return $"{prefix} {message}";
    }
}
