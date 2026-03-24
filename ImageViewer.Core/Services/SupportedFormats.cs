namespace ImageViewer.Core.Services;

public static class SupportedFormats
{
    private static readonly HashSet<string> StillExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".gif",
    };

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return StillExtensions.Contains(extension);
    }

    public static bool IsAnimatedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
