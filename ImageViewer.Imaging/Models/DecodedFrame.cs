namespace ImageViewer.Imaging.Models;

public sealed class DecodedFrame
{
    public DecodedFrame(int width, int height, int stride, byte[] pixels, TimeSpan duration)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
        Duration = duration;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public byte[] Pixels { get; }

    public TimeSpan Duration { get; }
}
