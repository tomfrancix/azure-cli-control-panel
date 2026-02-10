namespace AzureCliControlPanel.Core.Util;

public sealed class SimpleMemoryCache
{
    private sealed record Entry(DateTimeOffset ExpiresAt, object Value);

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();

    public bool TryGet<T>(string key, out T? value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                if (DateTimeOffset.UtcNow <= e.ExpiresAt)
                {
                    value = (T)e.Value;
                    return true;
                }
                _entries.Remove(key);
            }
        }
        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        lock (_lock)
        {
            _entries[key] = new Entry(DateTimeOffset.UtcNow.Add(ttl), value!);
        }
    }

    public void InvalidateByPrefix(string prefix)
    {
        lock (_lock)
        {
            var keys = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in keys) _entries.Remove(k);
        }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}
