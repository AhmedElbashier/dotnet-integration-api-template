namespace IntegrationApi.Crm;

public sealed record CreateContactRequest(string Email, string FullName, string? Phone);

public sealed record CrmContact(string Id, string Email, string FullName, string? Phone, DateTimeOffset CreatedAt);

/// <summary>Raised when the upstream API returns a non-success status we cannot recover from.</summary>
public sealed class CrmApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
