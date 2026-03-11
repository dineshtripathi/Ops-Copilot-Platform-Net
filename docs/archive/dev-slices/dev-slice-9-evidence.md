# Dev Slice 9 — SafeActions Real Executor (Single Action, STRICT)

## Evidence Report

**Slice scope:** Add a real HTTP probe executor for action type `http_probe` with SSRF
protection, while preserving the existing dry-run pipeline. Two-flag safety model.

**Commit baseline:** `5f04e37` (Slice 8 — DryRunActionExecutor, 140 tests)

---

## Section A — Safety Model

### Two-flag precedence

| Flag | Key | Default | Purpose |
|------|-----|---------|---------|
| Endpoint guard | `SafeActions:EnableExecution` | `false` | Gate at HTTP layer; `false` → HTTP 501 |
| Probe gate | `SafeActions:EnableRealHttpProbe` | `false` | Route `http_probe` to real executor |

**Precedence chain:**

1. `EnableExecution = false` → **501 Not Implemented** (endpoint guard, no executor invoked)
2. `EnableExecution = true` + `EnableRealHttpProbe = false` → all action types routed to **DryRunActionExecutor**
3. `EnableExecution = true` + `EnableRealHttpProbe = true` + action type ≠ `http_probe` → **DryRunActionExecutor**
4. `EnableExecution = true` + `EnableRealHttpProbe = true` + action type = `http_probe` → **HttpProbeActionExecutor** (real outbound HTTPS GET)

**Default-off guarantee:** Both flags default to `false` in code and in `appsettings.Development.json`. A fresh deployment performs zero real I/O.

---

## Section B — Validator Rules (SSRF Protection)

**Class:** `TargetUriValidator` (`internal sealed`, Infrastructure/Validators)

| # | Check | Rejection reason |
|---|-------|-----------------|
| 1 | Null / whitespace URL | `url must not be null or whitespace` |
| 2 | Invalid absolute URI | `url is not a valid absolute URI` |
| 3 | Non-HTTPS scheme | `only HTTPS is allowed; got {scheme}` |
| 4 | `localhost` hostname | `localhost is blocked` |
| 5 | `*.internal` hostname | `*.internal hostnames are blocked` |
| 6 | IP-literal: loopback | `loopback address {ip} is blocked` |
| 7 | IP-literal: IPv6 link-local | `IPv6 link-local address {ip} is blocked` |
| 8 | IP-literal: 10.0.0.0/8 | `private IP {ip} (10.0.0.0/8) is blocked` |
| 9 | IP-literal: 172.16.0.0/12 | `private IP {ip} (172.16.0.0/12) is blocked` |
| 10 | IP-literal: 192.168.0.0/16 | `private IP {ip} (192.168.0.0/16) is blocked` |
| 11 | IP-literal / DNS: 169.254.0.0/16 (IMDS) | `link-local/IMDS address {ip} is blocked` |
| 12 | DNS resolved to any blocked range | `DNS for {host} resolved to blocked IP: {reason}` |
| 13 | DNS resolution failure | `DNS resolution failed for {host}` |

**Unit tests:** 10 test methods in `TargetUriValidatorTests.cs` — all passing.

---

## Section C — Runtime Behavior Matrix

### RoutingActionExecutor routing table

| `EnableRealHttpProbe` | Action type | Downstream executor | Method signature used |
|-----------------------|-------------|--------------------|-----------------------|
| `true` | `http_probe` | `HttpProbeActionExecutor` | `ExecuteAsync(payloadJson, ct)` |
| `true` | `restart_service` | `DryRunActionExecutor` | `ExecuteAsync(actionType, payloadJson, ct)` |
| `true` | `scale_out` | `DryRunActionExecutor` | `ExecuteAsync(actionType, payloadJson, ct)` |
| `false` | `http_probe` | `DryRunActionExecutor` | `ExecuteAsync(actionType, payloadJson, ct)` |
| `false` | `restart_service` | `DryRunActionExecutor` | `ExecuteAsync(actionType, payloadJson, ct)` |
| `false` | any | `DryRunActionExecutor` | `ExecuteAsync(actionType, payloadJson, ct)` |

### HttpProbeActionExecutor execution pipeline

1. Parse payload JSON → extract `url`, `method` (default `"GET"`), optional `timeoutMs`
2. Reject non-GET methods → `method_not_allowed`
3. `TargetUriValidator.Validate(url)` → reject if blocked → `url_blocked`
4. Outbound `HttpClient.GetAsync(url)` with cancellation timeout
5. `ReadCappedBodyAsync` → read up to `_maxResponseBytes` (default 1024)
6. Return `ActionExecutionResult` with JSON: `{mode, statusCode, url, truncated, responseSnippet, durationMs}`

### Rollback behavior

- `HttpProbeActionExecutor.RollbackAsync` → always returns `Success=false`, `reason="rollback is not supported for http_probe"`
- Non-`http_probe` rollback → delegated to `DryRunActionExecutor.RollbackAsync`

---

## Section D — Logging & Audit

### Structured log events (no secrets exposed)

