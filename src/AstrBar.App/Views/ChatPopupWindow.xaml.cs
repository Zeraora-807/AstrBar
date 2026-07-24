using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AstrBar.Models;
using AstrBar.Services;
using Forms = System.Windows.Forms;

namespace AstrBar.Views;

public partial class ChatPopupWindow : Window, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly StartupService _startupService;
    private readonly AstrBotClient _astrBotClient;
    private readonly AttachmentService _attachmentService;
    private readonly NotificationService _notificationService;
    private readonly SshTunnelService _sshTunnelService;
    private readonly ThemeService _themeService;
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private readonly ObservableCollection<PendingUploadAttachment> _pendingUploads = [];
    private readonly Dictionary<AttachmentKind, Queue<AttachmentMessagePart>>
        _pendingAttachments = new();

    private CancellationTokenSource? _requestCancellation;
    private HotkeyService? _hotkeyService;
    private bool _allowClose;

    public ChatPopupWindow(
        SettingsService settingsService,
        CredentialService credentialService,
        StartupService startupService,
        AstrBotClient astrBotClient,
        AttachmentService attachmentService,
        NotificationService notificationService,
        SshTunnelService sshTunnelService,
        ThemeService themeService)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _credentialService = credentialService;
        _startupService = startupService;
        _astrBotClient = astrBotClient;
        _attachmentService = attachmentService;
        _notificationService = notificationService;
        _sshTunnelService = sshTunnelService;
        _themeService = themeService;

        MessagesList.ItemsSource = _messages;
        PendingAttachmentsList.ItemsSource = _pendingUploads;
        Topmost = _settingsService.Current.KeepPopupTopmost;
        _sshTunnelService.StatusChanged += SshTunnelService_StatusChanged;
        StatusText.Text = _sshTunnelService.IsRunning
            ? "SSH 隧道已连接"
            : "就绪 · Ctrl + Alt + Space 呼出";
    }

    public event EventHandler? CollapseToOrbRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? UnreadReplyAvailable;

    private void SshTunnelService_StatusChanged(object? sender, TunnelStatusChangedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() => StatusText.Text = e.Status);
    }

    public void RefreshSettings()
    {
        Topmost = _settingsService.Current.KeepPopupTopmost;
        foreach (var message in _messages)
        {
            message.RefreshTheme();
        }
    }

    public void ShowNearTray()
    {
        Topmost = _settingsService.Current.KeepPopupTopmost;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        PositionNearTaskbar();
        ActivateInput();
    }

    public void ShowAt(Rect bounds)
    {
        Topmost = _settingsService.Current.KeepPopupTopmost;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, bounds.Width);
        Height = Math.Max(MinHeight, bounds.Height);
        Left = bounds.Left;
        Top = bounds.Top;
        ClampToVisibleWorkArea();
        ActivateInput();
    }

    private void ActivateInput()
    {
        Activate();
        _ = Dispatcher.InvokeAsync(() =>
        {
            MessageInput.Focus();
            Keyboard.Focus(MessageInput);
        });
    }


    private void ClampToVisibleWorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point(
            (int)((Left + Width / 2) * dpi.DpiScaleX),
            (int)((Top + Height / 2) * dpi.DpiScaleY));
        var screen = Forms.Screen.FromPoint(center);
        var workLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var workTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var workRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        var workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        Left = Math.Clamp(Left, workLeft + 8, workRight - Width - 8);
        Top = Math.Clamp(Top, workTop + 8, workBottom - Height - 8);
    }

    private void PositionNearTaskbar()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(this);

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Left = screen.WorkingArea.Right / dpi.DpiScaleX - width - 10;
        Top = screen.WorkingArea.Bottom / dpi.DpiScaleY - height - 10;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation is not null)
        {
            _requestCancellation.Cancel();
            return;
        }

        var text = MessageInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) && _pendingUploads.Count == 0)
        {
            return;
        }

        var apiKey = _credentialService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                this,
                "请先在设置中填写 AstrBot API Key。",
                "AstrBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            OpenSettings();
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        var uploads = _pendingUploads.ToList();
        _messages.Add(ChatMessage.User(text, uploads));

        var assistant = ChatMessage.Assistant();
        _messages.Add(assistant);
        MessageInput.Clear();
        _pendingUploads.Clear();
        UpdatePendingAttachmentsVisibility();
        ScrollToBottom();
        _pendingAttachments.Clear();

        _requestCancellation = new CancellationTokenSource();
        SendButton.Content = "停止";
        StatusText.Text = "正在连接服务器…";

        try
        {
            var sendMode = SendModeInput.SelectedIndex switch
            {
                1 => SendMode.LanguageModel,
                2 => SendMode.RawCommand,
                _ => SendMode.Auto
            };

            var uploadedAttachments = new List<UploadedAttachment>();
            foreach (var upload in uploads)
            {
                upload.IsUploading = true;
                upload.StatusText = "正在上传…";
                StatusText.Text = $"正在上传：{upload.FileName}";
                var uploaded = await _astrBotClient.UploadFileAsync(
                    _settingsService.Current,
                    apiKey,
                    upload,
                    _requestCancellation.Token);
                uploadedAttachments.Add(uploaded);
                upload.IsUploading = false;
                upload.StatusText = "已上传";
            }

            StatusText.Text = "正在连接 AstrBot…";
            await foreach (var chunk in _astrBotClient.StreamChatAsync(
                               _settingsService.Current,
                               apiKey,
                               text,
                               sendMode,
                               uploadedAttachments,
                               _requestCancellation.Token))
            {
                switch (chunk.Kind)
                {
                    case ChatStreamChunkKind.Text:
                    {
                        var textPart = assistant.GetOrCreateTextPart();
                        if (chunk.ReplaceExisting)
                        {
                            textPart.Text = chunk.Value;
                        }
                        else
                        {
                            textPart.Append(chunk.Value);
                        }

                        StatusText.Text = "正在接收回复…";
                        ScrollToBottom();
                        break;
                    }

                    case ChatStreamChunkKind.Attachment:
                    {
                        var attachment = CreateAttachmentPart(
                            chunk.AttachmentType,
                            chunk.Value);
                        assistant.Parts.Add(attachment);
                        GetPendingQueue(attachment.Kind).Enqueue(attachment);
                        StatusText.Text = $"收到{attachment.KindLabel}…";
                        ScrollToBottom();
                        break;
                    }

                    case ChatStreamChunkKind.AttachmentSaved:
                    {
                        var kind = ParseAttachmentKind(chunk.AttachmentType);
                        if (GetPendingQueue(kind).TryDequeue(out var attachment))
                        {
                            attachment.AttachmentId = chunk.AttachmentId;
                            attachment.State = AttachmentLoadState.ReadyToDownload;
                            attachment.StatusText = attachment.IsImage
                                ? "正在加载图片…"
                                : "已准备好，可下载";

                            if (attachment.IsImage)
                            {
                                try
                                {
                                    await EnsureAttachmentDownloadedAsync(
                                        attachment,
                                        _requestCancellation.Token);
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch
                                {
                                    StatusText.Text = "图片加载失败，可点击重试";
                                }
                            }
                        }

                        ScrollToBottom();
                        break;
                    }

                    case ChatStreamChunkKind.Status:
                        StatusText.Text = chunk.Value;
                        break;

                    case ChatStreamChunkKind.Session:
                        StatusText.Text = $"会话：{chunk.Value}";
                        break;

                    case ChatStreamChunkKind.End:
                        StatusText.Text = "完成";
                        break;
                }
            }

            CleanAssistantMessage(assistant);
            if (!assistant.HasDisplayableContent)
            {
                assistant.Parts.Add(new TextMessagePart(
                    "AstrBot 已结束处理，但没有返回可显示内容。"));
            }

            if (!IsActive)
            {
                UnreadReplyAvailable?.Invoke(this, EventArgs.Empty);
            }

            if (_settingsService.Current.NotifyOnComplete && !IsActive)
            {
                _notificationService.Show(
                    "AstrBot 已完成回复",
                    BuildNotificationPreview(assistant),
                    _settingsService.Current.SessionId);
            }
        }
        catch (OperationCanceledException)
        {
            if (!assistant.HasDisplayableContent)
            {
                assistant.Parts.Add(new TextMessagePart("已取消。"));
            }

            StatusText.Text = "已取消";
        }
        catch (Exception ex)
        {
            assistant.Parts.Clear();
            assistant.Parts.Add(new TextMessagePart($"连接失败：{ex.Message}"));
            StatusText.Text = "发生错误";

            _notificationService.Show(
                "AstrBar 连接失败",
                ex.Message,
                _settingsService.Current.SessionId);
        }
        finally
        {
            _requestCancellation.Dispose();
            _requestCancellation = null;
            SendButton.Content = "发送";
            ScrollToBottom();
        }
    }

    private static AttachmentMessagePart CreateAttachmentPart(
        string type,
        string rawPayload)
    {
        var kind = ParseAttachmentKind(type);
        var payloadParts = rawPayload.Split('|', 2, StringSplitOptions.TrimEntries);
        var remoteName = payloadParts.ElementAtOrDefault(0) ?? string.Empty;
        var displayName = payloadParts.ElementAtOrDefault(1);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileName(remoteName.Replace('\\', '/'));
        }

        displayName = SanitizeDisplayName(displayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = kind switch
            {
                AttachmentKind.Image => "AstrBot 图片",
                AttachmentKind.Audio => "AstrBot 音频",
                AttachmentKind.Video => "AstrBot 视频",
                _ => "AstrBot 文件"
            };
        }

        return new AttachmentMessagePart
        {
            Kind = kind,
            RemoteName = remoteName,
            DisplayName = displayName
        };
    }


    private static string SanitizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(displayName
            .Where(character => !invalidCharacters.Contains(character))
            .ToArray())
            .Trim();
        return sanitized.Length <= 120 ? sanitized : sanitized[..120];
    }

    private static AttachmentKind ParseAttachmentKind(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "image" => AttachmentKind.Image,
            "record" or "audio" => AttachmentKind.Audio,
            "video" => AttachmentKind.Video,
            _ => AttachmentKind.File
        };
    }

    private Queue<AttachmentMessagePart> GetPendingQueue(AttachmentKind kind)
    {
        if (!_pendingAttachments.TryGetValue(kind, out var queue))
        {
            queue = new Queue<AttachmentMessagePart>();
            _pendingAttachments[kind] = queue;
        }

        return queue;
    }

    private async Task EnsureAttachmentDownloadedAsync(
        AttachmentMessagePart attachment,
        CancellationToken cancellationToken = default)
    {
        if (attachment.State == AttachmentLoadState.Ready &&
            !string.IsNullOrWhiteSpace(attachment.LocalPath) &&
            File.Exists(attachment.LocalPath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(attachment.AttachmentId))
        {
            throw new InvalidOperationException("附件尚未获得 attachment_id。");
        }

        var apiKey = _credentialService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("未配置 AstrBot API Key。");
        }

        attachment.State = AttachmentLoadState.Downloading;
        attachment.StatusText = "正在下载…";

        try
        {
            attachment.LocalPath = await _attachmentService.DownloadAsync(
                _settingsService.Current.BaseUrl,
                apiKey,
                attachment.AttachmentId,
                attachment.RemoteName,
                cancellationToken);
            attachment.State = AttachmentLoadState.Ready;
            attachment.StatusText = "已下载";
            attachment.LoadPreview();
        }
        catch
        {
            attachment.State = AttachmentLoadState.Failed;
            attachment.StatusText = "下载失败，点击重试";
            throw;
        }
    }

    private async void DownloadAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentMessagePart attachment)
        {
            return;
        }

        try
        {
            await EnsureAttachmentDownloadedAsync(attachment);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "附件下载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentMessagePart attachment)
        {
            return;
        }

        try
        {
            await EnsureAttachmentDownloadedAsync(attachment);
            if (!string.IsNullOrWhiteSpace(attachment.LocalPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = attachment.LocalPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开附件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentMessagePart attachment)
        {
            return;
        }

        try
        {
            await EnsureAttachmentDownloadedAsync(attachment);
            if (string.IsNullOrWhiteSpace(attachment.LocalPath))
            {
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = attachment.DisplayName,
                Filter = "所有文件|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                File.Copy(attachment.LocalPath, dialog.FileName, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法保存附件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImageAttachment_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentMessagePart attachment)
        {
            return;
        }

        try
        {
            await EnsureAttachmentDownloadedAsync(attachment);
            if (!string.IsNullOrWhiteSpace(attachment.LocalPath))
            {
                new ImagePreviewWindow(attachment.LocalPath)
                {
                    Owner = this
                }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法预览图片", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void CleanAssistantMessage(ChatMessage assistant)
    {
        foreach (var textPart in assistant.Parts.OfType<TextMessagePart>().ToList())
        {
            textPart.Text = CleanModelText(textPart.Text);
            if (string.IsNullOrWhiteSpace(textPart.Text))
            {
                assistant.Parts.Remove(textPart);
            }
        }
    }

    private static string CleanModelText(string content)
    {
        var cleaned = content;
        foreach (var marker in new[]
                 {
                     "<|observation|>",
                     "<|assistant|>",
                     "<|endoftext|>",
                     "<|im_end|>"
                 })
        {
            cleaned = cleaned.Replace(marker, string.Empty, StringComparison.Ordinal);
        }

        return cleaned.TrimEnd();
    }

    private static string BuildNotificationPreview(ChatMessage message)
    {
        var normalized = message.PlainText
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var attachmentCount = message.Parts.OfType<AttachmentMessagePart>().Count();
            normalized = attachmentCount > 0
                ? $"返回了 {attachmentCount} 个附件。"
                : "回复已完成。";
        }

        return normalized.Length <= 120
            ? normalized
            : normalized[..120] + "…";
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = "选择要发送给 AstrBot 的附件",
            Filter = "AstrBot 支持的附件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.txt;*.md;*.json;*.csv;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.zip;*.7z;*.wav;*.mp3;*.m4a;*.ogg;*.flac;*.mp4;*.webm;*.mov;*.mkv|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPendingFiles(dialog.FileNames);
        }
    }

    private void Composer_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Composer_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPendingFiles(files);
        }
        e.Handled = true;
    }

    private void AddPendingFiles(IEnumerable<string> paths)
    {
        const long maxFileSize = 250L * 1024 * 1024;
        const int maxAttachmentCount = 8;

        foreach (var path in paths)
        {
            if (_pendingUploads.Count >= maxAttachmentCount)
            {
                MessageBox.Show(this, $"每条消息最多添加 {maxAttachmentCount} 个附件。", "附件数量限制", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            if (!File.Exists(path) || _pendingUploads.Any(item => string.Equals(item.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var info = new FileInfo(path);
            if (info.Length > maxFileSize)
            {
                MessageBox.Show(this, $"{info.Name} 超过 250 MB，未加入发送队列。", "附件过大", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            _pendingUploads.Add(new PendingUploadAttachment(path));
        }
        UpdatePendingAttachmentsVisibility();
    }

    private void RemovePendingAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PendingUploadAttachment attachment)
        {
            _pendingUploads.Remove(attachment);
            UpdatePendingAttachmentsVisibility();
        }
    }

    private void UpdatePendingAttachmentsVisibility()
    {
        PendingAttachmentsList.Visibility = _pendingUploads.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendButton_Click(SendButton, new RoutedEventArgs());
        }
    }

    private void ScrollToBottom()
    {
        _ = Dispatcher.InvokeAsync(MessagesScroll.ScrollToEnd);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(
            _settingsService,
            _credentialService,
            _startupService,
            _astrBotClient,
            _sshTunnelService,
            _themeService)
        {
            Owner = this
        };

        window.ShowDialog();
        Topmost = _settingsService.Current.KeepPopupTopmost;
    }

    private void CollapseToOrbButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseToOrbRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeyService = new HotkeyService(handle, () =>
            ToggleRequested?.Invoke(this, EventArgs.Empty));

        if (!_hotkeyService.IsRegistered)
        {
            StatusText.Text = "快捷键注册失败，可通过托盘打开";
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    public void Dispose()
    {
        _allowClose = true;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _hotkeyService?.Dispose();
        _sshTunnelService.StatusChanged -= SshTunnelService_StatusChanged;
        _attachmentService.Dispose();
        Close();
    }
}
