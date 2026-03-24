using ImageViewer.Core.Models;

namespace ImageViewer.Core.Services;

public sealed class ImageSorter
{
    public IReadOnlyList<ImageEntry> Sort(IReadOnlyList<ImageEntry> entries, SortField field, SortDirection direction)
    {
        IOrderedEnumerable<ImageEntry> ordered = field switch
        {
            SortField.Name => entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase),
            SortField.DateModified => entries.OrderBy(x => x.DateModified)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            SortField.Size => entries.OrderBy(x => x.SizeBytes)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            SortField.Type => entries.OrderBy(x => x.FileType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            SortField.Rating => entries.OrderBy(x => x.Rating ?? 0)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase),
        };

        return direction == SortDirection.Descending
            ? ordered.Reverse().ToArray()
            : ordered.ToArray();
    }
}
