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
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageCacheLease> _decodedLeases = new(StringComparer.OrdinalIgnoreCase);

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
        foreach (DecodeMode mode in Enum.GetValues<DecodeMode>())
        {
            var operationKey = BuildOperationKey(key, mode);
            _inFlight.TryRemove(operationKey, out _);
            if (_decodedLeases.TryRemove(operationKey, out var stale))
                stale.Dispose();
        }
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
        return await LoadInternalAsync(fullPath, DecodeMode.FirstFrameOnly, allowPartialCacheHit: true, cancellationToken);
    }

    public async Task<ImageCacheLease> LoadFullAnimationAsync(string fullPath, CancellationToken cancellationToken)
    {
        return await LoadInternalAsync(fullPath, DecodeMode.AllFrames, allowPartialCacheHit: false, cancellationToken);
    }

    private async Task<ImageCacheLease> LoadInternalAsync(
        string fullPath,
        DecodeMode mode,
        bool allowPartialCacheHit,
        CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(fullPath);
        var operationKey = BuildOperationKey(key, mode);

        if (_cache.TryAcquire(key, out var cachedLease) && cachedLease is not null)
        {
            if (allowPartialCacheHit || cachedLease.Image.IsFullyDecoded || !cachedLease.Image.IsAnimated)
                return cachedLease;

            cachedLease.Dispose();
        }

        if (mode == DecodeMode.AllFrames)
            return await DecodeAndCacheAsync(key, mode, cancellationToken);

        var decodeTask = _inFlight.GetOrAdd(operationKey, _ => DecodeAsync(key, mode, CancellationToken.None));

        if (decodeTask.IsFaulted)
        {
            _inFlight.TryRemove(operationKey, out _);
            decodeTask = _inFlight.GetOrAdd(operationKey, _ => DecodeAsync(key, mode, CancellationToken.None));
        }

        try
        {
            await decodeTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (decodeTask.IsCompleted)
                _inFlight.TryRemove(operationKey, out _);
        }

        // The decode holds a lease that's safe from eviction. Transfer it to the caller.
        if (_decodedLeases.TryRemove(operationKey, out var held))
            return held;

        // Fallback: try cache directly (shouldn't normally be needed)
        if (_cache.TryAcquire(key, out var lease) && lease is not null)
            return lease;

        throw new InvalidOperationException($"Decode completed but cache acquire failed for '{key}'.");
    }

    private async Task<ImageCacheLease> DecodeAndCacheAsync(
        string key,
        DecodeMode mode,
        CancellationToken cancellationToken)
    {
        var decoded = await _decoder.DecodeAsync(key, _limits, mode, cancellationToken);
        var lease = _cache.PutAndAcquire(key, decoded);
        if (lease is not null)
            return lease;

        throw new InvalidOperationException($"Decode completed but cache acquire failed for '{key}'.");
    }

    private async Task DecodeAsync(string key, DecodeMode mode, CancellationToken cancellationToken)
    {
        var operationKey = BuildOperationKey(key, mode);
        try
        {
            var lease = await DecodeAndCacheAsync(key, mode, cancellationToken);
            // Hold a lease to prevent eviction before the caller picks it up.
            var old = _decodedLeases.GetOrAdd(operationKey, lease);
            if (!ReferenceEquals(old, lease))
                lease.Dispose();
        }
        catch (Exception ex)
        {
            _crashLogger.Log(ex, $"Decode failed for {key}");
            _inFlight.TryRemove(operationKey, out _);
            throw;
        }
    }

    private static string BuildOperationKey(string key, DecodeMode mode)
    {
        return $"{mode}:{key}";
    }
}
