using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Frozen minimal-schema validator for pack safe-action definition JSON.
/// All checks are deterministic and offline — no network calls, no dynamic schema loading.
/// </summary>
internal static partial class PackSafeActionDefinitionValidator
{
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex KebabCasePattern();

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase) { "A", "B", "C" };

    internal readonly record struct DefinitionValidationResult(
        bool IsValid,
        string? ErrorCode,
        string? ErrorMessage);

    private static readonly DefinitionValidationResult ValidResult = new(true, null, null);

    /// <summary>
    /// Validates a safe-action definition JSON against the frozen minimal schema.
    /// Required: displayName (non-empty string), actionType (non-empty string).
    /// Optional-if-present: id (kebab-case, must match manifest), title (1-120 chars),
    /// requiresMode (A/B/C), supportsRollback (bool), parameters (object), defaults (keys ⊆ parameter keys).
    /// </summary>
    internal static DefinitionValidationResult Validate(string manifestActionId, string? definitionJson)
    {
        if (definitionJson is null)
            return new(false, "definition_null", "Definition JSON is null.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(definitionJson);
        }
        catch (JsonException ex)
        {
            return new(false, "parse_error", $"Invalid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return new(false, "not_object", "Definition must be a JSON object.");

            // ── Required: displayName ────────────────────────────────
            if (!root.TryGetProperty("displayName", out var dn)
                || dn.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(dn.GetString()))
            {
                return new(false, "missing_display_name", "Required property 'displayName' is missing or empty.");
            }

            // ── Required: actionType ─────────────────────────────────
            if (!root.TryGetProperty("actionType", out var at)
                || at.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(at.GetString()))
            {
                return new(false, "missing_action_type", "Required property 'actionType' is missing or empty.");
            }

            // ── Optional: id (kebab-case + must match manifest) ──────
            if (root.TryGetProperty("id", out var idProp))
            {
                if (idProp.ValueKind != JsonValueKind.String)
                    return new(false, "invalid_id_format", "Property 'id' must be a string.");

                var idVal = idProp.GetString();
                if (string.IsNullOrWhiteSpace(idVal) || !KebabCasePattern().IsMatch(idVal))
                    return new(false, "invalid_id_format", $"Property 'id' must be kebab-case (got '{idVal}').");

                if (!string.Equals(idVal, manifestActionId, StringComparison.Ordinal))
                    return new(false, "id_mismatch", $"Property 'id' value '{idVal}' does not match manifest action ID '{manifestActionId}'.");
            }

            // ── Optional: title (1-120 chars) ────────────────────────
            if (root.TryGetProperty("title", out var titleProp))
            {
                if (titleProp.ValueKind != JsonValueKind.String)
                    return new(false, "title_too_long", "Property 'title' must be a string.");

                var titleVal = titleProp.GetString() ?? string.Empty;
                if (titleVal.Length == 0 || titleVal.Length > 120)
                    return new(false, "title_too_long", $"Property 'title' must be 1-120 characters (got {titleVal.Length}).");
            }

            // ── Optional: requiresMode (A/B/C) ──────────────────────
            if (root.TryGetProperty("requiresMode", out var modeProp))
            {
                if (modeProp.ValueKind != JsonValueKind.String)
                    return new(false, "invalid_requires_mode", "Property 'requiresMode' must be a string.");

                var modeVal = modeProp.GetString();
                if (modeVal is null || !ValidModes.Contains(modeVal))
                    return new(false, "invalid_requires_mode", $"Property 'requiresMode' must be A, B, or C (got '{modeVal}').");
            }

            // ── Optional: supportsRollback (bool) ────────────────────
            if (root.TryGetProperty("supportsRollback", out var rollbackProp))
            {
                if (rollbackProp.ValueKind != JsonValueKind.True && rollbackProp.ValueKind != JsonValueKind.False)
                    return new(false, "invalid_supports_rollback", "Property 'supportsRollback' must be a boolean.");
            }

            // ── Optional: parameters (object) ────────────────────────
            HashSet<string>? parameterKeys = null;
            if (root.TryGetProperty("parameters", out var paramsProp))
            {
                if (paramsProp.ValueKind != JsonValueKind.Object)
                    return new(false, "invalid_parameters", "Property 'parameters' must be an object.");

                parameterKeys = [];
                foreach (var prop in paramsProp.EnumerateObject())
                    parameterKeys.Add(prop.Name);
            }

            // ── Optional: defaults (keys must be subset of parameter keys) ───
            if (root.TryGetProperty("defaults", out var defaultsProp))
            {
                if (defaultsProp.ValueKind != JsonValueKind.Object)
                    return new(false, "invalid_defaults", "Property 'defaults' must be an object.");

                if (parameterKeys is not null)
                {
                    foreach (var prop in defaultsProp.EnumerateObject())
                    {
                        if (!parameterKeys.Contains(prop.Name))
                            return new(false, "invalid_defaults", $"Default key '{prop.Name}' is not a declared parameter.");
                    }
                }
            }

            return ValidResult;
        }
    }

    /// <summary>
    /// Generates a deterministic human-readable "Operator Card" preview string.
    /// </summary>
    internal static string GenerateOperatorPreview(
        string displayName,
        string actionType,
        string? parametersJson,
        DefinitionValidationResult validation)
    {
        var lines = new List<string>(10)
        {
            "== Operator Card ==",
            $"Action : {displayName}",
            $"Type   : {actionType}"
        };

        if (parametersJson is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(parametersJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var keys = new List<string>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        keys.Add(prop.Name);

                    if (keys.Count > 0)
                    {
                        keys.Sort(StringComparer.Ordinal);
                        lines.Add($"Params : {string.Join(", ", keys)}");
                    }
                    else
                    {
                        lines.Add("Params : (none)");
                    }
                }
            }
            catch (JsonException)
            {
                lines.Add("Params : (parse error)");
            }
        }
        else
        {
            lines.Add("Params : (none)");
        }

        lines.Add(validation.IsValid
            ? "Valid  : yes"
            : $"Valid  : no — {validation.ErrorCode}: {validation.ErrorMessage}");

        return string.Join('\n', lines);
    }
}
