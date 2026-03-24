namespace ImageViewer.Core.Caps;

public static class CapsConstraintEvaluator
{
    public static CapsConstraintOptions NormalizeModes(CapsConstraintOptions options)
    {
        if (options.FixedSizeEnabled)
        {
            return options with
            {
                AspectRatioEnabled = false,
            };
        }

        if (options.AspectRatioEnabled)
        {
            return options with
            {
                FixedSizeEnabled = false,
            };
        }

        return options;
    }

    public static PixelSize ApplyCaptureModes(PixelSize source, CapsConstraintOptions options)
    {
        options = NormalizeModes(options);
        var current = source;

        if (options.FixedSizeEnabled && options.FixedWidth > 0 && options.FixedHeight > 0)
        {
            current = new PixelSize(options.FixedWidth, options.FixedHeight);
        }
        else if (options.AspectRatioEnabled && options.AspectRatioX > 0 && options.AspectRatioY > 0)
        {
            current = FitAspect(current, options.AspectRatioX, options.AspectRatioY);
        }

        if (options.ResizeLargestDimensionEnabled && options.ResizeLargestDimension > 0)
        {
            current = ResizeLargestDimension(current, options.ResizeLargestDimension);
        }

        return current;
    }

    public static PixelSize ResizeLargestDimension(PixelSize source, int largestDimensionTarget)
    {
        if (source.Width <= 0 || source.Height <= 0 || largestDimensionTarget <= 0)
        {
            return source;
        }

        var currentLargest = Math.Max(source.Width, source.Height);
        var scale = (double)largestDimensionTarget / currentLargest;
        var newWidth = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        var newHeight = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));
        return new PixelSize(newWidth, newHeight);
    }

    private static PixelSize FitAspect(PixelSize source, int ratioX, int ratioY)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return source;
        }

        var targetRatio = (double)ratioX / ratioY;
        var currentRatio = (double)source.Width / source.Height;

        if (currentRatio > targetRatio)
        {
            var width = (int)Math.Round(source.Height * targetRatio, MidpointRounding.AwayFromZero);
            return new PixelSize(width, source.Height);
        }

        var height = (int)Math.Round(source.Width / targetRatio, MidpointRounding.AwayFromZero);
        return new PixelSize(source.Width, height);
    }
}
