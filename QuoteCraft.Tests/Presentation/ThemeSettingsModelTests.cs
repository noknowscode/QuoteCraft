using NSubstitute;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Uno.Extensions.Toolkit;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Settings' Appearance row: the theme choice, kept in step with the rail's menu.</summary>
public class ThemeSettingsModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<ThemeSettingsModel> Create(ModelHarness h, IThemeService theme)
    {
        var model = new ThemeSettingsModel(theme, h.ThemeChoice);
        _ = await model.ThemeIndex;
        return model;
    }

    [Fact]
    public async Task Choosing_a_theme_applies_it_and_tells_the_shell()
    {
        using var h = new ModelHarness();
        var theme = Substitute.For<IThemeService>();
        var raised = 0;
        h.ThemeChoice.Changed += () => raised++;
        var model = await Create(h, theme);

        await model.ThemeIndex.UpdateAsync(_ => 2, None);

        await Settled(() => Task.FromResult(raised), n => n > 0);
        await theme.Received().SetThemeAsync(AppTheme.Dark);
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task The_theme_row_follows_a_choice_made_in_the_rail_without_setting_it_again()
    {
        using var h = new ModelHarness();
        var theme = Substitute.For<IThemeService>();
        theme.Theme.Returns(AppTheme.Light);
        var model = await Create(h, theme);
        Assert.Equal(1, await model.ThemeIndex);

        // The rail's menu sets Dark through its own theme service and says so.
        theme.Theme.Returns(AppTheme.Dark);
        h.ThemeChoice.Raise();

        Assert.Equal(2, await Settled(async () => await model.ThemeIndex, i => i == 2));
        await Task.Delay(100);
        await theme.DidNotReceive().SetThemeAsync(Arg.Any<AppTheme>());
    }
}
