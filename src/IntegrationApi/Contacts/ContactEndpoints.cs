using System.ComponentModel.DataAnnotations;
using IntegrationApi.Crm;
using IntegrationApi.Idempotency;

namespace IntegrationApi.Contacts;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts").WithTags("Contacts");

        group.MapPost("/", CreateAsync)
            .WithName("CreateContact")
            .WithSummary("Creates a contact in the upstream CRM. Requires an Idempotency-Key header.")
            .Produces<CrmContact>(StatusCodes.Status201Created)
            .Produces<CrmContact>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapGet("/{id}", GetAsync)
            .WithName("GetContact")
            .Produces<CrmContact>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, CreateContactRequest body, ICrmClient crm, IIdempotencyStore store, CancellationToken ct)
    {
        var key = request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return Results.Problem(statusCode: 400, title: "Idempotency-Key header is required (max 128 chars).");

        var errors = Validate(body);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var storeKey = $"contacts:create:{key}";
        if (!store.TryReserve(storeKey))
        {
            return store.TryGetResult(storeKey, out var previous) && previous is CrmContact c
                ? Results.Ok(c) // replay: same response, no second upstream call
                : Results.Problem(statusCode: 409, title: "A request with this Idempotency-Key is still in progress.");
        }

        try
        {
            var contact = await crm.CreateContactAsync(body, key, ct);
            store.Complete(storeKey, contact);
            return Results.Created($"/api/contacts/{contact.Id}", contact);
        }
        catch (Exception ex) when (ex is CrmApiException or HttpRequestException or TaskCanceledException)
        {
            store.Release(storeKey); // let the client retry with the same key
            return Results.Problem(statusCode: 502, title: "Upstream CRM unavailable", detail: ex.Message);
        }
    }

    private static async Task<IResult> GetAsync(string id, ICrmClient crm, CancellationToken ct)
        => await crm.GetContactAsync(id, ct) is { } c ? Results.Ok(c) : Results.Problem(statusCode: 404, title: "Contact not found");

    private static Dictionary<string, string[]> Validate(CreateContactRequest body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Email) || !new EmailAddressAttribute().IsValid(body.Email))
            errors["email"] = ["A valid email is required."];
        if (string.IsNullOrWhiteSpace(body.FullName))
            errors["fullName"] = ["Full name is required."];
        return errors;
    }
}
