using System.Collections.Concurrent;
using ImageViewer.Core.Contracts;

namespace ImageViewer.Platform.Windows;

public sealed class WindowsRatingService : IRatingService
{
    private readonly ConcurrentDictionary<string, uint> _processRatings = new(StringComparer.OrdinalIgnoreCase);

    public Task<uint?> GetRatingAsync(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Path.GetFullPath(fullPath);
        return Task.FromResult(_processRatings.TryGetValue(canonical, out var value) ? (uint?)value : null);
    }

    public Task SetRatingAsync(string fullPath, uint rating, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Path.GetFullPath(fullPath);
        _processRatings[canonical] = Math.Min(5, rating);
        return Task.CompletedTask;
    }
}
