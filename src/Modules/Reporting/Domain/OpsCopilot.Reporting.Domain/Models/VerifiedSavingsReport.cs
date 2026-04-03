namespace OpsCopilot.Reporting.Domain.Models;

public sealed record VerifiedSavingsReport(
    decimal   TotalEstimatedSavings,
    int       QualifiedRunCount,
    DateOnly? FromDate,
    DateOnly? ToDate);
