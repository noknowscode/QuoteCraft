using NSubstitute;
using QuoteCraft.Data.Remote;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Settings' account and billing: the local-mode note, Sign In, and checkout.</summary>
public class AccountSettingsModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    internal static AccountSettingsModel Create(ModelHarness h, SupabaseConfig? supabase = null, IAuthService? auth = null) =>
        new(h.Navigator, h.FeatureGate, h.Subscription, auth ?? Substitute.For<IAuthService>(), supabase ?? new SupabaseConfig(null, null));

    [Fact]
    public async Task Without_a_supabase_project_settings_say_the_data_stays_here()
    {
        using var h = new ModelHarness();

        var model = Create(h);

        Assert.Equal(SupabaseConfig.NotConfiguredMessage, await model.LocalModeNote);
    }

    [Fact]
    public async Task With_a_supabase_project_sign_in_opens_the_sign_in_page()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        auth.CurrentState.Returns(new AuthState(false, null, null, null, null));
        var model = Create(h, new SupabaseConfig("https://abc.supabase.co", "anon-key"), auth);

        Assert.False(await model.IsSignedIn);
        Assert.Equal(string.Empty, await model.LocalModeNote);
        await model.SignIn(None);

        Assert.Equal(["Auth"], h.Navigator.Routes);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_opens_the_sign_in_page()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        var model = Create(h, new SupabaseConfig("https://abc.supabase.co", "anon-key"), auth);

        await model.SignOut(None);

        await auth.Received(1).SignOutAsync();
        Assert.Equal([Uno.Extensions.Navigation.Qualifiers.ClearBackStack + "Auth"], h.Navigator.Routes);
    }

    [Fact]
    public async Task The_annual_plan_asks_checkout_for_yearly_billing_and_says_when_it_is_not_available()
    {
        using var h = new ModelHarness();
        h.Subscription.GetCheckoutUrlAsync("pro", "annual").Returns((string?)null);
        var model = Create(h, new SupabaseConfig("https://abc.supabase.co", "anon-key"));

        await model.UpgradeToProAnnual(None);

        await h.Subscription.Received(1).GetCheckoutUrlAsync("pro", "annual");
        var notice = Assert.IsType<ConfirmRequest>(h.Navigator.DataFor("Confirm"));
        Assert.Equal(("Checkout could not open.", "Checkout is not available right now. Try again later."), (notice.Title, notice.Message));
    }

    [Fact]
    public async Task The_plan_and_the_quotes_used_come_from_the_feature_gate()
    {
        using var h = new ModelHarness();
        h.Subscription.IsAvailable.Returns(true);
        h.Subscription.CurrentTier.Returns(SubscriptionTier.Free);
        await h.AddQuoteAsync("Boiler");

        var model = Create(h, new SupabaseConfig("https://abc.supabase.co", "anon-key"));

        Assert.Equal(("Free (5 quotes/mo)", "1 of 5"), (await model.CurrentPlanName, await model.QuotaUsage));
    }
}
