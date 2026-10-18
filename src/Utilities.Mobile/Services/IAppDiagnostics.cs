namespace Utilities.Mobile.Services;

public interface IAppDiagnostics
{
    Task ExportAsync(CancellationToken cancellationToken = default);
}
