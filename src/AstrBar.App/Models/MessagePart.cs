using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AstrBar.Models;

public abstract class MessagePart : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TextMessagePart : MessagePart
{
    private string _text = string.Empty;

    public TextMessagePart(string text = "")
    {
        _text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
        }
    }

    public void Append(string text)
    {
        Text += text;
    }
}

public enum AttachmentKind
{
    Image,
    Audio,
    File,
    Video
}

public enum AttachmentLoadState
{
    WaitingForId,
    ReadyToDownload,
    Downloading,
    Ready,
    Failed
}

public sealed class AttachmentMessagePart : MessagePart
{
    private string? _attachmentId;
    private string? _localPath;
    private string _statusText = "等待服务器保存附件…";
    private AttachmentLoadState _state = AttachmentLoadState.WaitingForId;
    private ImageSource? _previewSource;

    public required AttachmentKind Kind { get; init; }
    public string RemoteName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    public string? AttachmentId
    {
        get => _attachmentId;
        set
        {
            if (_attachmentId == value)
            {
                return;
            }

            _attachmentId = value;
            OnPropertyChanged();
        }
    }

    public string? LocalPath
    {
        get => _localPath;
        set
        {
            if (_localPath == value)
            {
                return;
            }

            _localPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpen));
        }
    }

    public AttachmentLoadState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanOpen));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? PreviewSource
    {
        get => _previewSource;
        private set
        {
            _previewSource = value;
            OnPropertyChanged();
        }
    }

    public bool IsImage => Kind == AttachmentKind.Image;
    public bool IsCard => Kind != AttachmentKind.Image;
    public bool CanDownload => State is AttachmentLoadState.ReadyToDownload or AttachmentLoadState.Failed;
    public bool CanOpen => State == AttachmentLoadState.Ready && !string.IsNullOrWhiteSpace(LocalPath);

    public string KindLabel => Kind switch
    {
        AttachmentKind.Image => "图片",
        AttachmentKind.Audio => "音频",
        AttachmentKind.Video => "视频",
        _ => "文件"
    };

    public string IconText => Kind switch
    {
        AttachmentKind.Image => "▧",
        AttachmentKind.Audio => "♫",
        AttachmentKind.Video => "▶",
        _ => "▤"
    };

    public static AttachmentMessagePart FromLocalUpload(PendingUploadAttachment upload)
    {
        var part = new AttachmentMessagePart
        {
            Kind = upload.Kind,
            RemoteName = upload.FileName,
            DisplayName = upload.FileName,
            LocalPath = upload.LocalPath,
            State = AttachmentLoadState.Ready,
            StatusText = upload.SizeText
        };
        part.LoadPreview();
        return part;
    }

    public void LoadPreview()
    {
        if (!IsImage || string.IsNullOrWhiteSpace(LocalPath) || !File.Exists(LocalPath))
        {
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(LocalPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewSource = bitmap;
    }
}
