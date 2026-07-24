namespace AstrBar.Models;

public sealed record ClientEvent(
    string Type,
    string Title,
    string Body,
    DateTimeOffset? ScheduledAt = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
