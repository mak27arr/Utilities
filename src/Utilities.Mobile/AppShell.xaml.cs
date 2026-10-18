namespace Utilities.Mobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        Items[0].Items[0].Items[0].Content = mainPage;
    }
}

