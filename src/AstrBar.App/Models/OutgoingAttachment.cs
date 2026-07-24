using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AstrBar.Models;

public sealed class PendingUploadAttachment : INotifyPropertyChanged
{
    private string _statusText = "等待发送";
    private bool _isUploading;

    public PendingUploadAttachment(string localPath)
    {
        LocalPath = localPath;
        FileName = Path.GetFileName(localPath);
        SizeBytes = new FileInfo(localPath).Length;
        Kind = FromExtension(Path.GetExtension(localPath));
        PreviewSource = TryLoadPreview(localPath, Kind);
    }

    public string LocalPath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public AttachmentKind Kind { get; }
    public ImageSource? PreviewSource { get; }
    public bool IsImage => Kind == AttachmentKind.Image;
    public string SizeText => FormatBytes(SizeBytes);

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

    public bool IsUploading
    {
        get => _isUploading;
        set
        {
            if (_isUploading == value)
            {
                return;
            }

            _isUploading = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static AttachmentKind FromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => AttachmentKind.Image,
            ".wav" or ".mp3" or ".m4a" or ".ogg" or ".flac" or ".aac" => AttachmentKind.Audio,
            ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" => AttachmentKind.Video,
            _ => AttachmentKind.File
        };
    }

    private static ImageSource? TryLoadPreview(string path, AttachmentKind kind)
    {
        if (kind != AttachmentKind.Image)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 180;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}

public sealed record UploadedAttachment(
    string AttachmentId,
    string FileName,
    string Type,
    string LocalPath);
