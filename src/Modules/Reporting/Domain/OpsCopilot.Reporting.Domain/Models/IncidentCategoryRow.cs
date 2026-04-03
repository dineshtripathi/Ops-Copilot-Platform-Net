namespace OpsCopilot.Reporting.Domain.Models;

public sealed record IncidentCategoryRow(
    string  Category,
    int     IncidentCount,
    double? AvgResolutionMinutes);
