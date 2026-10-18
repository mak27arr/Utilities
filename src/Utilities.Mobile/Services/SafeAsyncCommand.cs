namespace Utilities.Mobile.Services;

using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Utilities.Core.BusinessLogic;

public abstract class SafeAsyncCommandBase(Func<object?, CancellationToken, Task> execute, IUserDialogs dialogs,
    ILogger logger, string operationName, string errorTitle) : ICommand
{
    private readonly AsyncOperationGate _gate = new();
    private CancellationTokenSource? _activeCancellation;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_gate.IsRunning;
    public void Cancel() => Volatile.Read(ref _activeCancellation)?.Cancel();

    public async void Execute(object? parameter)
    {
        using var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _activeCancellation, cancellation, null) is not null) return;
        try
        {
            var accepted = await _gate.TryRunAsync(async token =>
            {
                RaiseCanExecuteChanged();
                try { await execute(parameter, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    try { await dialogs.ReportErrorAsync(errorTitle, exception); }
                    catch (Exception dialogException) { logger.LogError(dialogException, "Could not report failed UI operation {OperationName}.", operationName); }
                }
            }, cancellation.Token);
            if (!accepted) return;
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeCancellation, null, cancellation);
            RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged() => MainThread.BeginInvokeOnMainThread(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
}

public sealed class SafeAsyncCommand(Func<CancellationToken, Task> execute, IUserDialogs dialogs, ILogger logger,
    string operationName, string errorTitle)
    : SafeAsyncCommandBase((_, token) => execute(token), dialogs, logger, operationName, errorTitle);

public sealed class SafeAsyncCommand<T>(Func<T?, CancellationToken, Task> execute, IUserDialogs dialogs,
    ILogger logger, string operationName, string errorTitle)
    : SafeAsyncCommandBase((parameter, token) => execute(parameter is T value ? value : default, token),
        dialogs, logger, operationName, errorTitle);
