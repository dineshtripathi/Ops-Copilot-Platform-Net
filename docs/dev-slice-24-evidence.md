# Slice 24 — AlertIngestion Hardening + Provider Normalization (STRICT)

## Evidence Document

| Item | Detail |
| --- | --- |
| **Slice** | 24 |
| **Title** | AlertIngestion Hardening + Provider Normalization |
| **Branch** | main |
| **Baseline commit** | `baf4a6c` (Slice 23) |
| **Baseline tests** | 435 |
| **Final tests** | 466 (435 + 31 new) |
| **Status** | All acceptance criteria met |

---

## Acceptance Criteria

| AC | Description | Status | Evidence |
| --- | --- | --- | --- |
| AC-1 | `NormalizedAlert` sealed record with required fields (Provider, AlertExternalId, Title, Severity, FiredAtUtc, ResourceId, SourceType, RawPayload) and optional Description / Dimensions | ✅ | `NormalizedAlert.cs` — 47 lines, sealed record, all required + optional properties |
| AC-2 | `IAlertNormalizer` abstraction with `ProviderKey`, `CanHandle(string)`, `Normalize(string, JsonElement)` | ✅ | `IAlertNormalizer.cs` — 25 lines, interface in Abstractions folder |
| AC-3 | `AzureMonitorAlertNormalizer` parses `data.essentials`, maps Sev0→Critical … Sev4→Informational | ✅ | `AzureMonitorAlertNormalizer.cs` — 76 lines; Tests: AzureMonitor happy path + severity mapping (5 InlineData) |
| AC-4 | `DatadogAlertNormalizer` parses webhook payload, maps p1/critical→Critical … p4/low→Informational, tags→Dimensions | ✅ | `DatadogAlertNormalizer.cs` — 94 lines; Tests: Datadog happy path + priority mapping (8 InlineData) + tags→dimensions |
| AC-5 | `GenericAlertNormalizer` with flexible field lookup and candidate field names | ✅ | `GenericAlertNormalizer.cs` — 62 lines; Tests: Generic happy path |
| AC-6 | `AlertNormalizerRouter` dictionary lookup (OrdinalIgnoreCase), `IsSupported(string)`, `Normalize` throws for unknown | ✅ | `AlertNormalizerRouter.cs` — 44 lines; Tests: Router supported/unsupported/throws |
| AC-7 | Deterministic SHA-256 fingerprint from `provider|title|resourceId|severity|sourceType` → upper-case hex 64 chars | ✅ | `NormalizedAlertFingerprintService.cs` — 29 lines; Tests: Fingerprint deterministic + different-inputs |
| AC-8 | `AlertValidationService` with frozen error codes `unsupported_provider` / `invalid_alert_payload` | ✅ | `AlertValidationService.cs` — 36 lines; Tests: Validation valid/unsupported/null-empty-whitespace |
| AC-9 | `IngestAlertCommandHandler` validates tenant → payload → provider, Parse → Normalize → Fingerprint → CreateRunAsync | ✅ | `IngestAlertCommandHandler.cs` — 61 lines; Tests: Endpoint integration tests |
| AC-10 | `POST /ingest/alert` returns 200 on success, 400 with `ReasonCode` + `Message` on validation failure | ✅ | `AlertIngestionEndpoints.cs` — 57 lines; Tests: Endpoint happy path 200, missing tenant 400, unsupported provider 400, empty payload 400 |
| AC-11 | Error codes are `missing_tenant`, `invalid_alert_payload`, `unsupported_provider` — frozen, never changed | ✅ | `AlertValidationService.cs` frozen codes; `AlertIngestionEndpoints.cs` missing_tenant check |
| AC-12 | Module wired via `AddAlertIngestionModule` + `MapAlertIngestionEndpoints` | ✅ | `AlertIngestionApplicationExtensions.cs` registers normalizers + router + handler; Endpoints mapped in Presentation |
| AC-13 | ≥14 new tests | ✅ | 18 test methods expanding to 31 test cases via Theory/InlineData in `AlertIngestionTests.cs` |
| AC-14 | All 466 tests pass (0 failures) | ✅ | `dotnet test` output: 31+69+14+320+24+8 = 466 |

---

## New / Modified Files

### Domain (1 new file)
- `src/Modules/AlertIngestion/Domain/OpsCopilot.AlertIngestion.Domain/Models/NormalizedAlert.cs`

### Application (7 new files + 1 modified)
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Abstractions/IAlertNormalizer.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Normalizers/AzureMonitorAlertNormalizer.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Normalizers/DatadogAlertNormalizer.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Normalizers/GenericAlertNormalizer.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Services/AlertNormalizerRouter.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Services/NormalizedAlertFingerprintService.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Services/AlertValidationService.cs`
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Extensions/AlertIngestionApplicationExtensions.cs` (modified — registers normalizers + router + handler)

### Presentation (2 modified)
- `src/Modules/AlertIngestion/Presentation/OpsCopilot.AlertIngestion.Presentation/Endpoints/AlertIngestionEndpoints.cs` (modified — full validation chain + normalized response)
- `src/Modules/AlertIngestion/Presentation/OpsCopilot.AlertIngestion.Presentation/Contracts/IngestAlertContracts.cs` (modified — Request/Response/ErrorResponse DTOs)

### Application Commands (2 modified)
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Commands/IngestAlertCommand.cs` (modified — includes TenantId, Provider, RawJson)
- `src/Modules/AlertIngestion/Application/OpsCopilot.AlertIngestion.Application/Commands/IngestAlertCommandHandler.cs` (modified — full normalize + fingerprint pipeline)

### Tests (2 new/modified)
- `tests/Modules/AlertIngestion/OpsCopilot.Modules.AlertIngestion.Tests/OpsCopilot.Modules.AlertIngestion.Tests.csproj` (full test project)
- `tests/Modules/AlertIngestion/OpsCopilot.Modules.AlertIngestion.Tests/AlertIngestionTests.cs` (18 methods / 31 cases)

### .http
- `docs/http/OpsCopilot.Api.http` (Section Z: Z1–Z6)

### Deleted
- 5 × `Class1.cs` placeholders (Domain, Application, Infrastructure, Presentation, Tests)

---

## Test Results

```
Passed!  - Failed: 0, Passed:  31 - AlertIngestion (NEW)
Passed!  - Failed: 0, Passed:  69 - AgentRuns
Passed!  - Failed: 0, Passed:  14 - Reporting
Passed!  - Failed: 0, Passed: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24 - Integration
Passed!  - Failed: 0, Passed:   8 - MCP Contract
─────────────────────────────────────
Total:   466 passed, 0 failed
```
