using System.Collections.Concurrent;
using ImageViewer.Core.Contracts;
using ImageViewer.Imaging.Cache;
using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Decoding;

public sealed class ImageDecodePipeline
{
    private readonly IImageDecoder _decoder;
    private readonly RefCountedImageCache _cache;
    private readonly DecodeLimits _limits;
    private readonly ICrashLogger _crashLogger;
    private readonly ConcurrentDictionary<string, Task<ImageCacheLease>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public ImageDecodePipeline(
        IImageDecoder decoder,
        RefCountedImageCache cache,
        DecodeLimits limits,
        ICrashLogger crashLogger)
    {
        _decoder = decoder;
        _cache = cache;
        _limits = limits;
        _crashLogger = crashLogger;
    }

    public void Invalidate(string fullPath)
    {
        var key = Path.GetFullPath(fullPath);
        _cache.Remove(key);
        _inFlight.TryRemove(key, out _);
    }

    public ImageCacheLease? TryAcquireCached(string fullPath)
    {
        var key = Path.GetFullPath(fullPath);
        if (_cache.TryAcquire(key, out var lease) && lease is not null)
            return lease;
        return null;
    }

    public async Task<ImageCacheLease> LoadAsync(string fullPath, CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(fullPath);

        if (_cache.TryAcquire(key, out var cachedLease) && cachedLease is not null)
            return cachedLease;

        var decodeTask = _inFlight.GetOrAdd(key, _ => DecodeAndAcquireAsync(key));

        if (decodeTask.IsFaulted)
        {
            _inFlight.TryRemove(key, out _);
            decodeTask = _inFlight.GetOrAdd(key, _ => DecodeAndAcquireAsync(key));
        }

        ImageCacheLease lease;
        try
        {
            lease = await decodeTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (decodeTask.IsCompleted)
                _inFlight.TryRemove(key, out _);
        }

        if (_cache.TryAcquire(key, out var freshLease) && freshLease is not null)
            return freshLease;

        return lease;
    }

    private async Task<ImageCacheLease> DecodeAndAcquireAsync(string key)
    {
        try
        {
            var decoded = await _decoder.DecodeAsync(key, _limits, CancellationToken.None);
            _cache.Put(key, decoded);

            if (_cache.TryAcquire(key, out var lease) && lease is not null)
                return lease;

            throw new InvalidOperationException($"Cache put succeeded but acquire failed for '{key}'.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _crashLogger.Log(ex, $"Decode failed for {key}");
            _inFlight.TryRemove(key, out _);
            throw;
        }
    }
}
