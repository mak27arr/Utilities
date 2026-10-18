namespace Utilities.Mobile.Services;

public interface INotificationPermissionService
{
    Task<bool> IsGrantedAsync();
    Task<bool> RequestAsync();
}

public interface ICameraPermissionService
{
    Task<bool> IsGrantedAsync();
    bool ShouldShowRationale();
    Task<bool> RequestAsync();
}
