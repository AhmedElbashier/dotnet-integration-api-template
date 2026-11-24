namespace IntegrationApi.Idempotency;

/// <summary>
/// Remembers keys (Idempotency-Key headers, webhook event ids) so retried requests
/// are processed once. Swap the in-memory implementation for Redis/SQL in production.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns true if the key was newly reserved; false if it was already seen.</summary>
    bool TryReserve(string key);

    void Complete(string key, object result);

    bool TryGetResult(string key, out object? result);

    void Release(string key);
}
