using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The New Quote dialog: a title, an optional client, and the Draft it returns.</summary>
public class NewQuoteModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The dialog once its fields have their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<NewQuoteModel> Create(ModelHarness h)
    {
        var model = new NewQuoteModel(h.Navigator, h.Db.Quotes, h.Db.Clients, h.Db.Profiles, h.Numbers);
        _ = (await model.QuoteTitle, await model.ClientName, await model.ValidationError);
        return model;
    }

    [Fact]
    public async Task Create_needs_a_title()
    {
        using var h = new ModelHarness();
        var model = await Create(h);

        await model.Create(None);

        Assert.Equal("Enter a title for the quote.", await model.ValidationError);
        Assert.Empty(h.Navigator.Requests);
        Assert.Empty(await h.Db.Quotes.GetAllAsync());
    }

    [Fact]
    public async Task Create_saves_a_draft_with_the_business_defaults_linked_to_the_named_client()
    {
        using var h = new ModelHarness();
        var profile = await h.Db.Profiles.GetAsync();
        profile.DefaultTaxRate = 8.25m;
        profile.QuoteValidDays = 30;
        await h.Db.Profiles.SaveAsync(profile);
        var dana = await h.AddClientAsync("Dana Reyes");
        var model = await Create(h);

        await model.QuoteTitle.UpdateAsync(_ => "  Furnace replacement ", None);
        await model.ClientName.UpdateAsync(_ => "Dana Reyes", None);
        await model.Create(None);

        var returned = Assert.IsType<QuoteEntity>(h.Navigator.BackResult);
        var stored = (await h.Db.Quotes.GetByIdAsync(returned.Id))!;
        Assert.Equal(("Furnace replacement", QuoteStatus.Draft, 8.25m, dana.Id, "Dana Reyes"),
            (stored.Title, stored.Status, stored.TaxRate, stored.ClientId, stored.ClientName));
        Assert.InRange((stored.ValidUntil!.Value - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public async Task A_name_with_no_client_behind_it_stays_on_the_quote_unlinked()
    {
        using var h = new ModelHarness();
        var model = await Create(h);

        await model.QuoteTitle.UpdateAsync(_ => "Furnace", None);
        await model.ClientName.UpdateAsync(_ => "Walk-in customer", None);
        await model.Create(None);

        var stored = Assert.Single(await h.Db.Quotes.GetAllAsync());
        Assert.Equal(("Walk-in customer", (string?)null), (stored.ClientName, stored.ClientId));
    }

    [Fact]
    public async Task Client_suggestions_contain_what_is_typed()
    {
        using var h = new ModelHarness();
        await h.AddClientAsync("Dana Reyes");
        await h.AddClientAsync("Bob Johnson");
        var model = await Create(h);

        await model.ClientName.UpdateAsync(_ => "rey", None);

        Assert.Equal(["Dana Reyes"], await Settled(async () => await ListOf(model.ClientSuggestions), l => l.Count == 1));
    }
}
