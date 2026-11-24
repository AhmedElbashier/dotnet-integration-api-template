using System.Net;
using System.Net.Http.Json;

namespace IntegrationApi.Crm;

/// <summary>
/// Typed HttpClient for the upstream CRM. Transport concerns (base address, auth header,
/// timeout, retries) are configured in DI; this class only maps HTTP to domain results.
/// </summary>
public sealed class CrmClient(HttpClient http, ILogger<CrmClient> logger) : ICrmClient
{
    public async Task<CrmContact> CreateContactAsync(CreateContactRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "contacts")
        {
            Content = JsonContent.Create(request)
        };
        // Forward the key so the upstream can also de-duplicate retried POSTs.
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("CRM create contact failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new CrmApiException((int)response.StatusCode, "Upstream CRM rejected the request.");
        }

        return (await response.Content.ReadFromJsonAsync<CrmContact>(ct))
               ?? throw new CrmApiException(502, "Upstream CRM returned an empty body.");
    }

    public async Task<CrmContact?> GetContactAsync(string id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"contacts/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new CrmApiException((int)response.StatusCode, "Upstream CRM lookup failed.");
        return await response.Content.ReadFromJsonAsync<CrmContact>(ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CRM health probe failed");
            return false;
        }
    }
}
