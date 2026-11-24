namespace IntegrationApi.Webhooks;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Shared secret used for HMAC-SHA256 signatures. Load from env/user-secrets.</summary>
    public string SigningSecret { get; set; } = string.Empty;

    /// <summary>Reject deliveries whose timestamp is older/newer than this (replay protection).</summary>
    public int ToleranceSeconds { get; set; } = 300;
}
