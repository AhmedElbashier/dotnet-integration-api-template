using System.Net.Http.Headers;
using IntegrationApi.Contacts;
using IntegrationApi.Crm;
using IntegrationApi.Idempotency;
using IntegrationApi.Resilience;
using IntegrationApi.Webhooks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Integration API Template", Version = "v1" }));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIdempotencyStore>(sp => new InMemoryIdempotencyStore(sp.GetRequiredService<TimeProvider>()));

builder.Services.AddOptions<CrmOptions>()
    .Bind(builder.Configuration.GetSection(CrmOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<WebhookOptions>()
    .Bind(builder.Configuration.GetSection(WebhookOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningSecret), "Webhooks:SigningSecret must be set")
    .ValidateOnStart();

// Typed HttpClient + Polly retry policy.
builder.Services.AddHttpClient<ICrmClient, CrmClient>((sp, http) =>
    {
        var o = sp.GetRequiredService<IOptions<CrmOptions>>().Value;
        http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        if (!string.IsNullOrEmpty(o.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("integration-api-template/1.0");
    })
    .AddPolicyHandler((sp, _) =>
        ResiliencePolicies.RetryPolicy(sp.GetRequiredService<IOptions<CrmOptions>>().Value.RetryCount));

builder.Services.AddHealthChecks()
    .AddCheck<CrmHealthCheck>("crm", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapContacts();
app.MapWebhooks();

if (app.Services.GetRequiredService<IOptions<CrmOptions>>().Value.UseBuiltInMock)
    app.MapMockCrm();

app.Run();

public partial class Program;
