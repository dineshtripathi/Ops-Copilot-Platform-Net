namespace OpsCopilot.Reporting.Domain.Models;

public sealed record BlastRadiusSummary(
    int ImpactedSubscriptions,
    int ImpactedResourceGroups,
    int ImpactedResources,
    int ImpactedApplications);
