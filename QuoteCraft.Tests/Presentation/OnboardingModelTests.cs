using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QuoteCraft.Data;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>First run: the steps, their checks, and what Create First Quote and Skip leave behind.</summary>
public class OnboardingModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The steps once their fields hold their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<OnboardingModel> Create(ModelHarness h, IOnboardingService onboarding)
    {
        var model = new OnboardingModel(h.Navigator, h.Db.Profiles, onboarding, h.Creation);
        _ = (await model.CurrentStep, await model.BusinessName, await model.Phone, await model.Email, await model.Address,
            await model.TaxRate, await model.ValidDays, await model.QuotePrefix, await model.ValidationError);
        return model;
    }

    [Theory]
    [InlineData("dana@", "", "Please enter a valid email address.")]
    [InlineData("", "555-12", "Phone number must have at least 7 digits.")]
    public async Task Business_info_stays_on_its_step_until_it_reads(string email, string phone, string message)
    {
        using var h = new ModelHarness();
        var model = await Create(h, Substitute.For<IOnboardingService>());
        await model.NextStep(None);

        await model.Email.UpdateAsync(_ => email, None);
        await model.Phone.UpdateAsync(_ => phone, None);
        await model.NextStep(None);

        Assert.Equal((2, message), (await model.CurrentStep, await model.ValidationError));

        await model.Email.UpdateAsync(_ => "dana@example.com", None);
        await model.Phone.UpdateAsync(_ => "(555) 010-2030", None);
        await model.NextStep(None);
        Assert.Equal((3, string.Empty), (await model.CurrentStep, await model.ValidationError));
    }

    [Theory]
    [InlineData("abc", "14", "Tax rate must be between 0 and 100.")]
    [InlineData("8.25", "0", "Valid days must be at least 1.")]
    public async Task Quote_settings_are_checked_before_finishing(string rate, string days, string message)
    {
        using var h = new ModelHarness();
        var onboarding = Substitute.For<IOnboardingService>();
        var model = await Create(h, onboarding);

        await model.TaxRate.UpdateAsync(_ => rate, None);
        await model.ValidDays.UpdateAsync(_ => days, None);
        await model.Complete(None);

        Assert.Equal(message, await model.ValidationError);
        await onboarding.DidNotReceive().MarkCompleteAsync();
        Assert.Empty(h.Navigator.Requests);
    }

    [Fact]
    public async Task Create_first_quote_saves_the_business_and_opens_a_draft_with_its_defaults()
    {
        using var h = new ModelHarness();
        var onboarding = Substitute.For<IOnboardingService>();
        var model = await Create(h, onboarding);

        await model.BusinessName.UpdateAsync(_ => "Northside Plumbing", None);
        await model.Email.UpdateAsync(_ => "office@northside.example", None);
        await model.TaxRate.UpdateAsync(_ => "8.25", None);
        await model.ValidDays.UpdateAsync(_ => "21", None);
        await model.QuotePrefix.UpdateAsync(_ => "NP-", None);
        await model.Complete(None);

        var profile = await h.Db.Profiles.GetAsync();
        Assert.Equal(("Northside Plumbing", "office@northside.example", 8.25m, 21, "NP-"),
            (profile.BusinessName, profile.Email, profile.DefaultTaxRate, profile.QuoteValidDays, profile.QuoteNumberPrefix));
        await onboarding.Received(1).MarkCompleteAsync();

        // Onboarding is the first route on a first run: the editor opens inside Main, with nothing to go back to.
        var request = Assert.Single(h.Navigator.Requests);
        Assert.Equal(Qualifiers.ClearBackStack + "Main/QuoteEditor", h.Navigator.Routes.Single());
        var quote = Assert.IsType<QuoteEntity>(h.Navigator.DataFor(request.Route.Base!));
        var stored = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(("New Quote", QuoteStatus.Draft, 8.25m), (stored.Title, stored.Status, stored.TaxRate));
        Assert.StartsWith("NP-", stored.QuoteNumber);
        Assert.InRange((stored.ValidUntil!.Value - DateTimeOffset.UtcNow).TotalDays, 20.9, 21.1);
    }

    [Fact]
    public async Task Skip_finishes_onboarding_without_a_quote()
    {
        using var h = new ModelHarness();
        var onboarding = Substitute.For<IOnboardingService>();

        await (await Create(h, onboarding)).Skip(None);

        await onboarding.Received(1).MarkCompleteAsync();
        Assert.Equal(Qualifiers.ClearBackStack + "Main", Assert.Single(h.Navigator.Routes));
        Assert.Empty(await h.Db.Quotes.GetAllAsync());
    }

    [Fact]
    public async Task A_failed_skip_stays_on_the_step_and_says_so()
    {
        using var h = new ModelHarness();
        var onboarding = Substitute.For<IOnboardingService>();
        onboarding.MarkCompleteAsync().ThrowsAsync(new IOException("The disk is full."));
        var model = await Create(h, onboarding);

        await model.Skip(None);

        Assert.Equal("Your setup could not be finished. Try again.", await model.ValidationError);
        Assert.Empty(h.Navigator.Routes);
    }

    [Fact]
    public async Task A_failed_create_first_quote_stays_on_the_step_and_says_so()
    {
        using var h = new ModelHarness();
        var profiles = Substitute.For<IBusinessProfileRepository>();
        profiles.GetAsync().Returns(new BusinessProfileEntity());
        profiles.SaveAsync(Arg.Any<BusinessProfileEntity>()).ThrowsAsync(new IOException("The disk is full."));
        var model = new OnboardingModel(h.Navigator, profiles, Substitute.For<IOnboardingService>(), h.Creation);
        _ = (await model.TaxRate, await model.ValidDays, await model.ValidationError);

        await model.Complete(None);

        Assert.Equal("Your setup could not be finished. Try again.", await model.ValidationError);
        Assert.Empty(h.Navigator.Routes);
    }

    [Fact]
    public async Task Back_clears_the_message_and_returns_a_step()
    {
        using var h = new ModelHarness();
        var model = await Create(h, Substitute.For<IOnboardingService>());
        await model.NextStep(None);
        await model.Email.UpdateAsync(_ => "nope", None);
        await model.NextStep(None);

        await model.PreviousStep(None);

        Assert.Equal((1, string.Empty), (await model.CurrentStep, await model.ValidationError));
    }
}
