using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Cache;

public sealed class ImageCacheLease : IDisposable
{
    private readonly RefCountedImageCache _owner;
    private readonly RefCountedImageCache.CacheEntry _entry;
    private bool _disposed;

    internal ImageCacheLease(RefCountedImageCache owner, RefCountedImageCache.CacheEntry entry)
    {
        _owner = owner;
        _entry = entry;
        Image = entry.Image;
    }

    public DecodedImage Image { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _owner.Release(_entry);
    }
}
