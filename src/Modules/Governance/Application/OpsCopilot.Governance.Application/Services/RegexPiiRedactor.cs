using System.Text.RegularExpressions;
using OpsCopilot.BuildingBlocks.Contracts.Privacy;

namespace OpsCopilot.Governance.Application.Services;

/// <summary>
/// Redacts common PII patterns from strings using compiled regular expressions.
/// Applied in specificity order to avoid partial overlaps between patterns.
/// </summary>
public sealed class RegexPiiRedactor : IPiiRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        // Email addresses — most distinct (requires @), process first
        (new Regex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "[EMAIL]"),

        // US Social Security Numbers — NNN-NN-NNNN or NNN NN NNNN
        (new Regex(@"\b\d{3}[-\s]\d{2}[-\s]\d{4}\b",
            RegexOptions.Compiled), "[SSN]"),

        // Credit card numbers — 13–16 digits optionally separated by spaces/dashes
        (new Regex(@"\b(?:\d{4}[-\s]?){3}\d{1,4}\b",
            RegexOptions.Compiled), "[CC]"),

        // US phone numbers — (NNN) NNN-NNNN / NNN-NNN-NNNN / NNN NNN NNNN
        (new Regex(@"\b(?:\+1[\s\-]?)?\(?\d{3}\)?[\s\-]\d{3}[\s\-]\d{4}\b",
            RegexOptions.Compiled), "[PHONE]"),

        // IPv4 addresses — N.N.N.N
        (new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.Compiled), "[IP]"),
    ];

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;
        foreach (var (pattern, replacement) in Rules)
            result = pattern.Replace(result, replacement);

        return result;
    }
}
