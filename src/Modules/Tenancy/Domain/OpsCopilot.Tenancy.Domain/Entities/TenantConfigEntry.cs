namespace OpsCopilot.Tenancy.Domain.Entities;

public sealed class TenantConfigEntry
{
    public Guid TenantConfigEntryId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }

    private TenantConfigEntry() { }

    public static TenantConfigEntry Create(
        Guid tenantId, string key, string value, string? updatedBy = null) => new()
    {
        TenantConfigEntryId = Guid.NewGuid(),
        TenantId = tenantId,
        Key = key,
        Value = value,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedBy = updatedBy
    };

    public void Update(string value, string? updatedBy = null)
    {
        Value = value;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }
}
