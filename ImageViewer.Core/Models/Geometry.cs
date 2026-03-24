namespace ImageViewer.Core.Models;

public readonly record struct Size2D(double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct Point2D(double X, double Y);
