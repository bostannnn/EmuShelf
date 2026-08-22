using System.Text.Json.Serialization;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>One Drive object as EmuShelf needs it: enough to address, size, and date it.</summary>
public sealed record DriveFile
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = string.Empty;

    [JsonPropertyName("modifiedTime")]
    public DateTimeOffset? ModifiedTime { get; init; }

    /// <summary>The file's parent folder ids. Drive gives a file a single parent since 2020, but the
    /// field is a list; a flat listing carries it so the folder tree can be rebuilt without a listing
    /// per folder.</summary>
    [JsonPropertyName("parents")]
    public IReadOnlyList<string>? Parents { get; init; }

    public bool IsFolder => string.Equals(MimeType, FolderMimeType, StringComparison.Ordinal);
}

internal sealed record DriveFileList
{
    [JsonPropertyName("files")]
    public List<DriveFile>? Files { get; init; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; init; }
}
