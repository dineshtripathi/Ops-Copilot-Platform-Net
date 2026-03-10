using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackSafeActionDefinitionValidator"/> covering
/// all error codes and the Operator Card preview generation.
/// </summary>
public class PackSafeActionDefinitionValidatorTests
{
    // ── Helpers ─────────────────────────────────────────────────

    private static string MinimalValid(string id = "restart-vm") =>
        $$"""{"displayName":"Restart VM","actionType":"restart","id":"{{id}}"}""";

    private static string WithProperty(string baseJson, string key, string value)
    {
        // Insert before the closing brace
        return baseJson.TrimEnd('}') + $",\"{key}\":{value}" + "}";
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Valid — minimal required fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MinimalValid_ReturnsIsValidTrue()
    {
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", MinimalValid());

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. definition_null
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_NullJson_ReturnsDefinitionNull()
    {
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", null);

        Assert.False(result.IsValid);
        Assert.Equal("definition_null", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. parse_error
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MalformedJson_ReturnsParseError()
    {
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", "{bad json");

        Assert.False(result.IsValid);
        Assert.Equal("parse_error", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. not_object
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ArrayRoot_ReturnsNotObject()
    {
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", "[]");

        Assert.False(result.IsValid);
        Assert.Equal("not_object", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. missing_display_name
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingDisplayName_ReturnsMissingDisplayName()
    {
        var json = """{"actionType":"restart"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("missing_display_name", result.ErrorCode);
    }

    [Fact]
    public void Validate_EmptyDisplayName_ReturnsMissingDisplayName()
    {
        var json = """{"displayName":"","actionType":"restart"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("missing_display_name", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. missing_action_type
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingActionType_ReturnsMissingActionType()
    {
        var json = """{"displayName":"Restart VM"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("missing_action_type", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. invalid_id_format
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_NonKebabCaseId_ReturnsInvalidIdFormat()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","id":"RestartVM"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_id_format", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. id_mismatch
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_IdMismatch_ReturnsIdMismatch()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","id":"stop-vm"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("id_mismatch", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. title_too_long
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_TitleTooLong_ReturnsTitleTooLong()
    {
        var longTitle = new string('A', 121);
        var json = $$"""{"displayName":"Restart VM","actionType":"restart","title":"{{longTitle}}"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("title_too_long", result.ErrorCode);
    }

    [Fact]
    public void Validate_EmptyTitle_ReturnsTitleTooLong()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","title":""}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("title_too_long", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. invalid_requires_mode
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InvalidRequiresMode_ReturnsInvalidRequiresMode()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","requiresMode":"Z"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_requires_mode", result.ErrorCode);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void Validate_ValidRequiresMode_ReturnsIsValidTrue(string mode)
    {
        var json = $$"""{"displayName":"Restart VM","actionType":"restart","requiresMode":"{{mode}}"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. invalid_supports_rollback
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InvalidSupportsRollback_ReturnsInvalidSupportsRollback()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","supportsRollback":"yes"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_supports_rollback", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. invalid_parameters
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ParametersNotObject_ReturnsInvalidParameters()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","parameters":"bad"}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. invalid_defaults (key not in parameters)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_DefaultKeyNotInParameters_ReturnsInvalidDefaults()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","parameters":{"timeout":{}},"defaults":{"unknown":"val"}}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_defaults", result.ErrorCode);
    }

    [Fact]
    public void Validate_DefaultKeyMatchesParameter_ReturnsIsValidTrue()
    {
        var json = """{"displayName":"Restart VM","actionType":"restart","parameters":{"timeout":{}},"defaults":{"timeout":"30"}}""";
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Full valid definition with all optional fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_FullValidDefinition_ReturnsIsValidTrue()
    {
        var json = """
        {
            "id": "restart-vm",
            "displayName": "Restart VM",
            "actionType": "restart",
            "title": "Restart a virtual machine gracefully",
            "requiresMode": "B",
            "supportsRollback": true,
            "parameters": { "vmName": {}, "force": {} },
            "defaults": { "force": "false" }
        }
        """;
        var result = PackSafeActionDefinitionValidator.Validate("restart-vm", json);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════
    // Operator Card Preview Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateOperatorPreview_ValidWithParams_ContainsExpectedLines()
    {
        var validation = new PackSafeActionDefinitionValidator.DefinitionValidationResult(true, null, null);
        var parametersJson = """{"vmName":{},"force":{}}""";

        var preview = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "Restart VM", "restart", parametersJson, validation);

        Assert.Contains("== Operator Card ==", preview);
        Assert.Contains("Action : Restart VM", preview);
        Assert.Contains("Type   : restart", preview);
        Assert.Contains("Params : force, vmName", preview); // sorted
        Assert.Contains("Valid  : yes", preview);
    }

    [Fact]
    public void GenerateOperatorPreview_NoParams_ShowsNone()
    {
        var validation = new PackSafeActionDefinitionValidator.DefinitionValidationResult(true, null, null);

        var preview = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "Stop VM", "stop", null, validation);

        Assert.Contains("Params : (none)", preview);
        Assert.Contains("Valid  : yes", preview);
    }

    [Fact]
    public void GenerateOperatorPreview_InvalidResult_ShowsErrorInValid()
    {
        var validation = new PackSafeActionDefinitionValidator.DefinitionValidationResult(
            false, "missing_display_name", "Required property 'displayName' is missing or empty.");

        var preview = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "restart-vm", "unknown", null, validation);

        Assert.Contains("Valid  : no", preview);
        Assert.Contains("missing_display_name", preview);
    }

    [Fact]
    public void GenerateOperatorPreview_EmptyParams_ShowsNone()
    {
        var validation = new PackSafeActionDefinitionValidator.DefinitionValidationResult(true, null, null);

        var preview = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "Restart VM", "restart", "{}", validation);

        Assert.Contains("Params : (none)", preview);
    }

    [Fact]
    public void GenerateOperatorPreview_IsDeterministic()
    {
        var validation = new PackSafeActionDefinitionValidator.DefinitionValidationResult(true, null, null);
        var parametersJson = """{"zeta":{},"alpha":{},"beta":{}}""";

        var preview1 = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "Test", "test-action", parametersJson, validation);
        var preview2 = PackSafeActionDefinitionValidator.GenerateOperatorPreview(
            "Test", "test-action", parametersJson, validation);

        Assert.Equal(preview1, preview2);
        Assert.Contains("Params : alpha, beta, zeta", preview1);
    }
}
