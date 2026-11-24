using System.Collections.Concurrent;

namespace IntegrationApi.Idempotency;

public sealed class InMemoryIdempotencyStore(TimeProvider clock, TimeSpan? ttl = null) : IIdempotencyStore
{
    private sealed record Entry(DateTimeOffset ExpiresAt, object? Result);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromHours(24);

    public bool TryReserve(string key)
    {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(key, out var existing) && existing.ExpiresAt <= now)
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, existing));
        return _entries.TryAdd(key, new Entry(now + _ttl, null));
    }

    public void Complete(string key, object result)
        => _entries[key] = new Entry(clock.GetUtcNow() + _ttl, result);

    public bool TryGetResult(string key, out object? result)
    {
        result = null;
        if (!_entries.TryGetValue(key, out var e) || e.ExpiresAt <= clock.GetUtcNow()) return false;
        result = e.Result;
        return true;
    }

    public void Release(string key) => _entries.TryRemove(key, out _);
}
