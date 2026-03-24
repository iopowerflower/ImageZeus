namespace ImageViewer.Imaging.Models;

public sealed class DecodeLimits
{
    public int MaxDimension { get; init; } = 32_768;

    public long MaxPixelArea { get; init; } = 256L * 1_000_000L;

    public int MaxFrameCount { get; init; } = 10_000;

    public long MaxFileSizeBytes { get; init; } = 1L * 1024L * 1024L * 1024L;
}
