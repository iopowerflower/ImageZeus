using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Decoding;

public interface IImageDecoder
{
    Task<DecodedImage> DecodeAsync(string fullPath, DecodeLimits limits, CancellationToken cancellationToken);
}
