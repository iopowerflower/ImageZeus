using ImageViewer.Core.Contracts;
using ImageViewer.Core.Services;
using ImageViewer.Imaging.Cache;
using ImageViewer.Imaging.Decoding;
using ImageViewer.Imaging.Models;
using ImageViewer.Persistence;
using ImageViewer.Platform.Windows;

namespace ImageViewer.App.Services;

public sealed class AppServices
{
    public AppServices(string[] args)
    {
        Args = args;

        CrashLogger = new FileCrashLogger(AppPaths.GetCrashLogPath());
        SettingsStore = new JsonSettingsStore(AppPaths.GetSettingsPath());
        ShellService = new WindowsShellService();
        RatingService = new JsonRatingService(AppPaths.GetRatingsPath());

        var cache = new RefCountedImageCache(maxItems: 12);
        var decoder = new SkiaSharpDecoder();
        DecodePipeline = new ImageDecodePipeline(decoder, cache, new DecodeLimits(), CrashLogger);

        Task.Run(WarmUpSkia);
    }

    private AppServices(string[] args, AppServices parent)
    {
        Args = args;
        CrashLogger = parent.CrashLogger;
        SettingsStore = parent.SettingsStore;
        ShellService = parent.ShellService;
        RatingService = parent.RatingService;
        DecodePipeline = parent.DecodePipeline;
    }

    public AppServices CreateChild(string[] args) => new(args, this);

    private static void WarmUpSkia()
    {
        try { _ = SkiaSharp.SKImageInfo.Empty; }
        catch { /* best-effort warm-up */ }
    }

    public string[] Args { get; }

    public ICrashLogger CrashLogger { get; }

    public ISettingsStore SettingsStore { get; }

    public IShellService ShellService { get; }

    public IRatingService RatingService { get; }

    public ImageDecodePipeline DecodePipeline { get; }
}
