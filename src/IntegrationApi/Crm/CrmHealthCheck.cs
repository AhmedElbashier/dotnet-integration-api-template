using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IntegrationApi.Crm;

/// <summary>Readiness check: reports Degraded (not Unhealthy) when the upstream is down,
/// so the pod is not killed because a third party is having a bad day.</summary>
public sealed class CrmHealthCheck(ICrmClient crm) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => await crm.PingAsync(ct)
            ? HealthCheckResult.Healthy("CRM reachable")
            : HealthCheckResult.Degraded("CRM unreachable");
}
