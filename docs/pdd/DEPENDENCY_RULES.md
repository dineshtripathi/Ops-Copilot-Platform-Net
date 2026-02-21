# Ops Copilot — Dependency Rules (Locked)



## 1. Clean Architecture Principle

**All dependencies point inward.** Inner layers (Domain → Application → Infrastructure → Presentation/Hosts) must never depend on outer layers. Violations require an explicit architecture change request.

## 2. Module Layer Contracts

### Domain (innermost)
- May reference: `OpsCopilot.BuildingBlocks.Domain`, `OpsCopilot.BuildingBlocks.Contracts`.
- Must not reference: any Application, Infrastructure, Presentation, or Host project.
- Purpose: entities, value objects, domain events, invariants.

### Application
- May reference: its own `*.Domain`, `OpsCopilot.BuildingBlocks.Contracts`, optional `OpsCopilot.BuildingBlocks.Domain`.
- Must not reference: Infrastructure, Presentation, Hosts, or another module’s Application/Infrastructure/Presentation.
- Purpose: use cases, policies, orchestration, ports to be implemented by Infrastructure.

### Infrastructure
- May reference: its module’s `*.Application` and `*.Domain`, plus all `OpsCopilot.BuildingBlocks.*` projects.
- Must not be referenced by Domain or Application.
- Purpose: persistence adapters, messaging, Azure SDK adapters, vector/RAG infrastructure.

### Presentation
- May reference: its module’s `*.Application`, `OpsCopilot.BuildingBlocks.Contracts`, optional `OpsCopilot.BuildingBlocks.Domain`.
- Must not reference: any Infrastructure project.
- Purpose: HTTP endpoints, protocol adapters (AG-UI, gRPC, etc.), streaming surfaces. No business logic; no data-store access.

## 3. Host Composition Rules

### ApiHost
- References only module `*.Presentation` projects plus shared `OpsCopilot.BuildingBlocks.Contracts`.
- Must not reference Application or Infrastructure directly. There are **no fallbacks**; all traffic flows Presentation → Application → Domain.

### WorkerHost
- May reference module `*.Application` and `*.Infrastructure` to compose DI containers and schedulers.
- All runtime invocations must go through Application use-cases. Infrastructure references exist solely to register adapters/providers.

### McpHost
- May reference `OpsCopilot.Connectors.Abstractions`, `OpsCopilot.Connectors.Application`, and relevant module `*.Application` projects (e.g., SafeActions.Application) as well as tool wrapper processes.
- Must not reference module Infrastructure directly. Connector plugs are satisfied through abstractions, not concrete adapters.

## 4. Cross-Module Communication

- Allowed shared surfaces: `OpsCopilot.BuildingBlocks.Contracts` and `OpsCopilot.Connectors.Abstractions`.
- Cross-module calls outside these contracts (e.g., ModuleA.Application → ModuleB.Application or any Infrastructure → different module) are prohibited unless an approved exception is documented.

## 5. Testing Layers

### Unit Tests
- Target: Domain + Application assemblies for the same module.
- Must not reference Infrastructure, Presentation, or Hosts.

### Integration Tests
- Target: Application + Infrastructure (and specific Hosts if necessary to wire DI or transport).
- Purpose: validate adapter wiring, persistence, tool integration.

### MCP Contract Tests
- Target: MCP tool wrappers, `OpsCopilot.Connectors.Abstractions`, mocks/test doubles, and the `OpsCopilot.McpHost` entry point as needed.
- Must not reference module Infrastructure directly; behavior is validated via protocol boundaries.
