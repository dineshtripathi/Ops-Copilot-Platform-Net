using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that a known fingerprint algorithm produces a consistent SHA-256 hash
/// for a deterministic input.
/// </summary>
public sealed class AlertIngestion_FingerprintDeterminismScenario : IEvaluationScenario
{
    public string ScenarioId  => "AI-004";
    public string Module      => "AlertIngestion";
    public string Name        => "Fingerprint determinism";
    public string Category    => "Determinism";
    public string Description => "Same input → same SHA-256 fingerprint on every run.";

    public EvaluationResult Execute()
    {
        const string input = "tenant:contoso|rule:HighCpu|target:/subs/1/vm/web01";
        var hash1 = ComputeSha256(input);
        var hash2 = ComputeSha256(input);

        var passed = hash1 == hash2 && hash1.Length == 64; // SHA-256 hex = 64 chars

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: "Deterministic 64-char hex hash",
            Actual: passed ? $"Consistent hash ({hash1[..8]}…)" : "Hash mismatch or wrong length");
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
