namespace Utilities.Mobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        Title = AppText.Get("Utilities");
        if (Items.FirstOrDefault()?.Items.FirstOrDefault()?.Items.FirstOrDefault() is { } home)
            home.Title = AppText.Get("Home");
        Items[0].Items[0].Items[0].Content = mainPage;
    }
}

