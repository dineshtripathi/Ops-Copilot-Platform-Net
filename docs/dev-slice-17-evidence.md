# Dev Slice 17 — SafeActions Auth/Claims Identity Hardening — Evidence

## Slice Summary

**Goal**: Replace developer-convenience `x-actor-id` header-based identity
sourcing with a claims-based `IActorIdentityResolver` for authenticated requests.
Claims-first resolution with configurable dev/anonymous fallbacks gated by config
keys that default to `false` (safe). Endpoints return 401 when no identity is
available and all fallbacks are disabled. STRICT slice — no execution routing
changes, no schema changes, no auth middleware/JWT redesign.

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Result

| Assembly | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|
| OpsCopilot.Modules.AgentRuns.Tests | 53 | 0 | 0 | 300 ms |
| OpsCopilot.Modules.SafeActions.Tests | 271 | 0 | 0 | 1 s |
| OpsCopilot.Integration.Tests | 24 | 0 | 0 | 8 s |
| OpsCopilot.Mcp.ContractTests | 8 | 0 | 0 | 25 s |
| **Total** | **356** | **0** | **0** | — |

Baseline (Slice 16): 330 tests. New Slice 17 tests: **26**. Total: **356**.

## New Tests — ClaimsActorIdentityResolverTests (17 unit tests)

| # | Test Name | AC |
|---|---|---|
| 1 | Resolve_ReturnsNameIdentifier_WhenPresent | AC-1, AC-2 |
| 2 | Resolve_ReturnsOid_WhenNoNameIdentifier | AC-1, AC-2 |
| 3 | Resolve_ReturnsSub_WhenNoNameIdentifierOrOid | AC-1, AC-2 |
| 4 | Resolve_ReturnsName_WhenOnlyNameClaim | AC-1, AC-2 |
| 5 | Resolve_ReturnsPreferredUsername_WhenOnlyPreferredUsername | AC-1, AC-2 |
| 6 | Resolve_SkipsWhitespaceClaims | AC-1 |
| 7 | Resolve_NameIdentifierBeatsOidAndSub | AC-3 |
| 8 | Resolve_ReturnsHeader_WhenNoClaimsAndHeaderFallbackEnabled | AC-5, AC-6 |
| 9 | Resolve_IgnoresHeader_WhenHeaderFallbackDisabled | AC-4 |
| 10 | Resolve_IgnoresEmptyHeader_WhenHeaderFallbackEnabled | AC-5 |
| 11 | Resolve_ClaimsWinOverHeader_WhenBothPresent | AC-3 |
| 12 | Resolve_ReturnsUnknown_WhenAnonymousFallbackEnabled | AC-7 |
| 13 | Resolve_ReturnsNull_WhenAllFallbacksDisabled | AC-8 |
| 14 | Resolve_HeaderBeatsAnonymous_WhenBothEnabled | AC-3, AC-5 |
| 15 | Resolve_FallsToAnonymous_WhenHeaderEmptyAndBothEnabled | AC-7 |
| 16 | Resolve_IgnoresClaimsFromUnauthenticatedPrincipal | AC-1 |
| 17 | Resolve_FallbacksDefaultToFalse_WhenConfigMissing | AC-4 |

## New Tests — SafeActionIdentityEndpointTests (9 integration tests)

| # | Test Name | AC |
|---|---|---|
| 1 | Approve_Returns401_WhenResolverReturnsNull | AC-8 |
| 2 | Reject_Returns401_WhenResolverReturnsNull | AC-8 |
| 3 | RollbackApprove_Returns401_WhenResolverReturnsNull | AC-8 |
| 4 | Approve_Returns401_WithRealResolver_WhenFallbacksDisabled | AC-4, AC-8 |
| 5 | Approve_Returns200_WithHeaderFallback | AC-6, AC-10 |
| 6 | Reject_Returns200_WithHeaderFallback | AC-6, AC-10 |
| 7 | Approve_PassesActorIdFromResolver_ToOrchestrator | AC-10 |
| 8 | Reject_PassesActorIdFromResolver_ToOrchestrator | AC-10 |
| 9 | Approve_Returns200_WithAnonymousFallback | AC-7, AC-10 |

## Files Changed

### New Files
| File | Purpose |
|---|---|
| `src/Modules/SafeActions/Presentation/.../Identity/ActorIdentityResult.cs` | Sealed record: `ActorId`, `Source`, `IsAuthenticated` |
| `src/Modules/SafeActions/Presentation/.../Identity/IActorIdentityResolver.cs` | Abstraction: `ActorIdentityResult? Resolve(HttpContext)` |
| `src/Modules/SafeActions/Presentation/.../Identity/ClaimsActorIdentityResolver.cs` | 7-step precedence chain resolver |
| `tests/Modules/SafeActions/.../ClaimsActorIdentityResolverTests.cs` | 17 unit tests for resolver |
| `tests/Modules/SafeActions/.../SafeActionIdentityEndpointTests.cs` | 9 HTTP-level integration tests |

### Modified Files
| File | Change |
|---|---|
| `src/Modules/SafeActions/Presentation/.../SafeActionsPresentationExtensions.cs` | `AddSingleton<IActorIdentityResolver, ClaimsActorIdentityResolver>()` DI registration |
| `src/Modules/SafeActions/Presentation/.../Endpoints/SafeActionEndpoints.cs` | Removed `GetActorId` header helper; approve, reject, rollback/approve endpoints resolve identity via `IActorIdentityResolver`; return 401 when null |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `SafeActions:AllowActorHeaderFallback=true`, `SafeActions:AllowAnonymousActorFallback=true` |
| `docs/http/OpsCopilot.Api.http` | Section S added (3 requests: S1 approve with header, S2 approve anonymous, S3 reject with header) |

## Acceptance Criteria Coverage

| AC | Description | Status |
|---|---|---|
| AC-1 | Resolver reads claims (nameidentifier, oid, sub, name, preferred_username) | ✅ |
| AC-2 | `ActorIdentityResult` record with `ActorId`, `Source`, `IsAuthenticated` | ✅ |
| AC-3 | Claims-first precedence: nameidentifier > oid > sub > name > preferred_username | ✅ |
| AC-4 | Config keys default to `false` (safe by default) | ✅ |
| AC-5 | `x-actor-id` header fallback gated by `SafeActions:AllowActorHeaderFallback` | ✅ |
| AC-6 | Dev appsettings enables header fallback for local dev | ✅ |
| AC-7 | Anonymous fallback returns `"unknown"` gated by `AllowAnonymousActorFallback` | ✅ |
| AC-8 | Returns 401 when no identity available and all fallbacks disabled | ✅ |
| AC-9 | `GetActorId` header helper removed from endpoints | ✅ |
| AC-10 | Approve, reject, rollback/approve endpoints use resolver | ✅ |
| AC-11 | No execution routing changes (STRICT) | ✅ |
| AC-12 | No schema changes (STRICT) | ✅ |
| AC-13 | .http Section S with identity-hardening requests | ✅ |
| AC-14 | Evidence file created | ✅ |
| AC-15 | All prior tests still pass (330 baseline) + 26 new = 356 total, 0 failures | ✅ |
