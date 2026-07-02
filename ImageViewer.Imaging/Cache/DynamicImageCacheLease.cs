using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Cache;

public sealed class DynamicImageCacheLease : IImageLease
{
    private bool _disposed;

    public DynamicImageCacheLease(DecodedImage image)
    {
        Image = image;
    }

    public DecodedImage Image { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Image.Dispose();
    }
}
