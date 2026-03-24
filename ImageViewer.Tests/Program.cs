using ImageViewer.Core.Caps;
using ImageViewer.Core.Models;
using ImageViewer.Core.Services;
using ImageViewer.Persistence;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Zoom anchor keeps cursor pixel fixed", TestZoomAnchorMathAsync),
    ("Fit scale never upscales", TestFitScaleAsync),
    ("Sort order across fields asc/desc", TestSortOrderAsync),
    ("Caps constraints and resizing", TestCapsConstraintsAsync),
    ("Persistence roundtrip", TestPersistenceRoundtripAsync),
    ("Decode cancellation blocks stale result", TestDecodeCancellationAsync),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("All tests passed.");

static Task TestZoomAnchorMathAsync()
{
    var oldScale = 1.0;
    var newScale = 2.0;
    var offset = new Point2D(10, 20);
    var anchor = new Point2D(200, 150);

    var oldImageX = (anchor.X - offset.X) / oldScale;
    var oldImageY = (anchor.Y - offset.Y) / oldScale;

    var nextOffset = ZoomAnchorMath.ComputeAnchoredOffset(offset, oldScale, newScale, anchor);
    var newImageX = (anchor.X - nextOffset.X) / newScale;
    var newImageY = (anchor.Y - nextOffset.Y) / newScale;

    AssertAlmostEqual(oldImageX, newImageX, 0.0001, "X anchor mismatch");
    AssertAlmostEqual(oldImageY, newImageY, 0.0001, "Y anchor mismatch");

    return Task.CompletedTask;
}

static Task TestFitScaleAsync()
{
    var fitSmall = FitZoomCalculator.CalculateFitScale(new Size2D(100, 100), new Size2D(1000, 1000), zoomFix: false);
    AssertEqual(1.0, fitSmall, "Small image should remain 1:1");

    var fitLarge = FitZoomCalculator.CalculateFitScale(new Size2D(4000, 2000), new Size2D(1000, 500), zoomFix: false);
    AssertAlmostEqual(0.25, fitLarge, 0.0001, "Large image fit mismatch");

    return Task.CompletedTask;
}

static Task TestSortOrderAsync()
{
    var sorter = new ImageSorter();
    var items = new[]
    {
        new ImageEntry("c.jpg", "c.jpg", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 300, ".jpg", 1),
        new ImageEntry("a.png", "a.png", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 100, ".png", 5),
        new ImageEntry("b.bmp", "b.bmp", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 200, ".bmp", 3),
    };

    var nameAsc = sorter.Sort(items, SortField.Name, SortDirection.Ascending);
    AssertEqual("a.png", nameAsc[0].Name, "Name ascending first item");

    var dateDesc = sorter.Sort(items, SortField.DateModified, SortDirection.Descending);
    AssertEqual("a.png", dateDesc[0].Name, "Date descending first item");

    var sizeAsc = sorter.Sort(items, SortField.Size, SortDirection.Ascending);
    AssertEqual("a.png", sizeAsc[0].Name, "Size ascending first item");

    var typeAsc = sorter.Sort(items, SortField.Type, SortDirection.Ascending);
    AssertEqual("b.bmp", typeAsc[0].Name, "Type ascending first item");

    var ratingDesc = sorter.Sort(items, SortField.Rating, SortDirection.Descending);
    AssertEqual("a.png", ratingDesc[0].Name, "Rating descending first item");

    return Task.CompletedTask;
}

static Task TestCapsConstraintsAsync()
{
    var source = new PixelSize(640, 480);

    var fixedMode = CapsConstraintEvaluator.ApplyCaptureModes(source, new CapsConstraintOptions
    {
        FixedSizeEnabled = true,
        FixedWidth = 200,
        FixedHeight = 100,
        AspectRatioEnabled = true,
        AspectRatioX = 1,
        AspectRatioY = 1,
    });
    AssertEqual(200, fixedMode.Width, "Fixed mode width");
    AssertEqual(100, fixedMode.Height, "Fixed mode height");

    var upscaled = CapsConstraintEvaluator.ResizeLargestDimension(new PixelSize(640, 480), 1280);
    AssertEqual(1280, upscaled.Width, "Upscale width");
    AssertEqual(960, upscaled.Height, "Upscale height");

    var downscaled = CapsConstraintEvaluator.ResizeLargestDimension(new PixelSize(4000, 2000), 1000);
    AssertEqual(1000, downscaled.Width, "Downscale width");
    AssertEqual(500, downscaled.Height, "Downscale height");

    return Task.CompletedTask;
}

static async Task TestPersistenceRoundtripAsync()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "imagezeus-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var path = Path.Combine(tempRoot, "settings.json");

    var store = new JsonSettingsStore(path);
    var initial = new ViewerSettings
    {
        SortField = SortField.Rating,
        SortDirection = SortDirection.Descending,
        IsSidePanelOpen = true,
        JpegFallbackQuality = 88,
        Caps = new CapsSettings
        {
            SaveCapsEnabled = true,
            SaveCapsDirectory = @"C:\caps",
            OutputFormat = CapsOutputFormat.WebP,
            CopyToClipboard = true,
            AspectRatioEnabled = true,
            AspectRatioX = 16,
            AspectRatioY = 9,
            FixedSizeEnabled = false,
            FixedWidth = 320,
            FixedHeight = 180,
            ResizeLargestDimensionEnabled = true,
            ResizeLargestDimension = 1024,
        },
    };

    await store.SaveAsync(initial, CancellationToken.None);
    var loaded = await store.LoadAsync(CancellationToken.None);

    AssertEqual(SortField.Rating, loaded.SortField, "Sort field persisted");
    AssertEqual(SortDirection.Descending, loaded.SortDirection, "Sort direction persisted");
    AssertEqual(true, loaded.IsSidePanelOpen, "Panel state persisted");
    AssertEqual(88, loaded.JpegFallbackQuality, "JPEG quality persisted");
    AssertEqual(true, loaded.Caps.SaveCapsEnabled, "Caps save persisted");
    AssertEqual(CapsOutputFormat.WebP, loaded.Caps.OutputFormat, "Caps output format persisted");

    Directory.Delete(tempRoot, recursive: true);
}

static async Task TestDecodeCancellationAsync()
{
    var pipeline = new LocalDecodePipeline();

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
    var cancelled = false;
    try
    {
        await pipeline.LoadAsync("cancel-test-key", cts.Token);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }

    AssertEqual(true, cancelled, "Expected cancelled decode");

    using var successCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var value = await pipeline.LoadAsync("cancel-test-key", successCts.Token);
    AssertEqual("cancel-test-key", value.Key, "Decode key mismatch after successful retry");
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected={expected}, Actual={actual}");
    }
}

static void AssertAlmostEqual(double expected, double actual, double tolerance, string message)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{message}. Expected={expected}, Actual={actual}");
    }
}

file sealed class LocalDecodePipeline
{
    private readonly Dictionary<string, DecodeValue> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public async Task<DecodeValue> LoadAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        // Simulates an async decode that can be cancelled before it is cached.
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var value = new DecodeValue(key);
        lock (_gate)
        {
            _cache[key] = value;
        }

        return value;
    }
}

file sealed record DecodeValue(string Key);
