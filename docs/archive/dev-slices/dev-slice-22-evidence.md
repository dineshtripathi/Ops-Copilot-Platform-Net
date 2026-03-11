# Slice 22 — AgentRuns Redis Session Store (Durable Session Hardening)

**Status**: COMPLETE  
**Baseline commit**: `a18db9f` (slice 21)  
**Baseline tests**: 405 passing  
**Post-slice tests**: 421 passing (405 + 16 new)  

---

## Acceptance Criteria Checklist

| AC | Description | Status |
|----|-------------|--------|
| AC-1 | `RedisSessionStore` implements `ISessionStore` (same 4-method contract) | ✅ |
| AC-2 | Redis key format: `opscopilot:agentruns:sessions:{tenantId}:{sessionId}` | ✅ |
| AC-3 | Lookup key pattern: `opscopilot:agentruns:sessions:lookup:{sessionId}` for Guid-only lookup | ✅ |
| AC-4 | Shadow key pattern: `opscopilot:agentruns:sessions:shadow:{sessionId}` (no TTL) for `GetIncludingExpiredAsync` | ✅ |
| AC-5 | `CreateAsync` pipelines all Redis operations in a single `IBatch` | ✅ |
| AC-6 | `TouchAsync` extends expiry on primary + lookup keys (sliding TTL) | ✅ |
| AC-7 | `TouchAsync` is no-op when session not found (no exception) | ✅ |
| AC-8 | `GetAsync` cleans stale lookup keys when primary hash is expired | ✅ |
| AC-9 | Config-driven provider selection: `InMemory` (default) vs `Redis` (explicit) | ✅ |
| AC-10 | Missing Redis `ConnectionString` when provider = Redis → throws at startup | ✅ |
| AC-11 | `InMemorySessionStore` unchanged — stays as dev/test fallback | ✅ |
| AC-12 | No new routes, no schema changes, no API behavior changes | ✅ |
| AC-13 | Tenant isolation preserved — sessions scoped by `tenantId` in key prefix | ✅ |
| AC-14 | ≥10 new tests written and passing | ✅ (13 methods, 15 test cases) |
| AC-15 | All existing tests still pass (no regressions) | ✅ |
| AC-16 | .http documentation updated with Section X (`tenant-verify-22`) | ✅ |
| AC-17 | Evidence document created | ✅ (this file) |

---

## Design Decisions

### Shadow Key Pattern
Redis automatically deletes keys when TTL expires. The `ISessionStore` contract requires `GetIncludingExpiredAsync` to return session data even after expiry. Solved by maintaining a parallel "shadow" hash per session with **no TTL**. This shadow key is written during `CreateAsync` and updated during `TouchAsync` (to keep `expiresAtUtc` current), but never expires on its own.

### Lookup Key Pattern
`GetAsync(Guid sessionId)` doesn't receive a `tenantId`, but the primary key is tenant-scoped. Solved with a lookup key (`opscopilot:agentruns:sessions:lookup:{sessionId}`) that stores the full primary key as a string value with the same TTL as the primary key. On `GetAsync`, first read lookup → then read the primary hash.

### Config-Driven Provider Selection
`AgentRuns:SessionStore:Provider` in configuration drives which `ISessionStore` implementation is registered:
- `"InMemory"` (default when missing or unrecognized) → `InMemorySessionStore` (existing)
- `"Redis"` (case-insensitive) → `RedisSessionStore` with `ConnectionMultiplexer`

### Batch Pipelining
`CreateAsync` and `TouchAsync` use `IDatabase.CreateBatch()` to pipeline multiple Redis operations in a single round-trip, minimizing latency.

---

## Files Created

