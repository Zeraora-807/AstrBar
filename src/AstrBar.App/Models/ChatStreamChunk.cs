namespace AstrBar.Models;

public enum ChatStreamChunkKind
{
    Text,
    Attachment,
    AttachmentSaved,
    Status,
    Session,
    Metadata,
    End
}

public sealed record ChatStreamChunk(
    ChatStreamChunkKind Kind,
    string Value = "",
    bool ReplaceExisting = false,
    string AttachmentType = "",
    string AttachmentId = "",
    int? ElapsedMilliseconds = null,
    string Origin = "");
