using Microsoft.Agents.Builder;

namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Slice 147: MAF entry-point contract. Marker interface extending IAgent (MAF)
/// that identifies the triage agent handler in the DI container.
/// PDD §13: MAF is the orchestration foundation.
/// </summary>
public interface ITriageAgentHandler : IAgent { }
