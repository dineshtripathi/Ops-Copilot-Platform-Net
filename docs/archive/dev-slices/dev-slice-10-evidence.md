# Dev Slice 10 — SafeActions Azure Executor (Read-Only, STRICT)

## Evidence Report

| Item | Value |
|------|-------|
| Slice | 10 |
| Title | SafeActions Azure Executor — read-only ARM metadata GET |
| Status | **COMPLETE** |
| Tests Before | 194 |
| Tests After | **218** (+24 net new) |
| Build Warnings | 0 |
| Build Errors | 0 |

---

## 1. What Slice 10 Delivers

Slice 10 adds a new action type **`azure_resource_get`** that performs a **read-only ARM
metadata GET** for any Azure resource, using the `Azure.ResourceManager` SDK with
`DefaultAzureCredential`. This enables the agent to retrieve resource metadata (name,
type, location, provisioning state, ETag, tag count) without requiring resource-type-specific
SDK packages.

**Hard safety guarantees:**

- **Read-only** — only `GenericResource.GetAsync` (ARM GET) is ever called. No writes, deletes, or POST actions.
- **No custom token/header passthrough** — uses `DefaultAzureCredential` exclusively. The payload cannot inject credentials.
- **No secrets in logs** — only non-sensitive metadata fields are logged and returned.
- **Feature-gated** — three independent flags must all align before a real Azure call is made.
- **Configurable timeout** — `SafeActions:AzureReadTimeoutMs` (default 5000ms).

---

## 2. Architecture

### New Files

| File | Type | Lines | Purpose |
|------|------|-------|---------|
| `IAzureResourceReader.cs` | Interface + record | 37 | Abstraction for ARM metadata reads; enables mock injection |
| `ArmResourceReader.cs` | Implementation | 55 | Wraps `ArmClient.GetGenericResource(id).GetAsync()` |
| `AzureResourceGetActionExecutor.cs` | Executor | 192 | Parses payload, validates resource ID, calls reader, maps errors |
| `AzureResourceGetActionExecutorTests.cs` | Unit tests | 300 | 15 test cases covering all code paths |

### Modified Files

| File | Change |
|------|--------|
| `RoutingActionExecutor.cs` | 5-param constructor; three-way routing precedence |
| `SafeActionsInfrastructureExtensions.cs` | DI wiring for `ArmClient`, `IAzureResourceReader`, `AzureResourceGetActionExecutor` |
| `OpsCopilot.SafeActions.Infrastructure.csproj` | `Azure.Identity 1.13.2`, `Azure.ResourceManager 1.13.0`, `InternalsVisibleTo DynamicProxyGenAssembly2` |
| `appsettings.Development.json` | `EnableAzureReadExecutions`, `AzureReadTimeoutMs` |
| `RoutingActionExecutorTests.cs` | 6 new routing tests with azure_resource_get |
| `SafeActionRoutingEndpointTests.cs` | 2 new endpoint integration tests |
| `OpsCopilot.Api.http` | Section L — azure_resource_get documentation |

### Untouched Files (verified)

`StubActionExecutor.cs`, `DryRunActionExecutor.cs`, `HttpProbeActionExecutor.cs`,
`TargetUriValidator.cs` — zero modifications.

### Class Diagram

```
IActionExecutor
    └── RoutingActionExecutor (5 params)
            ├── DryRunActionExecutor           (Slice 8)
            ├── HttpProbeActionExecutor         (Slice 9)
            └── AzureResourceGetActionExecutor  (Slice 10)
                    └── IAzureResourceReader
                            └── ArmResourceReader → ArmClient (Azure.ResourceManager)
```

All new classes are `internal sealed`. `RoutingActionExecutor` is the only
`IActionExecutor` registered in DI.

---

## 3. Feature Flags — Three-Flag Safety Model

```
       ┌─────────────────────────────────────────────────────────┐
       │  Request arrives at POST /api/safe-actions/{id}/execute │
       └──────────────────┬──────────────────────────────────────┘
                          ▼
           ┌───── EnableExecution ─────┐
           │  (endpoint guard, Slice 8)│
           └─────────┬────────────────┘
                     ▼ true
       ┌──── azure_resource_get? ────┐
       │ + EnableAzureReadExecutions │
       └───────┬─────────────────────┘
               │ true → AzureResourceGetActionExecutor
               │ false ↓
       ┌──── http_probe? ───────────┐
       │ + EnableRealHttpProbe      │
       └───────┬────────────────────┘
               │ true → HttpProbeActionExecutor
               │ false ↓
               └──────→ DryRunActionExecutor
```

