using IntegrationApi.Idempotency;

namespace IntegrationApi.Tests;

public class IdempotencyStoreTests
{
    [Fact]
    public void Key_can_be_reserved_only_once()
    {
        var store = new InMemoryIdempotencyStore(new FakeClock(DateTimeOffset.UnixEpoch));
        Assert.True(store.TryReserve("k1"));
        Assert.False(store.TryReserve("k1"));
    }

    [Fact]
    public void Completed_result_is_returned_for_replays()
    {
        var store = new InMemoryIdempotencyStore(new FakeClock(DateTimeOffset.UnixEpoch));
        store.TryReserve("k1");
        store.Complete("k1", "result");
        Assert.True(store.TryGetResult("k1", out var r));
        Assert.Equal("result", r);
    }

    [Fact]
    public void Expired_key_can_be_reserved_again()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryIdempotencyStore(clock, TimeSpan.FromMinutes(1));
        store.TryReserve("k1");
        clock.Now = clock.Now.AddMinutes(2);
        Assert.True(store.TryReserve("k1"));
    }

    [Fact]
    public void Released_key_can_be_retried()
    {
        var store = new InMemoryIdempotencyStore(new FakeClock(DateTimeOffset.UnixEpoch));
        store.TryReserve("k1");
        store.Release("k1");
        Assert.True(store.TryReserve("k1"));
    }
}
