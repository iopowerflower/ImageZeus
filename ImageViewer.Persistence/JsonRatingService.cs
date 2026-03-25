using System.Text.Json;
using ImageViewer.Core.Contracts;

namespace ImageViewer.Persistence;

public sealed class JsonRatingService : IRatingService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, uint>? _cache;

    public JsonRatingService(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<uint?> GetRatingAsync(string fullPath, CancellationToken cancellationToken)
    {
        var db = await LoadDbAsync(cancellationToken);
        var key = NormalizePath(fullPath);
        return db.TryGetValue(key, out var stars) && stars > 0 ? stars : null;
    }

    public async Task<IReadOnlyDictionary<string, uint>> GetAllRatingsAsync(CancellationToken cancellationToken)
    {
        return await LoadDbAsync(cancellationToken);
    }

    public async Task SetRatingAsync(string fullPath, uint rating, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var db = _cache ?? await LoadDbCoreAsync(cancellationToken);
            var key = NormalizePath(fullPath);

            if (rating == 0)
                db.Remove(key);
            else
                db[key] = Math.Min(rating, 5);

            await PersistAsync(db, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, uint>> LoadDbAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            return _cache ?? await LoadDbCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, uint>> LoadDbCoreAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;

        if (File.Exists(_filePath))
        {
            try
            {
                await using var stream = File.OpenRead(_filePath);
                var loaded = await JsonSerializer.DeserializeAsync(
                    stream, PersistenceJsonContext.Default.DictionaryStringUInt32, ct);
                _cache = loaded != null
                    ? new Dictionary<string, uint>(loaded, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _cache = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            _cache = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }

    private async Task PersistAsync(Dictionary<string, uint> db, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(
                stream, db, PersistenceJsonContext.Default.DictionaryStringUInt32, ct);
        }
        File.Move(tmp, _filePath, true);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('/', '\\');
}
