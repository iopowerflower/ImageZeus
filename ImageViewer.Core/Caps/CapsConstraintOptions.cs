namespace ImageViewer.Core.Caps;

public sealed record CapsConstraintOptions
{
    public bool AspectRatioEnabled { get; init; }

    public int AspectRatioX { get; init; } = 1;

    public int AspectRatioY { get; init; } = 1;

    public bool FixedSizeEnabled { get; init; }

    public int FixedWidth { get; init; }

    public int FixedHeight { get; init; }

    public bool ResizeLargestDimensionEnabled { get; init; }

    public int ResizeLargestDimension { get; init; }
}

public readonly record struct PixelSize(int Width, int Height);
