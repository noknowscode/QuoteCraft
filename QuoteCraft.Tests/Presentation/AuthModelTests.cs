using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The sign-in page (a Supabase project configured): its checks, the three requests, the busy bar and failures.</summary>
public class AuthModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly AuthState SignedIn = new(true, "user-1", "dana@northside.example", "access", "refresh");
    private static readonly AuthState SignedOut = new(false, null, null, null, null);

    /// <summary>The page once its states hold their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<AuthModel> Create(ModelHarness h, IAuthService auth, string email = "dana@northside.example", string password = "secret1")
    {
        var model = new AuthModel(h.Navigator, auth);
        _ = (await model.Email, await model.Password, await model.ErrorMessage, await model.IsSignUp, await model.IsBusy, await model.MagicLinkSent);
        await model.Email.UpdateAsync(_ => email, None);
        await model.Password.UpdateAsync(_ => password, None);
        return model;
    }

    [Theory]
    [InlineData("", "secret1", "Email is required.")]
    [InlineData("dana@northside.example", "", "Password is required.")]
    public async Task Sign_in_needs_an_email_and_a_password(string email, string password, string message)
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        var model = await Create(h, auth, email, password);

        await model.SignIn(None);

        Assert.Equal(message, await model.ErrorMessage);
        await auth.DidNotReceive().SignInAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Signing_in_opens_the_app_and_clears_the_way_back()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        auth.SignInAsync("dana@northside.example", "secret1").Returns(SignedIn);
        var model = await Create(h, auth, email: "  dana@northside.example ");

        await model.SignIn(None);

        Assert.Equal([Qualifiers.ClearBackStack + "Main"], h.Navigator.Routes);
        Assert.Equal((string.Empty, false), (await model.ErrorMessage, await model.IsBusy));
    }

    [Fact]
    public async Task A_refused_sign_in_says_so()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        auth.SignInAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(SignedOut);
        var model = await Create(h, auth);

        await model.SignIn(None);

        Assert.Equal("Invalid email or password.", await model.ErrorMessage);
        Assert.Empty(h.Navigator.Routes);
    }

    [Fact]
    public async Task The_busy_bar_shows_while_the_request_runs_and_a_failure_says_what_did_not_happen()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        var request = new TaskCompletionSource<AuthState>();
        auth.SignInAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(request.Task);
        var model = await Create(h, auth);

        var signingIn = model.SignIn(None).AsTask();
        Assert.True(await Settled(async () => await model.IsBusy, busy => busy));

        request.SetException(new HttpRequestException("Connection refused"));
        await signingIn;

        Assert.Equal(("Sign in failed. Try again.", false), (await model.ErrorMessage, await model.IsBusy));
        Assert.Empty(h.Navigator.Routes);
    }

    [Fact]
    public async Task Sign_up_needs_six_characters_and_asks_to_confirm_by_email()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        auth.SignUpAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(SignedOut);
        var model = await Create(h, auth, password: "12345");

        await model.SignUp(None);
        Assert.Equal("Password must be at least 6 characters.", await model.ErrorMessage);

        await model.Password.UpdateAsync(_ => "123456", None);
        await model.SignUp(None);
        Assert.Equal("Check your email to confirm your account.", await model.ErrorMessage);
        await auth.Received(1).SignUpAsync("dana@northside.example", "123456");
    }

    [Fact]
    public async Task A_sign_in_link_says_it_was_sent_or_why_not()
    {
        using var h = new ModelHarness();
        var auth = Substitute.For<IAuthService>();
        auth.SignInWithMagicLinkAsync("dana@northside.example").Returns(SignedOut);
        var model = await Create(h, auth);

        await model.SendMagicLink(None);
        Assert.True(await model.MagicLinkSent);

        auth.SignInWithMagicLinkAsync("dana@northside.example").ThrowsAsync(new InvalidOperationException("No Supabase project is set up."));
        await model.SendMagicLink(None);
        Assert.Equal("The sign-in link could not be sent. No Supabase project is set up.", await model.ErrorMessage);
    }

    [Fact]
    public async Task The_mode_switch_names_the_other_mode_and_clears_the_message()
    {
        using var h = new ModelHarness();
        var model = await Create(h, Substitute.For<IAuthService>(), email: "");
        await model.SignIn(None);
        Assert.Equal("Don't have an account? Sign up", await model.ModeToggleText);

        await model.ToggleMode(None);

        Assert.True(await model.IsSignUp);
        Assert.Equal("Have an account? Sign in", await Settled(async () => await model.ModeToggleText ?? string.Empty, t => t.StartsWith("Have")));
        Assert.Equal(string.Empty, await model.ErrorMessage);
    }

    [Fact]
    public async Task Continue_without_an_account_opens_the_app()
    {
        using var h = new ModelHarness();
        var model = await Create(h, Substitute.For<IAuthService>());

        await model.SkipAuth();

        Assert.Equal([Qualifiers.ClearBackStack + "Main"], h.Navigator.Routes);
    }
}
