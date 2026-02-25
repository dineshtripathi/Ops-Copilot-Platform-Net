using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Redacts known sensitive keys from a JSON payload string.
/// Keys matched (case-insensitive): token, password, secret, key, connectionString.
/// Matched values are replaced with "[REDACTED]".
/// Non-JSON or null inputs are returned as-is.
/// </summary>
public static class PayloadRedactor
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "password",
        "secret",
        "key",
        "connectionString",
    };

    /// <summary>
    /// Returns a copy of <paramref name="json"/> with sensitive key values replaced
    /// by "[REDACTED]".  Returns <c>null</c> when <paramref name="json"/> is null,
    /// and returns the original string when it is not valid JSON.
    /// </summary>
    public static string? Redact(string? json)
    {
        if (json is null) return null;

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return json;

            RedactNode(node);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                if (SensitiveKeys.Contains(prop.Key))
                {
                    obj[prop.Key] = "[REDACTED]";
                }
                else if (prop.Value is not null)
                {
                    RedactNode(prop.Value);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var element in arr)
            {
                if (element is not null)
                    RedactNode(element);
            }
        }
    }
}
