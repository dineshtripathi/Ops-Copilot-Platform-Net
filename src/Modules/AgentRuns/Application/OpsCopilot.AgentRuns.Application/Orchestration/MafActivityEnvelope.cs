using System.Text.Json;
using Microsoft.Agents.Core.Models;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Slice 148: Typed envelope parsed from IActivity.ChannelData JSON.
/// Carries tenantId, workspaceId, and alertFingerprint into the orchestrator.
/// tenantId is required; workspaceId defaults to "default"; alertFingerprint
/// falls back to the caller-supplied fallback (typically the MAF activity Id).
/// </summary>
internal sealed record MafActivityEnvelope(
    string TenantId,
    string WorkspaceId,
    string AlertFingerprint)
{
    /// <summary>
    /// Tries to parse the MAF activity ChannelData into a typed envelope.
    /// Returns <see langword="false"/> when ChannelData is null, is not a valid
    /// JSON object, or does not contain a non-empty <c>tenantId</c> field.
    /// </summary>
    /// <param name="activity">The incoming MAF activity.</param>
    /// <param name="fallbackFingerprint">
    ///   Used as <see cref="AlertFingerprint"/> when the ChannelData does not
    ///   supply an <c>alertFingerprint</c> field.
    /// </param>
    /// <param name="envelope">Set to a valid envelope on success; <see langword="null"/> on failure.</param>
    internal static bool TryParse(
        IActivity activity,
        string fallbackFingerprint,
        out MafActivityEnvelope? envelope)
    {
        envelope = null;

        if (activity.ChannelData is null)
            return false;

        try
        {
            var json = JsonSerializer.Serialize(activity.ChannelData);
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("tenantId", out var tid) ||
                tid.ValueKind != JsonValueKind.String         ||
                string.IsNullOrWhiteSpace(tid.GetString()))
                return false;

            var workspaceId = root.TryGetProperty("workspaceId", out var wsId) &&
                              wsId.ValueKind == JsonValueKind.String           &&
                              !string.IsNullOrWhiteSpace(wsId.GetString())
                              ? wsId.GetString()!
                              : "default";

            var fingerprint = root.TryGetProperty("alertFingerprint", out var fp) &&
                              fp.ValueKind == JsonValueKind.String             &&
                              !string.IsNullOrWhiteSpace(fp.GetString())
                              ? fp.GetString()!
                              : fallbackFingerprint;

            envelope = new MafActivityEnvelope(
                TenantId:         tid.GetString()!,
                WorkspaceId:      workspaceId,
                AlertFingerprint: fingerprint);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
