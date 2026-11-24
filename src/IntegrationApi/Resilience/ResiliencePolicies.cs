using Polly;
using Polly.Extensions.Http;

namespace IntegrationApi.Resilience;

public static class ResiliencePolicies
{
    /// <summary>
    /// Retries transient failures (network errors, 5xx, 408) and 429 with exponential backoff + jitter.
    /// Honors Retry-After when the upstream sends it.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> RetryPolicy(int retryCount, TimeSpan? baseDelay = null)
    {
        var delay = baseDelay ?? TimeSpan.FromMilliseconds(200);
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(
                retryCount,
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    var retryAfter = outcome.Result?.Headers.RetryAfter?.Delta;
                    if (retryAfter is { } ra && ra < TimeSpan.FromSeconds(30)) return ra;
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
                    return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt - 1)) + jitter;
                },
                onRetryAsync: (_, _, _, _) => Task.CompletedTask);
    }
}
