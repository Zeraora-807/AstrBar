using System.Net.Http.Headers;

namespace AstrBar.Services;

public sealed class AttachmentService : IDisposable
{
    private const long MaximumAttachmentBytes = 250L * 1024L * 1024L;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private readonly string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AstrBar",
        "Cache",
        "Attachments");

    public async Task<string> DownloadAsync(
        string baseUrl,
        string token,
        string attachmentId,
        string suggestedName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attachmentId))
        {
            throw new InvalidOperationException("附件尚未获得 attachment_id。");
        }

        Directory.CreateDirectory(_cacheDirectory);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(
                baseUrl,
                $"astrbar/v1/attachments/{Uri.EscapeDataString(attachmentId)}"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"附件下载失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{Trim(detail, 300)}");
        }

        if (response.Content.Headers.ContentLength is > MaximumAttachmentBytes)
        {
            throw new InvalidOperationException("附件超过 AstrBar 的 250 MB 下载上限。");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var extension = ResolveExtension(suggestedName, contentType);
        var localPath = Path.Combine(
            _cacheDirectory,
            $"{SanitizeFileStem(attachmentId)}{extension}");
        var temporaryPath = localPath + ".tmp";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaximumAttachmentBytes)
                    {
                        throw new InvalidOperationException("附件超过 AstrBar 的 250 MB 下载上限。");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(temporaryPath, localPath, overwrite: true);
            return localPath;
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }
    }

    private static string ResolveExtension(string suggestedName, string? contentType)
    {
        var extension = Path.GetExtension(suggestedName);
        if (IsSafeExtension(extension))
        {
            return extension.ToLowerInvariant();
        }

        return contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "audio/wav" => ".wav",
            "audio/mpeg" => ".mp3",
            "video/mp4" => ".mp4",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }

    private static bool IsSafeExtension(string extension)
    {
        return extension.Length is >= 2 and <= 12 &&
               extension.Skip(1).All(character => char.IsLetterOrDigit(character));
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(character => !invalid.Contains(character))
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized)
            ? Guid.NewGuid().ToString("N")
            : sanitized;
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root))
        {
            throw new InvalidOperationException("AstrBar Protocol 地址不是有效 URL。");
        }

        return new Uri(root, relativePath);
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
}