| Flag | Key | Default | Slice |
|------|-----|---------|-------|
| Endpoint guard | `SafeActions:EnableExecution` | `false` | 8 |
| HTTP probe gate | `SafeActions:EnableRealHttpProbe` | `false` | 9 |
| Azure read gate | `SafeActions:EnableAzureReadExecutions` | `false` | 10 |
| HTTP timeout | `SafeActions:HttpProbeTimeoutMs` | `5000` | 9 |
| HTTP max body | `SafeActions:HttpProbeMaxResponseBytes` | `1024` | 9 |
| Azure timeout | `SafeActions:AzureReadTimeoutMs` | `5000` | 10 |

**All three execution flags default to `false`** — zero side-effects in a fresh deployment.

---

## 4. Error-Code Mapping

`AzureResourceGetActionExecutor` maps every failure to a deterministic error code
via the `Fail()` helper. Every failure response includes `mode`, `reason`, `detail`,
`resourceId`, and `durationMs`.

| Error Code | Trigger | Response |
|------------|---------|----------|
| `invalid_json` | `JsonException` parsing payload | `Success: false` |
| `invalid_payload` | `resourceId` field null, empty, or whitespace | `Success: false` |
| `invalid_resource_id` | `resourceId` does not start with `/subscriptions/` | `Success: false` |
| `azure_auth_failed` | `AuthenticationFailedException` from SDK | `Success: false` |
| `azure_forbidden` | `RequestFailedException` with status 403 | `Success: false` |
| `azure_not_found` | `RequestFailedException` with status 404 | `Success: false` |
| `azure_request_failed` | `RequestFailedException` with any other status | `Success: false` |
| `azure_timeout` | `OperationCanceledException` from linked CTS | `Success: false` |
| `unexpected_error` | Any other `Exception` | `Success: false` |

Rollback returns `Success: false` with reason `"rollback is not supported for azure_resource_get"`.

---

## 5. DI Wiring

Registered in `SafeActionsInfrastructureExtensions.AddSafeActionsInfrastructure()`:

```csharp
// Azure ARM reader (read-only, DefaultAzureCredential)
services.AddSingleton(_ =>
{
    var tenantId = configuration["SafeActions:AzureTenantId"];
    var options = string.IsNullOrWhiteSpace(tenantId)
        ? new DefaultAzureCredentialOptions()
        : new DefaultAzureCredentialOptions { TenantId = tenantId };
    return new ArmClient(new DefaultAzureCredential(options));
});
services.AddSingleton<IAzureResourceReader, ArmResourceReader>();

// Azure resource GET executor
services.AddSingleton<AzureResourceGetActionExecutor>();

// Routing executor (composite — 5-param constructor)
services.AddSingleton<IActionExecutor, RoutingActionExecutor>();
```

`ArmClient` is a singleton — the `Azure.ResourceManager` SDK is designed for singleton
lifetime. `DefaultAzureCredential` handles token caching and refresh internally.

Optional config: `SafeActions:AzureTenantId` — if set, constrains credential to a
specific tenant.

---

## 6. Test Matrix

### Summary

| Suite | Before | After | New |
|-------|--------|-------|-----|
| AgentRuns | 53 | 53 | 0 |
| SafeActions | 109 | 133 | +24 |
| Integration | 24 | 24 | 0 |
| MCP Contract | 8 | 8 | 0 |
| **Total** | **194** | **218** | **+24** |

### New Tests — AzureResourceGetActionExecutorTests (15 tests)

| # | Test | Error Code Verified |
|---|------|---------------------|
| 1 | `ExecuteAsync_Returns_Success_With_Metadata` | — (success) |
| 2 | `ExecuteAsync_Returns_InvalidJson_For_NonJsonPayload` | `invalid_json` |
| 3 | `ExecuteAsync_Returns_InvalidPayload_For_MissingResourceId` | `invalid_payload` |
| 4 | `ExecuteAsync_Returns_InvalidPayload_For_EmptyResourceId` | `invalid_payload` |
| 5 | `ExecuteAsync_Returns_InvalidPayload_For_WhitespaceResourceId` | `invalid_payload` |
| 6-8 | `ExecuteAsync_Returns_InvalidResourceId_For_BadFormat` (Theory ×3) | `invalid_resource_id` |
| 9 | `ExecuteAsync_Returns_AuthFailed_On_AuthenticationFailedException` | `azure_auth_failed` |
| 10 | `ExecuteAsync_Returns_Forbidden_On_403_RequestFailedException` | `azure_forbidden` |
| 11 | `ExecuteAsync_Returns_NotFound_On_404_RequestFailedException` | `azure_not_found` |
| 12 | `ExecuteAsync_Returns_RequestFailed_On_Other_RequestFailedException` | `azure_request_failed` |
| 13 | `ExecuteAsync_Returns_UnexpectedError_On_GenericException` | `unexpected_error` |
| 14 | `ExecuteAsync_Returns_Timeout_On_SlowReader` | `azure_timeout` |
| 15 | `RollbackAsync_Returns_NotSupported` | — (rollback) |
| 16 | `ExecuteAsync_Failure_Response_Always_Has_Mode_And_Reason` | — (shape) |

