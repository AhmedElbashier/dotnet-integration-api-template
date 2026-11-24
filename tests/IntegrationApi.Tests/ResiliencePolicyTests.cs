using System.Net;
using IntegrationApi.Crm;
using IntegrationApi.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace IntegrationApi.Tests;

public class ResiliencePolicyTests
{
    private static HttpClient ClientWith(ScriptedHandler handler, int retries)
    {
        var polly = new PolicyHttpMessageHandlerShim(ResiliencePolicies.RetryPolicy(retries, TimeSpan.FromMilliseconds(1)))
        {
            InnerHandler = handler
        };
        return new HttpClient(polly) { BaseAddress = new Uri("http://crm.test/") };
    }

    [Fact]
    public async Task Transient_5xx_is_retried_then_succeeds()
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            _ => ScriptedHandler.Status(HttpStatusCode.BadGateway),
            _ => ScriptedHandler.Status(HttpStatusCode.OK));
        var client = new CrmClient(ClientWith(handler, 3), NullLogger<CrmClient>.Instance);

        Assert.True(await client.PingAsync());
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Rate_limit_429_is_retried()
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Status(HttpStatusCode.TooManyRequests),
            _ => ScriptedHandler.Status(HttpStatusCode.OK));
        var client = new CrmClient(ClientWith(handler, 3), NullLogger<CrmClient>.Instance);

        Assert.True(await client.PingAsync());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Status(HttpStatusCode.BadRequest));
        var client = new CrmClient(ClientWith(handler, 3), NullLogger<CrmClient>.Instance);

        await Assert.ThrowsAsync<CrmApiException>(() =>
            client.CreateContactAsync(new("a@b.co", "A B", null), "key-1"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_configured_retries()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Status(HttpStatusCode.InternalServerError));
        var client = new CrmClient(ClientWith(handler, 2), NullLogger<CrmClient>.Instance);

        Assert.False(await client.PingAsync());
        Assert.Equal(3, handler.Calls); // 1 try + 2 retries
    }

    /// <summary>Minimal DelegatingHandler that runs a Polly policy (mirrors what AddPolicyHandler wires up).</summary>
    private sealed class PolicyHttpMessageHandlerShim(IAsyncPolicy<HttpResponseMessage> policy) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => policy.ExecuteAsync(token => base.SendAsync(request, token), ct);
    }
}
