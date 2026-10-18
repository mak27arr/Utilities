namespace Utilities.Mobile.Services;

using Utilities.Core.Domain.Models;

public interface IAppNavigation
{
    Task OpenAddressAsync(Guid addressId);
    Task OpenNewAddressAsync();
    Task OpenMeterAsync(Guid meterId);
    Task OpenMeterForManualReadingAsync(Guid meterId);
    Task OpenMeterSettingsAsync(Guid meterId);
    Task OpenMeterIdentifiersAsync(Guid meterId);
    Task OpenMeterReplacementAsync(Guid meterId);
    Task OpenMeterHistoryAsync(Guid meterId, bool tariffs);
    Task OpenServiceTypesAsync();
    Task OpenNewServiceTypeAsync();
    Task OpenEditServiceTypeAsync(Guid serviceTypeId);
    Task OpenRecentlyDeletedAsync();
    Task OpenSettingsAsync();
    Task OpenReminderEditorAsync(ReminderOwnerType ownerType, Guid ownerId, string ownerName, ReminderKind kind);
    Task OpenPhotoCropAsync(Guid meterId);
    Task GoBackAsync();
    void ShowHome();
}
