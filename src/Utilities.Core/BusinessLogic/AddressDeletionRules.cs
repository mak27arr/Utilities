namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.Models;

public static class AddressDeletionRules
{
    /// <summary>Soft-deletes the address and every address-owned record supplied by the application layer.</summary>
    public static void SoftDelete(Address address, IEnumerable<SoftDeletableEntity> relatedRecords, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(relatedRecords);
        SoftDeleteTreeRules.SoftDelete(address, relatedRecords, at);
    }

    /// <summary>Restores the address and its previously soft-deleted address-owned records.</summary>
    public static void Restore(Address address, IEnumerable<SoftDeletableEntity> relatedRecords, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(relatedRecords);
        SoftDeleteTreeRules.Restore(address, relatedRecords, at);
    }
}
