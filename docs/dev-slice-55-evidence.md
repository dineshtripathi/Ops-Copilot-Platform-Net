# Dev Slice 55 — Triage Agent + Session Flow: Evidence

## Objective

Add `GET /session/{sessionId}` endpoint that exposes session metadata and recent run summaries.
Expired sessions return `IsExpired: true` rather than 404, giving callers full visibility.

---

## Files Changed

| File | Change |
|------|--------|
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/SessionResponse.cs` | NEW — `SessionRunSummaryDto` + `SessionResponse` sealed records |
| `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs` | ADD — `MapSessionEndpoints()` public extension method; adds `using` for `IAgentRunRepository` |
| `src/Hosts/OpsCopilot.ApiHost/Program.cs` | ADD — `app.MapSessionEndpoints();` after `app.MapAgentRunEndpoints();` |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/SessionEndpointTests.cs` | NEW — 5 unit/integration tests for the GET endpoint |

---

## New DTOs (`SessionResponse.cs`)

```csharp
public sealed record SessionRunSummaryDto(
    Guid           RunId,
    string         Status,
    string?        AlertFingerprint,
    DateTimeOffset CreatedAtUtc);

public sealed record SessionResponse(
    Guid                                SessionId,
    string                              TenantId,
    bool                                IsExpired,
    DateTimeOffset                      CreatedAtUtc,
    DateTimeOffset                      ExpiresAtUtc,
    IReadOnlyList<SessionRunSummaryDto> RecentRuns);
```

---

## Endpoint (`MapSessionEndpoints`)

`GET /session/{sessionId:guid}`

| Header | Required | Purpose |
|--------|----------|---------|
| `x-tenant-id` | Yes | Tenant isolation; mismatch → 403 |

| Scenario | HTTP Status |
|----------|-------------|
| Missing `x-tenant-id` | 400 `ProblemDetails` |
| Session not found | 404 |
| `session.TenantId` ≠ header value | 403 `ProblemDetails` |
| Session found (active or expired) | 200 `SessionResponse` |

`IsExpired` is computed server-side: `DateTimeOffset.UtcNow > session.ExpiresAtUtc`.
`RecentRuns` is capped at 10 via `GetRecentRunsBySessionAsync(sessionId, limit: 10, ct)`.
`GetIncludingExpiredAsync` is used so expired sessions remain visible to callers.

---

## Architecture Note — Separate Extension Method

The GET endpoint is registered via its own `MapSessionEndpoints()` extension method, separate from `MapAgentRunEndpoints()` (which registers POST `/agent/triage` only).

**Reason:** ASP.NET Core minimal APIs validate parameter binding inference for ALL registered endpoints on the first HTTP request (`EndpointRoutingMiddleware.InitializeCoreAsync`). If a GET-only test host calls `MapAgentRunEndpoints()`, the POST endpoint's parameters (`TriageOrchestrator`, `IPackTriageEnricher`, etc.) are unregistered in DI and throw `InvalidOperationException: Failure to infer one or more parameters`.

Separating the methods lets each test host register only the dependencies it needs.

`Program.cs` calls both:
```csharp
app.MapAgentRunEndpoints();         // POST /agent/triage
app.MapSessionEndpoints();          // GET  /session/{sessionId}
```

---

## Test Coverage

| Test | Scenario | Assert |
|------|----------|--------|
| `MissingTenantHeader_Returns400` | No `x-tenant-id` header | 400 |
| `SessionNotFound_Returns404` | `GetIncludingExpiredAsync` returns `null` | 404 |
| `TenantMismatch_Returns403` | `session.TenantId` ≠ header | 403 |
| `ActiveSession_ReturnsOk_IsExpiredFalse_WithRecentRuns` | `ExpiresAtUtc = UtcNow+25min`, 1 run | 200, `IsExpired=false`, `RecentRuns.Count=1`, `AlertFingerprint="fp-001"` |
| `ExpiredSession_ReturnsOk_IsExpiredTrue_EmptyRuns` | `ExpiresAtUtc = UtcNow-1hr`, no runs | 200, `IsExpired=true`, `RecentRuns` empty |

---

## Build Gate

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Command: `dotnet build OpsCopilot.sln -warnaserror`

---

## Test Gate

```
940 passed, 0 failed
```

- Baseline (Slice 54): 935
- New tests (Slice 55): 5
- Total: **940**

Command: `dotnet test OpsCopilot.sln --no-build`
