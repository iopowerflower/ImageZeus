using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Cache;

public interface IImageLease : IDisposable
{
    DecodedImage Image { get; }
}
