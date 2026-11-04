using NSubstitute;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Uno.Extensions.Toolkit;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Settings: the business profile and quote defaults, the preview, the currency and the logo.</summary>
public class SettingsModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static SettingsModel Create(ModelHarness h) => new(
        h.Navigator, h.Db.Profiles, h.Catalog, h.Currency, h.Logos, h.Dispatcher,
        AccountSettingsModelTests.Create(h), new ThemeSettingsModel(Substitute.For<IThemeService>(), h.ThemeChoice));

    private static async Task<SettingsModel> LoadedAsync(ModelHarness h)
    {
        var model = Create(h);
        // The fields' first values, then the profile (see QuoteEditorModelTests.Create).
        _ = (await model.BusinessName, await model.Phone, await model.Email, await model.Address, await model.Website,
            await model.BusinessNumber, await model.DefaultTaxRate, await model.DefaultMarkup, await model.CurrencyCode,
            await model.QuoteValidDays, await model.QuoteNumberPrefix, await model.CustomFooter, await model.LogoPath,
            await model.ValidationError, await model.SavedMessage, await model.LoadError);
        await model.Reload(None);
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
    public async Task Save_sets_the_currency_every_amount_shows_in()
    {
        using var h = new ModelHarness();
        var model = await LoadedAsync(h);

        await model.CurrencyCode.UpdateAsync(_ => "CAD", None);
        await model.Save(None);

        Assert.Equal(("CAD", "CAD"), ((await h.Db.Profiles.GetAsync()).CurrencyCode, h.Currency.Code));
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
    public async Task The_preview_follows_the_defaults_as_they_are_typed()
    {
        using var h = new ModelHarness();
        var model = await LoadedAsync(h);

        await model.DefaultTaxRate.UpdateAsync(_ => "8.25", None);
        await model.DefaultMarkup.UpdateAsync(_ => "10", None);
        await model.QuoteNumberPrefix.UpdateAsync(_ => "NP-", None);

        var preview = await Settled(async () => await model.Preview, p => p?.Total == "$548.94");
        Assert.Equal(("$507.10", "Tax (8.25 %)", "$41.84", "$548.94"), (preview!.Subtotal, preview.TaxLabel, preview.Tax, preview.Total));
        Assert.StartsWith("NP-", preview.QuoteNumber);
    }
}
