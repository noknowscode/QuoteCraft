using Uno.Toolkit.UI;

namespace QuoteCraft.Presentation;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        this.Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeToggleVisuals();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var isDark = SystemThemeHelper.IsRootInDarkMode(this.XamlRoot);
        SystemThemeHelper.SetRootTheme(this.XamlRoot, !isDark);
        UpdateThemeToggleVisuals();
    }

    private void UpdateThemeToggleVisuals()
    {
        var isDark = SystemThemeHelper.IsRootInDarkMode(this.XamlRoot);
        ThemeIcon.Glyph = isDark ? "\uE706" : "\uE708";
        ThemeLabel.Text = isDark ? "Light Mode" : "Dark Mode";
    }
}
