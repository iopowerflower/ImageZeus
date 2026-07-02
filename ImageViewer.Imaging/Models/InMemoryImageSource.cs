namespace ImageViewer.Imaging.Models;

public sealed class InMemoryImageSource
{
    public InMemoryImageSource(byte[] bgraPixels, int width, int height, int stride, string title, int originX = 0, int originY = 0)
    {
        BgraPixels = bgraPixels;
        Width = width;
        Height = height;
        Stride = stride;
        Title = title;
        OriginX = originX;
        OriginY = originY;
    }

    public byte[] BgraPixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public string Title { get; }

    public int OriginX { get; }

    public int OriginY { get; }

    public DecodedFrame ToFrame(TimeSpan duration)
    {
        return new DecodedFrame(Width, Height, Stride, BgraPixels, duration);
    }
}
