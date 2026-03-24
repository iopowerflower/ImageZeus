using ImageViewer.Core.Models;

namespace ImageViewer.Core.Services;

public static class FitZoomCalculator
{
    public static double CalculateFitScale(Size2D imageSize, Size2D viewportSize, bool zoomFix)
    {
        if (zoomFix)
        {
            return 1.0;
        }

        if (imageSize.IsEmpty || viewportSize.IsEmpty)
        {
            return 1.0;
        }

        var widthScale = viewportSize.Width / imageSize.Width;
        var heightScale = viewportSize.Height / imageSize.Height;
        var fit = Math.Min(widthScale, heightScale);
        return Math.Clamp(fit, 0, 1.0);
    }
}
