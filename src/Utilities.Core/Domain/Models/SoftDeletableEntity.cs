namespace Utilities.Core.Domain.Models;

public abstract class SoftDeletableEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedById { get; private set; }
    public string? DeletedByType { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted(DateTimeOffset? at = null, Guid? deletedById = null, string? deletedByType = null)
    {
        if (IsDeleted) return;
        var timestamp = at ?? DateTimeOffset.UtcNow;
        DeletedAt = timestamp;
        DeletedById = deletedById ?? Id;
        DeletedByType = deletedByType ?? GetType().Name;
        UpdatedAt = timestamp;
    }

    public void Restore(DateTimeOffset? at = null)
    {
        DeletedAt = null;
        DeletedById = null;
        DeletedByType = null;
        UpdatedAt = at ?? DateTimeOffset.UtcNow;
    }

    protected void Touch(DateTimeOffset? at = null) => UpdatedAt = at ?? DateTimeOffset.UtcNow;
}
