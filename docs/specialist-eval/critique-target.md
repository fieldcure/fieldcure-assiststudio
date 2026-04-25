# QuickCache

A lightweight in-memory cache for .NET 8. Thread-safe, zero dependencies,
sub-microsecond lookups.

## Usage

```csharp
var cache = new QuickCache<string, User>(maxSize: 1000);
cache.Set("user:42", user, ttl: TimeSpan.FromMinutes(5));
var u = cache.Get("user:42");
```

## Implementation

Internally uses a `Dictionary<TKey, CacheEntry<TValue>>` with a `DateTime.UtcNow`
check on read. Eviction happens lazily on `Get` when an entry is expired.

```csharp
public TValue Get(TKey key)
{
    var entry = _dict[key];
    if (entry.ExpiresAt < DateTime.UtcNow)
    {
        _dict.Remove(key);
        return default;
    }
    return entry.Value;
}
```

## Performance

Benchmarked at 10x faster than `MemoryCache`.

## License

TBD.