| Component | Level | Template |
|-----------|-------|----------|
| `HttpProbeActionExecutor` | Warning | `[HttpProbe] URL validation failed: {Reason}` |
| `HttpProbeActionExecutor` | Information | `[HttpProbe] GET {Url} (timeout={TimeoutMs}ms)` |
| `HttpProbeActionExecutor` | Information | `[HttpProbe] {Url} → {StatusCode} ({DurationMs}ms, {BodyLen} bytes, truncated={Truncated})` |
| `HttpProbeActionExecutor` | Warning | `[HttpProbe] Timeout after {TimeoutMs}ms for {Url}` |
| `HttpProbeActionExecutor` | Warning | `[HttpProbe] HTTP request failed for {Url}` |
| `RoutingActionExecutor` | Information | `[RoutingExecutor] Routing {ActionType} to HttpProbeActionExecutor` |
| `RoutingActionExecutor` | Information | `[RoutingExecutor] Routing {ActionType} to DryRunActionExecutor` |
| `RoutingActionExecutor` | Information | `[RoutingExecutor] Routing {ActionType} rollback to *` |

**No auth tokens, no request/response bodies in log messages.** URLs are logged for audit trail only.

---

## Section E — Build & Test Conformance

### Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Test results

| Assembly | Passed | Failed | Skipped | Total |
|----------|--------|--------|---------|-------|
| `OpsCopilot.Modules.AgentRuns.Tests.dll` | 53 | 0 | 0 | 53 |
| `OpsCopilot.Modules.SafeActions.Tests.dll` | 109 | 0 | 0 | 109 |
| `OpsCopilot.Integration.Tests.dll` | 24 | 0 | 0 | 24 |
| `OpsCopilot.Mcp.ContractTests.dll` | 8 | 0 | 0 | 8 |
| **Total** | **194** | **0** | **0** | **194** |

### Slice 9 new tests (34 added)

| Test class | Count | Coverage area |
|------------|-------|---------------|
| `TargetUriValidatorTests` | 10 | SSRF validation rules |
| `HttpProbeActionExecutorTests` | 13 | Real executor: parse, method-reject, URL-block, success, timeout, HTTP error, rollback |
| `RoutingActionExecutorTests` | 8 | Routing logic: flag on/off × action type, rollback routing |
| `SafeActionRoutingEndpointTests` | 3 | HTTP integration: real-probe routed, dry-run fallback, rollback endpoint |

---

## Section F — Configuration

### New config keys (appsettings.Development.json)

```json
"SafeActions": {
  "EnableExecution": false,
  "EnableRealHttpProbe": false,
  "HttpProbeTimeoutMs": 5000,
  "HttpProbeMaxResponseBytes": 1024
}
```

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `EnableExecution` | bool | `false` | Existing (Slice 8). Endpoint guard → 501. |
| `EnableRealHttpProbe` | bool | `false` | **New.** Routes `http_probe` to real executor. |
| `HttpProbeTimeoutMs` | int | `5000` | **New.** Per-request timeout (ms). Per-request `timeoutMs` in payload overrides. |
| `HttpProbeMaxResponseBytes` | int | `1024` | **New.** Max response body bytes read. |

### NuGet packages added

| Package | Version | Project |
|---------|---------|---------|
| `Microsoft.Extensions.Configuration.Binder` | 9.0.2 | SafeActions.Infrastructure |
| `Microsoft.Extensions.Http` | 9.0.2 | SafeActions.Infrastructure |

---

## Section G — Out-of-Scope Confirmation

| Constraint | Status |
|------------|--------|
| No auth headers on outbound requests | Confirmed — `HttpClient.GetAsync` only, no `Authorization` header |
| No retries / Polly / circuit breakers | Confirmed — single attempt, timeout via `CancellationTokenSource` |
| GET only — no POST/PUT/DELETE | Confirmed — method check rejects non-GET |
| HTTPS only | Confirmed — `TargetUriValidator` rejects non-HTTPS |
| No schema changes to `ActionExecutionResult` | Confirmed — same `(bool Success, string ResponseJson, long DurationMs)` record |
| No McpHost / WorkerHost changes | Confirmed — zero changes to those projects |
| No endpoint route changes | Confirmed — same `/safe-actions/execute` and `/safe-actions/rollback` routes |
| No Azure SDKs | Confirmed — only standard .NET HTTP + configuration packages |
| `StubActionExecutor.cs` untouched | Confirmed |
| `DryRunActionExecutor.cs` untouched | Confirmed |
| Response body capped (not unbounded) | Confirmed — `ReadCappedBodyAsync` reads max `_maxResponseBytes` |

---

## Section H — Architecture Conformance

### Clean Architecture compliance

| Rule | Status |
|------|--------|
| Domain → no outward deps | Green |
| Application → Domain only | Green |
| Infrastructure → Application + Domain | Green |
| Presentation → Application only | Green |
| ApiHost → Presentation only | Green |
| No cross-module references | Green |

### New file inventory

| File | Layer | Visibility |
|------|-------|------------|
| `TargetUriValidator.cs` | Infrastructure/Validators | `internal sealed` |
| `HttpProbeActionExecutor.cs` | Infrastructure/Executors | `internal sealed` |
| `RoutingActionExecutor.cs` | Infrastructure/Executors | `internal sealed` |

### DI registration chain

```
TargetUriValidator           → Singleton
DryRunActionExecutor         → Singleton (unchanged from Slice 8)
HttpProbeActionExecutor      → Transient (via AddHttpClient<T>)
RoutingActionExecutor        → Singleton, registered as IActionExecutor
```

`RoutingActionExecutor` is the sole `IActionExecutor` in the container. Orchestrator and endpoints resolve only the `IActionExecutor` port — never a concrete executor.

### InternalsVisibleTo

SafeActions.Infrastructure exposes internals to:
- `OpsCopilot.Integration.Tests`
- `OpsCopilot.Modules.SafeActions.Tests`

---

**Slice 9 complete.** 194 tests, 0 failures, 0 warnings. Default-off safety preserved.
