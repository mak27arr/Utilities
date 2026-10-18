namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.Models;

/// <summary>Centralizes root ownership markers so persistence providers use the same cascade and restore rules.</summary>
public static class SoftDeleteTreeRules
{
    public static void SoftDelete(SoftDeletableEntity root, IEnumerable<SoftDeletableEntity> relatedRecords, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relatedRecords);
        var timestamp = at ?? DateTimeOffset.UtcNow;
        root.MarkDeleted(timestamp, root.Id, root.GetType().Name);
        foreach (var record in relatedRecords.DistinctBy(record => record.Id))
            if (record.Id != root.Id && record.DeletedAt is null)
                record.MarkDeleted(timestamp, root.Id, root.GetType().Name);
    }

    public static void Restore(SoftDeletableEntity root, IEnumerable<SoftDeletableEntity> relatedRecords, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relatedRecords);
        var timestamp = at ?? DateTimeOffset.UtcNow;
        var rootType = root.GetType().Name;
        root.Restore(timestamp);
        foreach (var record in relatedRecords.DistinctBy(record => record.Id))
            if (record.DeletedById == root.Id && record.DeletedByType == rootType) record.Restore(timestamp);
    }
}
