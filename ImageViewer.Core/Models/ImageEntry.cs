namespace ImageViewer.Core.Models;

public sealed record ImageEntry(
    string FullPath,
    string Name,
    DateTimeOffset DateModified,
    long SizeBytes,
    string FileType,
    uint? Rating);
