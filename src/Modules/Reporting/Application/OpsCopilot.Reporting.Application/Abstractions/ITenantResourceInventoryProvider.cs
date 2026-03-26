using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Enumerates Azure resource groups, App Insights components, and Log Analytics workspaces
/// for a given tenant via the McpHost Resource Graph tools.
/// </summary>
public interface ITenantResourceInventoryProvider
{
    Task<TenantResourceInventory?> GetInventoryAsync(string tenantId, CancellationToken ct);
}
