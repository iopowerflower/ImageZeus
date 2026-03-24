using ImageViewer.Core.Models;

namespace ImageViewer.Core.Services;

public sealed class FolderImageIndexBuilder
{
    public IReadOnlyList<ImageEntry> Build(string currentFilePath, IReadOnlyDictionary<string, uint?> ratings)
    {
        var fullPath = Path.GetFullPath(currentFilePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<ImageEntry>();
        }

        var entries = new List<ImageEntry>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!SupportedFormats.IsSupported(path))
            {
                continue;
            }

            var info = new FileInfo(path);
            ratings.TryGetValue(path, out var rating);
            entries.Add(new ImageEntry(
                info.FullName,
                info.Name,
                info.LastWriteTimeUtc,
                info.Length,
                info.Extension,
                rating));
        }

        return entries;
    }
}
