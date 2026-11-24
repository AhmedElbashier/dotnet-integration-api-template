namespace IntegrationApi.Crm;

public interface ICrmClient
{
    Task<CrmContact> CreateContactAsync(CreateContactRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<CrmContact?> GetContactAsync(string id, CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);
}
