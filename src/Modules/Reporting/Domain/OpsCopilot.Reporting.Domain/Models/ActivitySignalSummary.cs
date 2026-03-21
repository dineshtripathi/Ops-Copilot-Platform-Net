namespace OpsCopilot.Reporting.Domain.Models;

public sealed record ActivitySignalSummary(
    int TotalPolicyEvents,
    int PolicyDenials,
    int ScopeDenials,
    int BudgetDenials,
    int DegradedModeEvents);
