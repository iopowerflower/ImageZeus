using System.Text.Json;
using ImageViewer.Core.Contracts;
using ImageViewer.Core.Models;

namespace ImageViewer.Persistence;

public sealed class DebouncedSettingsWriter : IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ICrashLogger _crashLogger;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();

    private CancellationTokenSource? _pendingWriteCts;
    private ViewerSettings _latest = new();
    private bool _disposed;

    public DebouncedSettingsWriter(ISettingsStore store, ICrashLogger crashLogger, TimeSpan? debounce = null)
    {
        _store = store;
        _crashLogger = crashLogger;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
    }

    public void ScheduleSave(ViewerSettings settings)
    {
        CancellationTokenSource? toCancel;
        lock (_gate)
        {
            ThrowIfDisposed();
            _latest = CloneSettings(settings);
            toCancel = _pendingWriteCts;
            _pendingWriteCts = new CancellationTokenSource();
        }

        toCancel?.Cancel();
        toCancel?.Dispose();

        _ = PersistAfterDelayAsync(_pendingWriteCts.Token);
    }

    public async Task SaveImmediatelyAsync(ViewerSettings settings, CancellationToken cancellationToken)
    {
        CancellationTokenSource? toCancel;
        ViewerSettings snapshot;

        lock (_gate)
        {
            ThrowIfDisposed();
            snapshot = CloneSettings(settings);
            _latest = snapshot;
            toCancel = _pendingWriteCts;
            _pendingWriteCts = null;
        }

        toCancel?.Cancel();
        toCancel?.Dispose();

        await _store.SaveAsync(snapshot, cancellationToken);
    }

    private async Task PersistAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
            await Task.Run(() => _store.SaveAsync(_latest, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _crashLogger.Log(ex, "Debounced settings write failed");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingWriteCts?.Cancel();
            _pendingWriteCts?.Dispose();
            _pendingWriteCts = null;
        }
    }

    private static ViewerSettings CloneSettings(ViewerSettings source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            source, PersistenceJsonContext.Default.ViewerSettings);
        return JsonSerializer.Deserialize(
            bytes, PersistenceJsonContext.Default.ViewerSettings) ?? new ViewerSettings();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DebouncedSettingsWriter));
    }
}
