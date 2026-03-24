using ImageViewer.Core.Contracts;
using ImageViewer.Imaging.Cache;

namespace ImageViewer.Imaging.Decoding;

public sealed class NavigationLoadCoordinator : IDisposable
{
    private readonly ImageDecodePipeline _pipeline;
    private readonly ICrashLogger _crashLogger;
    private readonly object _gate = new();

    private long _generation;
    private CancellationTokenSource? _preloadCts;
    private bool _disposed;

    public NavigationLoadCoordinator(ImageDecodePipeline pipeline, ICrashLogger crashLogger)
    {
        _pipeline = pipeline;
        _crashLogger = crashLogger;
    }

    public long BeginNavigation()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _generation++;
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = new CancellationTokenSource();
            return _generation;
        }
    }

    public async Task<ImageCacheLease?> LoadCurrentAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _pipeline.LoadAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void PreloadNeighbors(IReadOnlyList<string> paths, long generation)
    {
        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            token = _preloadCts?.Token ?? CancellationToken.None;
        }

        _ = PreloadNeighborsAsync(paths, generation, token);
    }

    private bool IsCurrentGeneration(long generation)
    {
        lock (_gate)
        {
            return generation == _generation;
        }
    }

    private async Task PreloadNeighborsAsync(IReadOnlyList<string> paths, long generation, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentGeneration(generation))
                    return;

                using var lease = await _pipeline.LoadAsync(path, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _crashLogger.Log(ex, "Preload neighbors failed");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NavigationLoadCoordinator));
    }
}
