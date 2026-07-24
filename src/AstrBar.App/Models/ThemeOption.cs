namespace AstrBar.Models;

public sealed record ThemeOption(
    string Id,
    string DisplayName,
    string WindowBackground,
    string Panel,
    string Accent,
    string AccentHover,
    string AccentSoft,
    string TextPrimary,
    string TextSecondary,
    string Border,
    string Input,
    string AssistantBubble)
{
    public override string ToString() => DisplayName;
}

public sealed record OrbColorOption(
    string Id,
    string DisplayName,
    string Color,
    string HoverColor,
    bool FollowsTheme = false)
{
    public override string ToString() => DisplayName;
}
