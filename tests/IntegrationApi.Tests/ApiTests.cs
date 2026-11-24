using System.Net;
using System.Net.Http.Json;
using System.Text;
using IntegrationApi.Crm;
using IntegrationApi.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntegrationApi.Tests;

/// <summary>End-to-end tests through the real HTTP pipeline with a fake upstream CRM.</summary>
public class ApiTests : IClassFixture<ApiTests.Factory>
{
    private const string Secret = "integration-test-secret";
    private readonly Factory _factory;

    public ApiTests(Factory factory) => _factory = factory;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeCrm Crm { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Crm:BaseUrl", "http://crm.test/");
            builder.UseSetting("Webhooks:SigningSecret", Secret);
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<ICrmClient>();
                s.AddSingleton<ICrmClient>(Crm);
            });
        }
    }

    public sealed class FakeCrm : ICrmClient
    {
        public int CreateCalls;
        public bool Up = true;

        public Task<CrmContact> CreateContactAsync(CreateContactRequest r, string key, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CreateCalls);
            return Task.FromResult(new CrmContact($"c_{key}", r.Email, r.FullName, r.Phone, DateTimeOffset.UnixEpoch));
        }

        public Task<CrmContact?> GetContactAsync(string id, CancellationToken ct = default)
            => Task.FromResult<CrmContact?>(id == "c_known" ? new CrmContact(id, "x@y.z", "X", null, DateTimeOffset.UnixEpoch) : null);

        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(Up);
    }

    [Fact]
    public async Task Liveness_and_readiness_endpoints_respond()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/contacts", json);
        Assert.Contains("/webhooks/crm", json);
    }

    [Fact]
    public async Task Create_contact_requires_idempotency_key()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/contacts", new { email = "a@b.co", fullName = "A B" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_contact_validates_payload()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res = await client.PostAsJsonAsync("/api/contacts", new { email = "not-an-email", fullName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("email", body);
        Assert.Contains("fullName", body);
    }

    [Fact]
    public async Task Same_idempotency_key_calls_upstream_once_and_replays_response()
    {
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var before = _factory.Crm.CreateCalls;

        var first = await client.PostAsJsonAsync("/api/contacts", new { email = "a@b.co", fullName = "A B" });
        var second = await client.PostAsJsonAsync("/api/contacts", new { email = "a@b.co", fullName = "A B" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(before + 1, _factory.Crm.CreateCalls);
        var a = await first.Content.ReadFromJsonAsync<CrmContact>();
        var b = await second.Content.ReadFromJsonAsync<CrmContact>();
        Assert.Equal(a!.Id, b!.Id);
    }

    [Fact]
    public async Task Unknown_contact_returns_404_problem()
    {
        var res = await _factory.CreateClient().GetAsync("/api/contacts/c_missing");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    private static HttpRequestMessage SignedWebhook(string json, string? secret = null, DateTimeOffset? at = null)
    {
        var ts = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/crm")
        {
            Content = new ByteArrayContent(bytes) { Headers = { { "Content-Type", "application/json" } } }
        };
        req.Headers.Add(WebhookSignatureVerifier.TimestampHeader, ts);
        req.Headers.Add(WebhookSignatureVerifier.SignatureHeader, WebhookSignatureVerifier.ComputeSignature(secret ?? Secret, ts, bytes));
        return req;
    }

    [Fact]
    public async Task Signed_webhook_is_accepted_once_then_deduplicated()
    {
        var client = _factory.CreateClient();
        var id = $"evt_{Guid.NewGuid():N}";
        var json = "{\"id\":\"" + id + "\",\"type\":\"contact.updated\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"data\":{\"contactId\":\"c_1\"}}";

        var first = await client.SendAsync(SignedWebhook(json));
        var second = await client.SendAsync(SignedWebhook(json));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("duplicate", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_with_bad_signature_is_rejected()
    {
        var res = await _factory.CreateClient().SendAsync(SignedWebhook("""{"id":"evt_x","type":"t","createdAt":"2026-01-01T00:00:00Z","data":{}}""", secret: "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Replayed_old_webhook_is_rejected()
    {
        var res = await _factory.CreateClient().SendAsync(SignedWebhook("""{"id":"evt_old","type":"t","createdAt":"2026-01-01T00:00:00Z","data":{}}""", at: DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Malformed_but_signed_webhook_returns_400()
    {
        var res = await _factory.CreateClient().SendAsync(SignedWebhook("not json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
