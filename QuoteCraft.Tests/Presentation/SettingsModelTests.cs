using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuoteCraft.Data.Remote;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Reactive;
using Uno.Extensions.Toolkit;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Settings: the business profile and quote defaults, the theme, and the logo.</summary>
public class SettingsModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static SettingsModel Create(ModelHarness h, IThemeService? theme = null, SupabaseConfig? supabase = null) => new(
        h.Navigator, h.Db.Profiles, h.Catalog, h.FeatureGate, h.Subscription, Substitute.For<IAuthService>(),
        supabase ?? new SupabaseConfig(null, null), theme ?? Substitute.For<IThemeService>(), h.ThemeChoice, h.Dispatcher,
        NullLogger<SettingsModel>.Instance);

    private static async Task<SettingsModel> LoadedAsync(ModelHarness h, IThemeService? theme = null)
    {
        var model = Create(h, theme);
        // The fields' first values, then the profile (see QuoteEditorModelTests.Create).
        _ = (await model.BusinessName, await model.Phone, await model.Email, await model.Address, await model.Website,
            await model.BusinessNumber, await model.DefaultTaxRate, await model.DefaultMarkup, await model.CurrencyCode,
            await model.QuoteValidDays, await model.QuoteNumberPrefix, await model.CustomFooter, await model.LogoPath,
            await model.ValidationError, await model.SavedMessage, await model.ThemeIndex);
        await model.LoadProfile(None);
        return model;
    }

    [Fact]
    public async Task The_fields_start_from_the_stored_profile()
    {
        using var h = new ModelHarness();
        var profile = await h.Db.Profiles.GetAsync();
        profile.BusinessName = "Northside Plumbing";
        profile.DefaultTaxRate = 8.25m;
        profile.QuoteValidDays = 30;
        await h.Db.Profiles.SaveAsync(profile);

        var model = await LoadedAsync(h);

        Assert.Equal(("Northside Plumbing", "8.25", "30"), (await model.BusinessName, await model.DefaultTaxRate, await model.QuoteValidDays));
    }

    [Fact]
    public async Task Save_writes_the_profile_and_says_so()
    {
        using var h = new ModelHarness();
        var model = await LoadedAsync(h);

        await model.BusinessName.UpdateAsync(_ => "Northside Plumbing", None);
        await model.DefaultTaxRate.UpdateAsync(_ => "9.975", None);
        await model.QuoteValidDays.UpdateAsync(_ => "2", None);
        await model.CustomFooter.UpdateAsync(_ => "Thank you!", None);
        await model.Save(None);

        var saved = await h.Db.Profiles.GetAsync();
        Assert.Equal(("Northside Plumbing", 9.975m, 2, "Thank you!"), (saved.BusinessName, saved.DefaultTaxRate, saved.QuoteValidDays, saved.CustomFooter));
        Assert.Equal("Settings saved.", await model.SavedMessage);
        Assert.Equal(string.Empty, await model.ValidationError);
    }

    [Fact]
    public async Task Save_refuses_a_rate_that_does_not_read()
    {
        using var h = new ModelHarness();
        var before = (await h.Db.Profiles.GetAsync()).DefaultTaxRate;
        var model = await LoadedAsync(h);

        await model.DefaultTaxRate.UpdateAsync(_ => "abc", None);
        await model.Save(None);

        Assert.NotEqual(string.Empty, await model.ValidationError);
        Assert.Equal(string.Empty, await model.SavedMessage);
        Assert.Equal(before, (await h.Db.Profiles.GetAsync()).DefaultTaxRate);
    }

    [Fact]
    public async Task Choosing_a_theme_applies_it_and_tells_the_shell()
    {
        using var h = new ModelHarness();
        var theme = Substitute.For<IThemeService>();
        var raised = 0;
        h.ThemeChoice.Changed += () => raised++;
        var model = await LoadedAsync(h, theme);

        await model.ThemeIndex.UpdateAsync(_ => 2, None);

        await Settled(() => Task.FromResult(raised), n => n > 0);
        await theme.Received().SetThemeAsync(AppTheme.Dark);
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Remove_logo_deletes_the_copy_and_save_keeps_no_logo()
    {
        using var h = new ModelHarness();
        var logo = Path.Combine(h.Db.Folder, "logo.png");
        await File.WriteAllBytesAsync(logo, [1, 2, 3]);
        var profile = await h.Db.Profiles.GetAsync();
        profile.LogoPath = logo;
        await h.Db.Profiles.SaveAsync(profile);
        var model = await LoadedAsync(h);
        Assert.Equal("logo.png", await model.LogoFileName);

        await model.RemoveLogo(None);
        await model.Save(None);

        Assert.False(File.Exists(logo));
        Assert.Equal(string.Empty, await model.LogoPath);
        Assert.True(string.IsNullOrEmpty((await h.Db.Profiles.GetAsync()).LogoPath));
    }

    [Fact]
    public async Task Without_a_supabase_project_settings_say_the_data_stays_here()
    {
        using var h = new ModelHarness();

        var model = await LoadedAsync(h);

        Assert.Equal(SupabaseConfig.NotConfiguredMessage, await model.LocalModeNote);
    }
}
