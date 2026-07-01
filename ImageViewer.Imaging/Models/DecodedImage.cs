namespace ImageViewer.Imaging.Models;

public sealed class DecodedImage : IDisposable
{
    private bool _disposed;

    public DecodedImage(string key, IReadOnlyList<DecodedFrame> frames, int? totalFrameCount = null)
    {
        Key = key;
        Frames = frames;
        TotalFrameCount = totalFrameCount ?? frames.Count;
    }

    public string Key { get; }

    public IReadOnlyList<DecodedFrame> Frames { get; }

    public int TotalFrameCount { get; }

    public bool IsAnimated => TotalFrameCount > 1;

    public bool IsFullyDecoded => Frames.Count >= TotalFrameCount;

    public int Width => Frames.Count == 0 ? 0 : Frames[0].Width;

    public int Height => Frames.Count == 0 ? 0 : Frames[0].Height;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var frame in Frames)
        {
            Array.Clear(frame.Pixels, 0, frame.Pixels.Length);
        }
    }
}
