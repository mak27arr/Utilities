namespace Utilities.Mobile.Services;

public interface IUserDialogs
{
    Task AlertAsync(string title, string message, string accept);
    Task ReportErrorAsync(string title, Exception exception);
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, bool numeric = false);
    Task<string?> ChooseAsync(string title, string cancel, string? destructive, params string[] choices);
}
