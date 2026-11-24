using System.Collections.Concurrent;

namespace IntegrationApi.Crm;

/// <summary>
/// A deliberately tiny fake CRM so the template runs end-to-end with no external account.
/// Enabled with Crm:UseBuiltInMock=true (on in Development). Not for production.
/// </summary>
public static class MockCrmEndpoints
{
    private static readonly ConcurrentDictionary<string, CrmContact> Contacts = new();
    private static readonly ConcurrentDictionary<string, CrmContact> ByIdempotencyKey = new();

    public static IEndpointRouteBuilder MapMockCrm(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mock-crm").ExcludeFromDescription();

        group.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        group.MapPost("/contacts", (HttpRequest req, CreateContactRequest body) =>
        {
            var key = req.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrEmpty(key) && ByIdempotencyKey.TryGetValue(key, out var existing))
                return Results.Ok(existing);

            var contact = new CrmContact($"c_{Guid.NewGuid():N}"[..14], body.Email, body.FullName, body.Phone, DateTimeOffset.UtcNow);
            Contacts[contact.Id] = contact;
            if (!string.IsNullOrEmpty(key)) ByIdempotencyKey[key] = contact;
            return Results.Created($"/mock-crm/contacts/{contact.Id}", contact);
        });

        group.MapGet("/contacts/{id}", (string id) =>
            Contacts.TryGetValue(id, out var c) ? Results.Ok(c) : Results.NotFound());

        return app;
    }
}
