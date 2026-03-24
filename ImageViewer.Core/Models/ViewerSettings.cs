namespace ImageViewer.Core.Models;

public sealed class ViewerSettings
{
    public SortField SortField { get; set; } = SortField.Name;

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    public bool IsSidePanelOpen { get; set; }

    public CapsSettings Caps { get; set; } = new();

    public int JpegFallbackQuality { get; set; } = 92;
}

public sealed class CapsSettings
{
    public bool CapsEnabled { get; set; }

    public bool AutoCap { get; set; }

    public bool SaveCapsEnabled { get; set; }

    public string? SaveCapsDirectory { get; set; }

    public CapsOutputFormat OutputFormat { get; set; } = CapsOutputFormat.SameAsSource;

    public bool CopyToClipboard { get; set; } = true;

    public bool AspectRatioEnabled { get; set; }

    public int AspectRatioX { get; set; } = 16;

    public int AspectRatioY { get; set; } = 9;

    public bool FixedSizeEnabled { get; set; }

    public int FixedWidth { get; set; } = 640;

    public int FixedHeight { get; set; } = 480;

    public bool ResizeLargestDimensionEnabled { get; set; }

    public int ResizeLargestDimension { get; set; } = 1280;
}
