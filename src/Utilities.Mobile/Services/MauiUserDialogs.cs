namespace Utilities.Mobile.Services;

using Microsoft.Extensions.Logging;

public sealed class MauiUserDialogs : IUserDialogs
{
    private readonly ILogger<MauiUserDialogs> _logger;

    public MauiUserDialogs(ILogger<MauiUserDialogs> logger) => _logger = logger;

    public Task AlertAsync(string title, string message, string accept) => CurrentPage.DisplayAlertAsync(title, message, accept);

    public Task ReportErrorAsync(string title, Exception exception)
    {
        _logger.LogError(exception, "UI action failed: {Title}.", title);
        return AlertAsync(title, AppText.Get("UnexpectedError"), AppText.Get("OK"));
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        CurrentPage.DisplayAlertAsync(title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, bool numeric = false) =>
        CurrentPage.DisplayPromptAsync(title, message, accept, cancel, initialValue: initialValue, keyboard: numeric ? Keyboard.Numeric : Keyboard.Default);

    public Task<string?> ChooseAsync(string title, string cancel, string? destructive, params string[] choices) =>
        CurrentPage.DisplayActionSheetAsync(title, cancel, destructive, choices);

    private static Page CurrentPage
    {
        get
        {
            var root = Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("No active MAUI page is available for a dialog.");
            if (root is NavigationPage navigationPage)
                return navigationPage.CurrentPage ?? navigationPage;
            return root.Navigation?.NavigationStack.LastOrDefault() ?? root;
        }
    }
}
