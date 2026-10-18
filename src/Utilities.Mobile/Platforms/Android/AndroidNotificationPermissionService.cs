namespace Utilities.Mobile.Infrastructure.Reminders;

using Microsoft.Maui.ApplicationModel;
using Utilities.Mobile.Services;

public sealed class AndroidNotificationPermissionService : INotificationPermissionService
{
    public async Task<bool> IsGrantedAsync() => await Permissions.CheckStatusAsync<NotificationPermission>() == PermissionStatus.Granted;
    public async Task<bool> RequestAsync() => await Permissions.RequestAsync<NotificationPermission>() == PermissionStatus.Granted;
}