| File | Description |
|------|-------------|
| `src/Modules/AgentRuns/Infrastructure/OpsCopilot.AgentRuns.Infrastructure/Sessions/RedisSessionStore.cs` | Redis-backed `ISessionStore` (216 lines): 3 key types (primary, lookup, shadow), batch pipelining, sliding TTL |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/RedisSessionStoreTests.cs` | 13 test methods (15 cases): constructor guards, CRUD operations, batch verification, tenant isolation |

## Files Modified

| File | Change Summary |
|------|---------------|
| `src/Modules/AgentRuns/Infrastructure/OpsCopilot.AgentRuns.Infrastructure/AgentRunsInfrastructureExtensions.cs` | Config-driven session store provider selection; Redis or InMemory based on `AgentRuns:SessionStore:Provider` |
| `src/Modules/AgentRuns/Infrastructure/OpsCopilot.AgentRuns.Infrastructure/OpsCopilot.AgentRuns.Infrastructure.csproj` | Added `StackExchange.Redis 2.8.16` NuGet package reference |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `AgentRuns:SessionStore:Provider = "InMemory"` config section |
| `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/OpsCopilot.Modules.AgentRuns.Tests.csproj` | Added project reference to Infrastructure project |
| `docs/http/OpsCopilot.Api.http` | Added Section X (session durability smoke tests) with tenant `tenant-verify-22` |

---

## New Tests (13 methods / 15 cases)

| # | Test Name | Verifies |
|---|-----------|----------|
| 1 | `Constructor_ThrowsArgumentNullException_WhenRedisIsNull` | Null guard on `IConnectionMultiplexer` |
| 2 | `Constructor_ThrowsArgumentNullException_WhenTimeProviderIsNull` | Null guard on `TimeProvider` |
| 3 | `Constructor_ThrowsArgumentNullException_WhenLoggerIsNull` | Null guard on `ILogger` |
| 4 | `GetAsync_ReturnsNull_WhenLookupKeyDoesNotExist` | Lookup miss → null |
| 5 | `GetAsync_ReturnsNull_WhenHashExpiredBetweenLookupAndFetch` | Race: lookup exists but primary expired → null + stale lookup cleanup |
| 6 | `GetAsync_ReturnsSessionInfo_WhenSessionExists` | Happy path: lookup → primary hash → `SessionInfo` |
| 7 | `GetIncludingExpiredAsync_ReturnsNull_WhenSessionWasNeverCreated` | Shadow key miss → null |
| 8 | `GetIncludingExpiredAsync_ReturnsSessionInfo_FromShadowKey` | Shadow key hit → `SessionInfo` (even if primary expired) |
| 9 | `CreateAsync_ReturnsSessionInfo_WithIsNewTrue` | Returns `SessionInfo` with `IsNew=true` |
| 10 | `CreateAsync_PipelinesAllRedisOperationsViaBatch` | Batch: HashSet(primary) + KeyExpire + StringSet(lookup) + HashSet(shadow) |
| 11 | `CreateAsync_UsesCorrectKeyFormat_WithTenantAndSessionId` | Key = `opscopilot:agentruns:sessions:{tenantId}:{sessionId}` |
| 12 | `TouchAsync_ExtendsExpiry_WhenSessionExists` | Batch: KeyExpire(primary) + KeyExpire(lookup) + HashSet updates |
| 13 | `TouchAsync_IsNoOp_WhenSessionNotFound` | Lookup miss → no batch, no exception |
| 14-15 | `CreateAsync_KeyContainsTenantId_ForTenantIsolation` [Theory] | Tenant isolation with `"tenant-alpha"`, `"tenant-beta"`, `"org/team-42"` |

---

## Redis Key Layout

```
opscopilot:agentruns:sessions:{tenantId}:{sessionId}     # Primary hash (with TTL)
  ├── sessionId      : "guid"
  ├── tenantId       : "tenant-abc"
  ├── createdAtUtc   : "2025-07-15T12:00:00.0000000Z"
  └── expiresAtUtc   : "2025-07-15T14:00:00.0000000Z"

opscopilot:agentruns:sessions:lookup:{sessionId}          # String → full primary key (same TTL)

opscopilot:agentruns:sessions:shadow:{sessionId}          # Hash (NO TTL — survives primary expiry)
  ├── sessionId      : "guid"
  ├── tenantId       : "tenant-abc"
  ├── createdAtUtc   : "2025-07-15T12:00:00.0000000Z"
  └── expiresAtUtc   : "2025-07-15T14:00:00.0000000Z"
```

---

## Config Sample (`appsettings.Development.json`)

```json
"AgentRuns": {
  "SessionStore": {
    "Provider": "InMemory"
  }
}
```

Redis configuration (when needed):

```json
"AgentRuns": {
  "SessionStore": {
    "Provider": "Redis",
    "ConnectionString": "localhost:6379"
  }
}
```

---

## NuGet Additions

| Package | Version | Purpose |
|---------|---------|---------|
| `StackExchange.Redis` | 2.8.16 | Redis client for `RedisSessionStore` |

---

## Test Results

```
Passed!  - Failed: 0, Passed:  69, Total:  69 - AgentRuns
Passed!  - Failed: 0, Passed: 320, Total: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24, Total:  24 - Integration
Passed!  - Failed: 0, Passed:   8, Total:   8 - MCP Contract
Total: 421 passing, 0 failed
```
