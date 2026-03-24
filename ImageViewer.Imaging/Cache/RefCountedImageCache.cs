using ImageViewer.Imaging.Models;

namespace ImageViewer.Imaging.Cache;

public sealed class RefCountedImageCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly HashSet<CacheEntry> _evictedLeasedEntries = new();

    public RefCountedImageCache(int maxItems)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems));
        }

        MaxItems = maxItems;
    }

    public int MaxItems { get; }

    public bool TryAcquire(string key, out ImageCacheLease? lease)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                TouchLru(entry);
                lease = new ImageCacheLease(this, entry);
                return true;
            }
        }

        lease = null;
        return false;
    }

    public void Put(string key, DecodedImage image)
    {
        List<CacheEntry> toDispose = new();

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.PendingDispose = true;
                RemoveEntry(key, existing);
                if (existing.RefCount > 0)
                {
                    _evictedLeasedEntries.Add(existing);
                }
                else
                {
                    toDispose.Add(existing);
                }
            }

            var node = _lru.AddLast(key);
            _entries[key] = new CacheEntry(image, node);
            EvictUnsafe(toDispose);
        }

        DisposeOutsideLock(toDispose);
    }

    public void Remove(string key)
    {
        CacheEntry? toDispose = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry)) return;
            entry.PendingDispose = true;
            RemoveEntry(key, entry);
            if (entry.RefCount > 0)
                _evictedLeasedEntries.Add(entry);
            else
                toDispose = entry;
        }
        toDispose?.Image.Dispose();
    }

    public void Clear()
    {
        List<CacheEntry> toDispose;
        lock (_gate)
        {
            toDispose = _entries.Values.ToList();
            toDispose.AddRange(_evictedLeasedEntries);

            _entries.Clear();
            _lru.Clear();
            _evictedLeasedEntries.Clear();
        }

        foreach (var entry in toDispose)
        {
            entry.PendingDispose = true;
            if (entry.RefCount == 0)
            {
                entry.Image.Dispose();
            }
        }
    }

    internal void Release(CacheEntry entry)
    {
        var shouldDispose = false;

        lock (_gate)
        {
            entry.RefCount = Math.Max(0, entry.RefCount - 1);
            if (entry.PendingDispose && entry.RefCount == 0)
            {
                _evictedLeasedEntries.Remove(entry);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            entry.Image.Dispose();
        }
    }

    private void EvictUnsafe(List<CacheEntry> toDispose)
    {
        while (_entries.Count > MaxItems && _lru.First is { } first)
        {
            var key = first.Value;
            if (!_entries.TryGetValue(key, out var entry))
            {
                _lru.RemoveFirst();
                continue;
            }

            entry.PendingDispose = true;
            RemoveEntry(key, entry);

            if (entry.RefCount > 0)
            {
                _evictedLeasedEntries.Add(entry);
            }
            else
            {
                toDispose.Add(entry);
            }
        }
    }

    private static void DisposeOutsideLock(IEnumerable<CacheEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.PendingDispose && entry.RefCount == 0)
            {
                entry.Image.Dispose();
            }
        }
    }

    private void RemoveEntry(string key, CacheEntry entry)
    {
        _entries.Remove(key);
        _lru.Remove(entry.LruNode);
    }

    private void TouchLru(CacheEntry entry)
    {
        _lru.Remove(entry.LruNode);
        entry.LruNode = _lru.AddLast(entry.LruNode.Value);
    }

    internal sealed class CacheEntry
    {
        public CacheEntry(DecodedImage image, LinkedListNode<string> lruNode)
        {
            Image = image;
            LruNode = lruNode;
        }

        public DecodedImage Image { get; }

        public LinkedListNode<string> LruNode { get; set; }

        public int RefCount { get; set; }

        public bool PendingDispose { get; set; }
    }
}
