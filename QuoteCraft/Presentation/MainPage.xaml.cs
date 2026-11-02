using Microsoft.UI.Xaml.Automation;
using Uno.Extensions.Toolkit;
using Uno.Toolkit.UI;
using QuoteCraft.Helpers;
using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public sealed partial class MainPage : Page
{
    private ConnectivityHelper? _connectivity;
    private INotificationService? _notificationService;
    private bool _isSideRailCollapsed;

    public MainPage()
    {
        this.InitializeComponent();
        this.Loaded += MainPage_Loaded;
        this.Unloaded += MainPage_Unloaded;
        this.ActualThemeChanged += (_, _) => UpdateThemeToggleVisuals();

        foreach (var item in BottomTabs.Items.Concat(SideTabs.Items).OfType<TabBarItem>())
        {
            item.Click += TabItem_Click;
        }
    }

    /// <summary>
    /// A tab that is already selected does not navigate again, so after the quote editor (which is not a tab) the
    /// Quotes tab did nothing. Tapping the selected tab now opens its page.
    /// </summary>
    private async void TabItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is TabBarItem { IsSelected: true } item
            && Uno.Extensions.Navigation.UI.Region.GetName(item) is { Length: > 0 } route
            && this.Navigator() is { } navigator)
        {
            await navigator.NavigateRouteAsync(item, route);
        }
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeToggleVisuals();

        // Wire up offline banner
        try
        {
            _connectivity = App.Services.GetService<ConnectivityHelper>();
            if (_connectivity is not null)
            {
                _connectivity.ConnectivityChanged += OnConnectivityChanged;
                _connectivity.StartMonitoring();
            }
        }
        catch { }

        // Cache notification service
        try
        {
            _notificationService = App.Services.GetService<INotificationService>();
        }
        catch { }
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_connectivity is not null)
            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(bool isOnline)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            OfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    // --- Theme: System (the default), Light or Dark, saved by the theme service and restored at launch ---

    private IThemeService? ThemeService => this.FindServiceProvider()?.GetService<IThemeService>();

    private void ThemeMenu_Opening(object? sender, object e)
    {
        var theme = ThemeService?.Theme ?? AppTheme.System;
        ThemeSystemItem.IsChecked = theme == AppTheme.System;
        ThemeLightItem.IsChecked = theme == AppTheme.Light;
        ThemeDarkItem.IsChecked = theme == AppTheme.Dark;
    }

    private async void ThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || ThemeService is not { } service)
        {
            return;
        }

        await service.SetThemeAsync(Enum.Parse<AppTheme>(tag));
        UpdateThemeToggleVisuals();
    }

    private void UpdateThemeToggleVisuals()
    {
        var theme = ThemeService?.Theme ?? AppTheme.System;
        // The label names the choice, so the button reads the same to a screen reader and on screen.
        ThemeLabel.Text = theme switch
        {
            AppTheme.Light => "Light theme",
            AppTheme.Dark => "Dark theme",
            _ => "System theme",
        };
        ThemeIcon.Glyph = ActualTheme == ElementTheme.Dark ? "\uE708" : "\uE706";
        AutomationProperties.SetName(ThemeButton, $"Theme: {ThemeLabel.Text}");
    }

    // --- Notifications overlay ---

    private async void Notifications_Click(object sender, RoutedEventArgs e)
    {
        NotificationsOverlay.Visibility = Visibility.Visible;
        await LoadNotificationsAsync();
    }

    private void NotifBack_Click(object sender, RoutedEventArgs e)
    {
        NotificationsOverlay.Visibility = Visibility.Collapsed;
    }

    private async void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        if (_notificationService is not null)
        {
            await _notificationService.MarkAllAsReadAsync();
            await LoadNotificationsAsync();
        }
    }

    private async void NotifRetry_Click(object sender, RoutedEventArgs e) => await LoadNotificationsAsync();

    private async Task LoadNotificationsAsync()
    {
        if (_notificationService is null) return;

        List<NotificationEntity> notifications;
        try
        {
            notifications = await _notificationService.GetAllAsync();
        }
        catch (Exception)
        {
            NotificationsList.Visibility = Visibility.Collapsed;
            NotifEmptyState.Visibility = Visibility.Collapsed;
            NotifErrorState.Visibility = Visibility.Visible;
            return;
        }

        NotifErrorState.Visibility = Visibility.Collapsed;
        if (notifications.Count == 0)
        {
            NotificationsList.Visibility = Visibility.Collapsed;
            NotifEmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            NotificationsList.ItemsSource = notifications;
            NotificationsList.Visibility = Visibility.Visible;
            NotifEmptyState.Visibility = Visibility.Collapsed;
        }
    }

    // --- Side rail collapse ---

    private void CollapseToggle_Click(object sender, RoutedEventArgs e)
    {
        _isSideRailCollapsed = !_isSideRailCollapsed;

        if (_isSideRailCollapsed)
        {
            // Collapse: icons only
            SideRail.Width = (double)Application.Current.Resources["RailCollapsedWidth"];
            BrandLabel.Visibility = Visibility.Collapsed;
            NotifLabel.Visibility = Visibility.Collapsed;
            ThemeLabel.Visibility = Visibility.Collapsed;
            CollapseLabel.Visibility = Visibility.Collapsed;
            CollapseIcon.Glyph = "\uE76C"; // ChevronRight
            AutomationProperties.SetName(CollapseToggle, "Expand sidebar");
            ToolTipService.SetToolTip(CollapseToggle, "Expand sidebar");

            // Hide labels on tab bar items via visual tree
            HideTabBarItemLabels(true);
        }
        else
        {
            // Expand: icons + labels
            SideRail.Width = (double)Application.Current.Resources["RailWidth"];
            BrandLabel.Visibility = Visibility.Visible;
            NotifLabel.Visibility = Visibility.Visible;
            ThemeLabel.Visibility = Visibility.Visible;
            CollapseLabel.Visibility = Visibility.Visible;
            CollapseIcon.Glyph = "\uE76B"; // ChevronLeft
            AutomationProperties.SetName(CollapseToggle, "Collapse sidebar");
            ToolTipService.SetToolTip(CollapseToggle, "Collapse sidebar");

            HideTabBarItemLabels(false);
        }
    }

    private void HideTabBarItemLabels(bool hide)
    {
        // Find all LabelText elements in the side rail's TabBar items
        var tabBar = FindDescendant<TabBar>(SideRail);
        if (tabBar is null) return;

        foreach (var item in tabBar.Items.OfType<TabBarItem>())
        {
            var label = FindDescendantByName(item, "LabelText") as TextBlock;
            if (label is not null)
            {
                label.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : class
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            var result = FindDescendantByName(child, name);
            if (result is not null) return result;
        }
        return null;
    }
}
