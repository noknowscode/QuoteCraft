namespace QuoteCraft.Helpers;

/// <summary>
/// Builds styled ContentDialogs with gradient banner headers
/// matching the "Unlock QuoteCraft Pro" banner style.
/// </summary>
public static class DialogHelper
{
    // Pages pass their own XamlRoot, which is set once they are in the visual tree, as they are whenever a click runs.
    private const string NoXamlRootMessage = "A dialog needs a page that is in the visual tree (XamlRoot is null).";

    /// <summary>
    /// Builds a gradient banner Border (slate blue gradient, amber icon, white text).
    /// </summary>
    public static Border BuildGradientBanner(string iconGlyph, string title, string subtitle)
    {
        var headerTitle = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["TitleMediumBold"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBarForegroundFixedBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var headerSubtitle = new TextBlock
        {
            Text = subtitle,
            Style = (Style)Application.Current.Resources["BodySmall"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["UpgradeBannerSubtitleFixedBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var textStack = new StackPanel { Spacing = (double)Application.Current.Resources["Space2"] };
        textStack.Children.Add(headerTitle);
        textStack.Children.Add(headerSubtitle);

        var icon = new FontIcon
        {
            Glyph = iconGlyph,
            FontSize = (double)Application.Current.Resources["IconSize2xl"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBarAccentInvariantBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = (Thickness)Application.Current.Resources["GapEnd12"],
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(icon);
        grid.Children.Add(textStack);

        return new Border
        {
            Background = BuildGradientBrush(),
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusLg"],
            Padding = (Thickness)Application.Current.Resources["BannerInset"],
            Child = grid,
        };
    }

    /// <summary>
    /// Builds a simple ContentDialog with only a gradient banner (no form fields).
    /// </summary>
    public static ContentDialog BuildBannerDialog(
        XamlRoot? xamlRoot,
        string iconGlyph,
        string title,
        string subtitle,
        string? closeButtonText = null,
        string? primaryButtonText = null)
    {
        var banner = BuildGradientBanner(iconGlyph, title, subtitle);
        var root = xamlRoot ?? throw new InvalidOperationException(NoXamlRootMessage);
        var panel = new StackPanel { MinWidth = MinContentWidth(root) };
        panel.Children.Add(banner);

        return Styled(new ContentDialog
        {
            Content = panel,
            CloseButtonText = closeButtonText ?? "OK",
            PrimaryButtonText = primaryButtonText,
            XamlRoot = root,
        });
    }

    /// <summary>
    /// Builds a ContentDialog with a gradient banner header and custom body content.
    /// </summary>
    public static ContentDialog BuildBannerDialogWithContent(
        XamlRoot? xamlRoot,
        string iconGlyph,
        string title,
        string subtitle,
        UIElement bodyContent,
        string? primaryButtonText = null,
        string? closeButtonText = null)
    {
        var banner = BuildGradientBanner(iconGlyph, title, subtitle);
        banner.Margin = (Thickness)Application.Current.Resources["GapBottom4"];

        var root = xamlRoot ?? throw new InvalidOperationException(NoXamlRootMessage);
        var panel = new StackPanel { Spacing = (double)Application.Current.Resources["Space16"], MinWidth = MinContentWidth(root) };
        panel.Children.Add(banner);
        panel.Children.Add(bodyContent);

        // Long forms (Send Quote) scroll inside the dialog on short windows.
        return Styled(new ContentDialog
        {
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = primaryButtonText ?? "Save",
            CloseButtonText = closeButtonText ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        });
    }

    /// <summary>The banner gradient (App.xaml BannerGradientFixedBrush: brand colours, the same in both themes).</summary>
    public static Microsoft.UI.Xaml.Media.Brush BuildGradientBrush() =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BannerGradientFixedBrush"];

    /// <summary>
    /// The Material dialog style, so code-built dialogs match the rest of the app (buttons, surface, corner radius).
    /// </summary>
    public static ContentDialog Styled(ContentDialog dialog)
    {
        if (Application.Current.Resources.TryGetValue("MaterialContentDialogStyle", out var style) && style is Style material)
        {
            dialog.Style = material;
        }
        return dialog;
    }

    /// <summary>DialogMinWidth on tablets and desktops; on a phone, whatever the window leaves after the dialog's chrome.</summary>
    public static double MinContentWidth(XamlRoot root)
    {
        var preferred = (double)Application.Current.Resources["DialogMinWidth"];
        var chrome = (double)Application.Current.Resources["DialogChromeWidth"];
        return Math.Max(0, Math.Min(preferred, root.Size.Width - chrome));
    }

    /// <summary>An error line for a dialog form, hidden until <see cref="ShowValidation"/> fills it.</summary>
    public static TextBlock BuildValidationText(FrameworkElement owner)
    {
        var text = new TextBlock
        {
            Style = (Style)Application.Current.Resources["BodySmall"],
            Foreground = ThemeResources.GetBrush(owner, "ErrorBrush"),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        return text;
    }

    public static void ShowValidation(TextBlock validation, string message)
    {
        validation.Text = message;
        validation.Visibility = Visibility.Visible;
    }
}
