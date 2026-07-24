namespace AstrBar.Models;

public enum ChatStreamChunkKind
{
    Text,
    Attachment,
    AttachmentSaved,
    Status,
    Session,
    End
}

public sealed record ChatStreamChunk(
    ChatStreamChunkKind Kind,
    string Value = "",
    bool ReplaceExisting = false,
    string AttachmentType = "",
    string AttachmentId = "");
