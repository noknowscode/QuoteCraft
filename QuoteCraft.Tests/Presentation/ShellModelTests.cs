using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuoteCraft.Data.Remote;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive;
using Uno.Extensions.Toolkit;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The shell (rail, bottom tabs, badge, theme menu) and the notifications drawer, with expiry reminders.</summary>
public class ShellModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static MainModel CreateMain(ModelHarness h, IThemeService? theme = null) => new(h.Navigator, theme ?? Substitute.For<IThemeService>(),
        h.ThemeChoice, h.Notifications, new ConnectivityHelper(new HttpClient(), new SupabaseConfig(null, null), NullLogger<ConnectivityHelper>.Instance));

    private static QuoteExpiryService Expiry(ModelHarness h) => new(h.Db.Quotes, h.Status, h.Notifications, NullLogger<QuoteExpiryService>.Instance);

    [Fact]
    public async Task A_reminder_from_the_expiry_check_shows_in_the_badge_and_the_drawer()
    {
        using var h = new ModelHarness();
        await h.AddQuoteAsync("Boiler", QuoteStatus.Sent, validUntil: DateTimeOffset.UtcNow.AddDays(2));
        var main = CreateMain(h);
        Assert.False(await main.HasUnreadNotifications);

        await Expiry(h).CheckExpirationsAsync();

        Assert.True(await Settled(async () => await main.HasUnreadNotifications, unread => unread));
        Assert.Equal("1", await Settled(async () => await main.UnreadBadge ?? string.Empty, b => b == "1"));
        var drawer = new NotificationsModel(h.Notifications);
        var reminder = Assert.Single(await drawer.Notifications);
        Assert.Equal(("Quote Expiring Soon", false), (reminder.Title, reminder.IsRead));
        Assert.Matches("expires in 2 days", reminder.Message);
    }

    [Fact]
    public async Task Mark_all_read_clears_the_badge_and_stays_read_after_a_relaunch()
    {
        using var h = new ModelHarness();
        await h.AddQuoteAsync("Boiler", QuoteStatus.Sent, validUntil: DateTimeOffset.UtcNow.AddDays(1));
        await Expiry(h).CheckExpirationsAsync();
        var main = CreateMain(h);
        Assert.True(await Settled(async () => await main.HasUnreadNotifications, unread => unread));

        await new NotificationsModel(h.Notifications).MarkAllRead(None);
        Assert.False(await Settled(async () => await main.HasUnreadNotifications, unread => !unread));

        // The next launch: a new service over the same database, and the check runs again.
        var relaunched = new NotificationService(new QuoteCraft.Data.AppDatabase(h.Db.Folder), NullLogger<NotificationService>.Instance);
        await new QuoteExpiryService(h.Db.Quotes, h.Status, relaunched, NullLogger<QuoteExpiryService>.Instance).CheckExpirationsAsync();
        Assert.Equal(0, await relaunched.GetUnreadCountAsync());
        Assert.True(Assert.Single(await relaunched.GetAllAsync()).IsRead);
    }

    [Fact]
    public async Task Alerts_and_the_rail_bell_open_the_notifications_drawer()
    {
        using var h = new ModelHarness();

        await CreateMain(h).OpenNotifications(None);

        Assert.Equal([Qualifiers.Dialog + "Notifications"], h.Navigator.Routes);
    }

    [Fact]
    public async Task The_shell_reads_the_business_currency_at_startup()
    {
        using var h = new ModelHarness();
        var profile = await h.Db.Profiles.GetAsync();
        profile.CurrencyCode = "GBP";
        await h.Db.Profiles.SaveAsync(profile);

        _ = new ShellModel(h.Db.Profiles, h.Currency, NullLogger<ShellModel>.Instance);

        Assert.Equal("GBP", await Settled(() => Task.FromResult(h.Currency.Code), code => code == "GBP"));
    }

    [Fact]
    public async Task The_rail_theme_menu_saves_the_choice_and_tells_settings()
    {
        using var h = new ModelHarness();
        var theme = Substitute.For<IThemeService>();
        var raised = 0;
        h.ThemeChoice.Changed += () => raised++;

        await CreateMain(h, theme).SetTheme("Dark", None);

        await theme.Received(1).SetThemeAsync(AppTheme.Dark);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task A_tab_opens_its_page_and_clears_the_tab_frame()
    {
        using var h = new ModelHarness();

        await CreateMain(h).OpenTab("Clients", None);

        Assert.Equal([Qualifiers.Nested + Qualifiers.ClearBackStack + "Clients"], h.Navigator.Routes);
    }
}
