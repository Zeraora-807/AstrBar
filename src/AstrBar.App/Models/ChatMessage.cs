using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace AstrBar.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private ChatMessage(bool isUser)
    {
        IsUser = isUser;
    }

    public bool IsUser { get; }
    public ObservableCollection<MessagePart> Parts { get; } = [];
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public HorizontalAlignment BubbleAlignment =>
        IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Brush BubbleBackground => IsUser
        ? FindBrush("AccentBrush", Color.FromRgb(106, 87, 255))
        : FindBrush("AssistantBubbleBrush", Color.FromRgb(238, 239, 245));

    public Brush TextBrush => IsUser
        ? Brushes.White
        : FindBrush("TextPrimaryBrush", Color.FromRgb(32, 32, 42));

    public static ChatMessage User(
        string content,
        IEnumerable<PendingUploadAttachment>? attachments = null)
    {
        var message = new ChatMessage(true);
        if (!string.IsNullOrWhiteSpace(content))
        {
            message.Parts.Add(new TextMessagePart(content));
        }

        foreach (var attachment in attachments ?? [])
        {
            message.Parts.Add(AttachmentMessagePart.FromLocalUpload(attachment));
        }

        return message;
    }

    public static ChatMessage Assistant() => new(false);

    public TextMessagePart GetOrCreateTextPart()
    {
        if (Parts.LastOrDefault() is TextMessagePart textPart)
        {
            return textPart;
        }

        textPart = new TextMessagePart();
        Parts.Add(textPart);
        return textPart;
    }

    public string PlainText => string.Concat(
        Parts.OfType<TextMessagePart>().Select(part => part.Text));

    public bool HasDisplayableContent => Parts.Any(part =>
        part is AttachmentMessagePart ||
        part is TextMessagePart text && !string.IsNullOrWhiteSpace(text.Text));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(BubbleBackground));
        OnPropertyChanged(nameof(TextBrush));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static Brush FindBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(fallback);
    }
}
