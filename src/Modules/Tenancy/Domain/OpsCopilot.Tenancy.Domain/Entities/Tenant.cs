namespace OpsCopilot.Tenancy.Domain.Entities;

public sealed class Tenant
{
    public Guid TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }

    private Tenant() { }

    public static Tenant Create(string displayName, string? updatedBy = null) => new()
    {
        TenantId = Guid.NewGuid(),
        DisplayName = displayName,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedBy = updatedBy
    };

    public void Deactivate(string? updatedBy = null)
    {
        IsActive = false;
        UpdatedBy = updatedBy;
    }

    public void Activate(string? updatedBy = null)
    {
        IsActive = true;
        UpdatedBy = updatedBy;
    }
}
