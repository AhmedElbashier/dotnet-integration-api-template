using System.ComponentModel.DataAnnotations;

namespace IntegrationApi.Crm;

/// <summary>Settings for the upstream CRM ("third-party") API.</summary>
public sealed class CrmOptions
{
    public const string SectionName = "Crm";

    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:8080/mock-crm/";

    /// <summary>Sent as a bearer token. Load from user-secrets / env vars, never commit it.</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(0, 10)]
    public int RetryCount { get; set; } = 3;

    /// <summary>When true the app also hosts a tiny fake CRM under /mock-crm for local demos.</summary>
    public bool UseBuiltInMock { get; set; }
}
