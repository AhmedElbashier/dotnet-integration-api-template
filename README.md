# .NET Integration API Template

[![CI](https://github.com/AhmedElbashier/dotnet-integration-api-template/actions/workflows/ci.yml/badge.svg)](https://github.com/AhmedElbashier/dotnet-integration-api-template/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

A small, production-minded **ASP.NET Core 8** service that shows the patterns I use when connecting
a product to third-party APIs (CRMs, payment providers, messaging platforms, contact-center platforms):

| Concern | How it is handled here |
|---|---|
| Outbound calls | **Typed `HttpClient`** (`CrmClient`) configured through DI and validated options |
| Transient failures | **Polly** retry with exponential backoff + jitter on 5xx / 408 / 429, honours `Retry-After` |
| Duplicate POSTs | **`Idempotency-Key`** header: first call hits the upstream, replays return the stored result |
| Inbound events | **Webhook receiver** with **HMAC-SHA256** signature, timestamp-based replay protection, constant-time compare |
| Duplicate deliveries | Webhook **event-id de-duplication** (providers retry; we process once) |
| Errors | RFC 9457 **ProblemDetails** everywhere |
| Docs | **OpenAPI / Swagger UI** at `/swagger` |
| Ops | **Liveness** `/health/live` and **readiness** `/health/ready` (upstream down = *Degraded*, not dead) |
| Shipping | Multi-stage **Dockerfile**, **GitHub Actions** CI, **xUnit** tests (unit + in-memory HTTP) |

The "CRM" here is generic on purpose. Swap `CrmClient` for HubSpot, Salesforce, Zoho, Stripe, WhatsApp
Cloud API, etc. — the plumbing stays the same.

## Architecture

```
            ┌──────────────── IntegrationApi (ASP.NET Core 8) ────────────────┐
 client ──► │ POST /api/contacts ──► Idempotency store ──► CrmClient ──Polly──┼──► Upstream CRM API
            │   (Idempotency-Key)        (replay = same response)            │
            │                                                                │
 provider ─►│ POST /webhooks/crm ──► HMAC verify ──► event-id dedupe ──► 202  │
            │   X-Webhook-Signature / X-Webhook-Timestamp                    │
            │                                                                │
            │ /health/live  /health/ready (CrmHealthCheck)   /swagger        │
            └────────────────────────────────────────────────────────────────┘
```

```
src/IntegrationApi/
  Program.cs                      DI, options validation, HttpClient + Polly, health checks, Swagger
  Crm/                            typed client, options, health check, built-in mock CRM (dev only)
  Contacts/ContactEndpoints.cs    idempotent create + lookup
  Webhooks/                       signature verifier + receiver endpoint
  Idempotency/                    IIdempotencyStore + in-memory implementation (swap for Redis/SQL)
  Resilience/ResiliencePolicies.cs
tests/IntegrationApi.Tests/       xUnit: signature, idempotency, retry policy, end-to-end API tests
```

## Run it

Requirements: .NET 8 SDK (or Docker).

```bash
dotnet run --project src/IntegrationApi      # http://localhost:8080/swagger
```

In `Development` the app also hosts a tiny fake CRM under `/mock-crm`, so everything works with no external account.

```bash
# Create a contact (run twice: the second call returns 200 with the same id, no second upstream call)
curl -X POST localhost:8080/api/contacts \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-1' \
  -d '{"email":"jane@example.com","fullName":"Jane Doe"}'

# Send a signed webhook
BODY='{"id":"evt_1","type":"contact.updated","createdAt":"2026-01-01T00:00:00Z","data":{}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s' "$TS.$BODY" | openssl dgst -sha256 -hmac local-dev-only-secret-change-me -hex | awk '{print $NF}')"
curl -X POST localhost:8080/webhooks/crm -H "X-Webhook-Timestamp: $TS" -H "X-Webhook-Signature: $SIG" -d "$BODY"
```

Docker:

```bash
docker build -t integration-api-template .
docker run -p 8080:8080 -e Crm__BaseUrl=https://your-crm.example/api/ \
  -e Crm__ApiKey=... -e Webhooks__SigningSecret=... integration-api-template
```

### Configuration

| Key | Default | Notes |
|---|---|---|
| `Crm:BaseUrl` | `http://localhost:8080/mock-crm/` | Upstream base URL |
| `Crm:ApiKey` | empty | Sent as `Bearer`. Use env vars / user-secrets, never commit |
| `Crm:RetryCount` | `3` | Polly retries for transient errors |
| `Crm:TimeoutSeconds` | `10` | Per-request timeout |
| `Crm:UseBuiltInMock` | `false` (`true` in Development) | Hosts the fake CRM |
| `Webhooks:SigningSecret` | **required** | App refuses to start without it |
| `Webhooks:ToleranceSeconds` | `300` | Replay window |

## Tests

```bash
dotnet test
```

25 tests cover: valid / tampered / wrong-secret / expired / malformed webhook signatures; idempotency
reserve, replay, expiry and release; retry on 5xx and 429, no retry on 4xx, giving up after N retries;
and end-to-end HTTP tests (health, OpenAPI, validation, idempotent create, webhook accept + dedupe + reject).

## Production notes

- Replace `InMemoryIdempotencyStore` with Redis or a SQL table (unique key + TTL) when running more than one instance.
- Hand webhook events to a queue / outbox and return `202` fast; do the work in a background worker.
- Add a circuit breaker / timeout per attempt (`Microsoft.Extensions.Http.Resilience`) for chatty upstreams.

## License

MIT © Ahmed Elbashier
