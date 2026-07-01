using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Decoding;

public interface IImageDecoder
{
    Task<DecodedImage> DecodeAsync(
        string fullPath,
        DecodeLimits limits,
        DecodeMode mode,
        CancellationToken cancellationToken);
}

public enum DecodeMode
{
    FirstFrameOnly,
    AllFrames,
}
