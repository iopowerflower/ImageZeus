using ImageViewer.Core.Models;

namespace ImageViewer.Core.Services;

public static class ZoomAnchorMath
{
    public static Point2D ComputeAnchoredOffset(
        Point2D currentOffset,
        double oldScale,
        double newScale,
        Point2D anchorInViewport)
    {
        if (oldScale <= 0 || newScale <= 0)
        {
            return currentOffset;
        }

        var imageX = (anchorInViewport.X - currentOffset.X) / oldScale;
        var imageY = (anchorInViewport.Y - currentOffset.Y) / oldScale;

        return new Point2D(
            anchorInViewport.X - (imageX * newScale),
            anchorInViewport.Y - (imageY * newScale));
    }

    public static Point2D RecenterForViewportChange(
        Size2D oldViewport,
        Size2D newViewport,
        Point2D currentOffset)
    {
        var deltaX = (newViewport.Width - oldViewport.Width) / 2.0;
        var deltaY = (newViewport.Height - oldViewport.Height) / 2.0;
        return new Point2D(currentOffset.X + deltaX, currentOffset.Y + deltaY);
    }
}
