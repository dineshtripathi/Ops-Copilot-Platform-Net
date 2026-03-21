namespace OpsCopilot.Reporting.Domain.Models;

public sealed record DiagnosisHypothesis(
    string Cause,
    int Score,
    double Confidence,
    string Evidence);
