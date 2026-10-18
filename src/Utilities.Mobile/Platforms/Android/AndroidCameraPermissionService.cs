namespace Utilities.Mobile.Infrastructure.Media;

using Microsoft.Maui.ApplicationModel;
using Utilities.Mobile.Services;

public sealed class AndroidCameraPermissionService : ICameraPermissionService
{
    public async Task<bool> IsGrantedAsync() => await Permissions.CheckStatusAsync<Permissions.Camera>() == PermissionStatus.Granted;
    public bool ShouldShowRationale() => Permissions.ShouldShowRationale<Permissions.Camera>();
    public async Task<bool> RequestAsync() => await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted;
}
