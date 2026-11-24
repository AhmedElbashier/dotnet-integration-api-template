using System.Text.Json;
using IntegrationApi.Idempotency;
using Microsoft.Extensions.Options;

namespace IntegrationApi.Webhooks;

public sealed record WebhookEvent(string Id, string Type, DateTimeOffset CreatedAt, JsonElement Data);

public static class WebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/crm", HandleAsync)
           .WithName("ReceiveCrmWebhook")
           .WithTags("Webhooks")
           .WithSummary("Receives signed CRM events (HMAC-SHA256, replay-protected, idempotent).")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status202Accepted)
           .ProducesProblem(StatusCodes.Status400BadRequest)
           .ProducesProblem(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<WebhookOptions> options,
        TimeProvider clock,
        IIdempotencyStore idempotency,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Webhooks");

        // Read the RAW body: signatures are computed over bytes, not over re-serialized JSON.
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var body = ms.ToArray();

        var verifier = new WebhookSignatureVerifier(
            options.Value.SigningSecret, TimeSpan.FromSeconds(options.Value.ToleranceSeconds), clock);
        var result = verifier.Verify(
            request.Headers[WebhookSignatureVerifier.SignatureHeader],
            request.Headers[WebhookSignatureVerifier.TimestampHeader],
            body);

        if (result != SignatureResult.Valid)
        {
            logger.LogWarning("Rejected webhook: {Reason}", result);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid webhook signature", detail: result.ToString());
        }

        WebhookEvent? evt;
        try { evt = JsonSerializer.Deserialize<WebhookEvent>(body, JsonOptions); }
        catch (JsonException) { evt = null; }
        if (evt is null || string.IsNullOrWhiteSpace(evt.Id))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Malformed event payload");

        // Providers retry deliveries; process each event id exactly once.
        if (!idempotency.TryReserve($"webhook:{evt.Id}"))
        {
            logger.LogInformation("Duplicate webhook {EventId} ignored", evt.Id);
            return Results.Ok(new { status = "duplicate", eventId = evt.Id });
        }

        // In a real service: enqueue to a background worker / outbox and return fast.
        logger.LogInformation("Accepted webhook {EventId} of type {Type}", evt.Id, evt.Type);
        idempotency.Complete($"webhook:{evt.Id}", evt.Type);
        return Results.Accepted(value: new { status = "accepted", eventId = evt.Id });
    }
}