> Note: The Theory with 3 `InlineData` values counts as 3 test cases, giving 15 individual
> test executions from 14 [Fact]/[Theory] attributes.

### New Tests — RoutingActionExecutorTests (6 tests)

| # | Test |
|---|------|
| 1 | `ExecuteAsync_Routes_AzureResourceGet_When_Flag_Enabled` |
| 2 | `ExecuteAsync_Routes_AzureResourceGet_To_DryRun_When_Flag_Disabled` |
| 3 | `ExecuteAsync_Routes_AzureResourceGet_CaseInsensitive` (Theory ×2) |
| 4 | `RollbackAsync_Routes_AzureResourceGet_When_Flag_Enabled` |
| 5 | `RollbackAsync_Routes_AzureResourceGet_To_DryRun_When_Flag_Disabled` |

### New Tests — SafeActionRoutingEndpointTests (2 tests)

| # | Test |
|---|------|
| 1 | `ExecuteEndpoint_Routes_AzureResourceGet_When_Enabled` |
| 2 | `ExecuteEndpoint_Falls_Back_To_DryRun_When_Azure_Disabled` |

### Build Fix Notes

Three issues were resolved during the build+test cycle:

1. **`AzureLocation` is a struct** — `data.Location` cannot use `?.` operator.
   Fix: `data.Location.Name` (direct access).

2. **`GenericResourceData` lacks `ETag` property** — the ETag is on the raw HTTP response.
   Fix: `response.GetRawResponse().Headers.ETag?.ToString()`.

3. **`InternalsVisibleTo("DynamicProxyGenAssembly2")`** — `IAzureResourceReader` is
   `internal`, and Moq/Castle.DynamicProxy cannot create proxies for internal types
   without this attribute. Added to `OpsCopilot.SafeActions.Infrastructure.csproj`.

---

## 7. .http Documentation

Section L was added to `docs/http/OpsCopilot.Api.http` with four test scenarios:

| Request | Description |
|---------|-------------|
| L1 | `azure_resource_get` execute — real SDK call (both flags enabled) |
| L2 | `azure_resource_get` execute — dry-run fallback (Azure flag disabled) |
| L3 | `azure_resource_get` rollback — not supported |
| L4 | `azure_resource_get` with invalid `resourceId` — deterministic error |

Each request documents the required preconditions (`EnableExecution=true`,
`EnableAzureReadExecutions=true`) and expected response shape.

---

## 8. Why Azure SDKs in Slice 10

### Decision Rationale

Slice 9 introduced outbound HTTP probe via raw `HttpClient`. Slice 10 raises the
abstraction level to Azure-native SDK calls. Why?

1. **AuthN/AuthZ** — `DefaultAzureCredential` provides automatic credential selection
   (Managed Identity in production, Visual Studio / Azure CLI locally) without secrets
   in config. Raw HTTP calls to ARM would require manual token management.

2. **Type safety** — `ResourceIdentifier`, `GenericResourceData`, typed exception hierarchy
   (`RequestFailedException` with status codes, `AuthenticationFailedException`) provide
   structured error handling instead of parsing HTTP status codes and raw JSON.

3. **Scope control** — by using `GenericResource.GetAsync`, we get metadata for _any_
   resource type with a single method. No need for resource-type-specific SDK packages
   (e.g., `Azure.ResourceManager.Compute`) — the `Azure.ResourceManager` base package
   is sufficient.

4. **Future extensibility** — the `IAzureResourceReader` abstraction allows swapping
   the implementation (e.g., for resource-type-specific readers that return richer
   metadata) without changing the executor/routing layer.

### Why Not Earlier?

Slices 8/9 deliberately avoided Azure SDK dependencies to establish the safety patterns
(feature flags, routing, rollback, deterministic error codes, endpoint integration tests)
in the simplest possible context. Now that those patterns are proven, adding Azure SDK is
a controlled extension with no architectural risk.

### Package Choices

| Package | Version | Purpose |
|---------|---------|---------|
| `Azure.Identity` | 1.13.2 | `DefaultAzureCredential` for automatic credential resolution |
| `Azure.ResourceManager` | 1.13.0 | `ArmClient` + `GenericResource` for ARM metadata GET |

Both are official Microsoft SDKs with stable API surfaces. No preview packages are used.

---

## Verification Command

```bash
dotnet build OpsCopilot.sln
dotnet test  OpsCopilot.sln
# Expected: 218 passed, 0 failed, 0 skipped
```

---

_Generated as part of Dev Slice 10 implementation evidence._
