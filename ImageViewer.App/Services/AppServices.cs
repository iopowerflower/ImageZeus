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
        LoggingEnabled = args.Any(IsLoggingEnabledArgument);

        CrashLogger = LoggingEnabled
            ? new FileCrashLogger(AppPaths.GetCrashLogPath())
            : new NullCrashLogger();
        SettingsStore = new JsonSettingsStore(AppPaths.GetSettingsPath());
        ShellService = new WindowsShellService();
        RatingService = new JsonRatingService(AppPaths.GetRatingsPath());

        var cache = new RefCountedImageCache(maxItems: 20);
        var decoder = new SkiaSharpDecoder();
        DecodePipeline = new ImageDecodePipeline(decoder, cache, new DecodeLimits(), CrashLogger);

        Task.Run(WarmUpSkia);
    }

    private AppServices(string[] args, AppServices parent)
    {
        Args = args;
        LoggingEnabled = parent.LoggingEnabled;
        CrashLogger = parent.CrashLogger;
        SettingsStore = parent.SettingsStore;
        ShellService = parent.ShellService;
        RatingService = parent.RatingService;
        DecodePipeline = parent.DecodePipeline;
    }

    public AppServices CreateChild(string[] args) => new(args, this);

    private static bool IsLoggingEnabledArgument(string arg)
    {
        return arg.Equals("--enable-logging", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--logging", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--logs", StringComparison.OrdinalIgnoreCase);
    }

    private static void WarmUpSkia()
    {
        try { _ = SkiaSharp.SKImageInfo.Empty; }
        catch { /* best-effort warm-up */ }
    }

    public string[] Args { get; }

    public bool LoggingEnabled { get; }

    public ICrashLogger CrashLogger { get; }

    public ISettingsStore SettingsStore { get; }

    public IShellService ShellService { get; }

    public IRatingService RatingService { get; }

    public ImageDecodePipeline DecodePipeline { get; }
}
