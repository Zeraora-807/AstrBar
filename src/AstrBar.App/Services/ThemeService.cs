using System.Windows;
using System.Windows.Media;
using AstrBar.Models;

namespace AstrBar.Services;

public sealed class ThemeService
{
    private static readonly ThemeOption[] ThemeCatalog =
    [
        new("violet", "紫藤", "#F7F6FC", "#FFFFFFFF", "#6A57FF", "#5946ED", "#EEEAFE", "#20202A", "#747482", "#E3E0EE", "#F7F5FC", "#EEEFF5"),
        new("ocean", "海洋", "#F2F8FC", "#FFFFFFFF", "#1687D9", "#0873BE", "#E4F3FD", "#183244", "#6C7E8A", "#D9E8F1", "#F4F9FC", "#EAF3F8"),
        new("forest", "森林", "#F4F9F5", "#FFFFFFFF", "#2D9A64", "#248052", "#E4F5EB", "#20352A", "#708177", "#DAE9DF", "#F5FAF7", "#EAF3ED"),
        new("sunset", "日落", "#FCF7F3", "#FFFFFFFF", "#E8783A", "#CD6129", "#FCEBDF", "#3E2D25", "#8C766A", "#EEDFD5", "#FCF8F5", "#F5ECE6"),
        new("rose", "蔷薇", "#FCF5F8", "#FFFFFFFF", "#D95785", "#BF3F6D", "#FBE5ED", "#3D2630", "#8B707A", "#ECDCE3", "#FCF7F9", "#F5EAF0"),
        new("graphite", "石墨", "#F4F5F7", "#FFFFFFFF", "#536273", "#414E5D", "#E8EBEE", "#222A31", "#737D86", "#DDE1E5", "#F6F7F8", "#EDEFF1")
    ];

    private static readonly OrbColorOption[] OrbCatalog =
    [
        new("follow", "跟随界面", "#6A57FF", "#5946ED", true),
        new("violet", "星云紫", "#6A57FF", "#5946ED"),
        new("blue", "潮汐蓝", "#1687D9", "#0873BE"),
        new("green", "苔原绿", "#2D9A64", "#248052"),
        new("orange", "篝火橙", "#E8783A", "#CD6129"),
        new("pink", "莓果粉", "#D95785", "#BF3F6D"),
        new("black", "夜行黑", "#34404C", "#252E37")
    ];

    public IReadOnlyList<ThemeOption> Themes => ThemeCatalog;
    public IReadOnlyList<OrbColorOption> OrbColors => OrbCatalog;

    public ThemeOption GetTheme(string? id)
    {
        return ThemeCatalog.FirstOrDefault(item => item.Id == id) ?? ThemeCatalog[0];
    }

    public OrbColorOption GetOrbColor(string? id)
    {
        return OrbCatalog.FirstOrDefault(item => item.Id == id) ?? OrbCatalog[0];
    }

    public void Apply(AppSettings settings)
    {
        if (Application.Current is null)
        {
            return;
        }

        var theme = GetTheme(settings.ThemeId);
        var orb = GetOrbColor(settings.OrbColorId);

        SetBrush("WindowBackgroundBrush", theme.WindowBackground);
        SetBrush("PanelBrush", theme.Panel);
        SetBrush("AccentBrush", theme.Accent);
        SetBrush("AccentHoverBrush", theme.AccentHover);
        SetBrush("AccentSoftBrush", theme.AccentSoft);
        SetBrush("TextPrimaryBrush", theme.TextPrimary);
        SetBrush("TextSecondaryBrush", theme.TextSecondary);
        SetBrush("BorderBrush", theme.Border);
        SetBrush("InputBrush", theme.Input);
        SetBrush("AssistantBubbleBrush", theme.AssistantBubble);

        SetBrush("OrbBrush", orb.FollowsTheme ? theme.Accent : orb.Color);
        SetBrush("OrbHoverBrush", orb.FollowsTheme ? theme.AccentHover : orb.HoverColor);
    }

    private static void SetBrush(string key, string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
