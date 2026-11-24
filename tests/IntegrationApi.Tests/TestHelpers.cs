using System.Net;

namespace IntegrationApi.Tests;

/// <summary>A TimeProvider whose clock tests can move.</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>HttpMessageHandler that replays a scripted sequence of responses.</summary>
public sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var step = steps[Math.Min(Calls, steps.Length - 1)];
        Calls++;
        return Task.FromResult(step(request));
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}
