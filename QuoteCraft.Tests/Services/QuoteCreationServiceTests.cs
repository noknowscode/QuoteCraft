using QuoteCraft.Models;
using QuoteCraft.Tests.Presentation;
using Xunit;

namespace QuoteCraft.Tests.Services;

/// <summary>The one place new quotes are made (New Quote, a client's New Quote, onboarding, Duplicate).</summary>
public class QuoteCreationServiceTests
{
    [Fact]
    public async Task A_draft_gets_the_next_number_and_the_business_defaults()
    {
        using var h = new ModelHarness();
        var profile = await h.Db.Profiles.GetAsync();
        profile.DefaultTaxRate = 9.975m;
        profile.QuoteValidDays = 7;
        profile.QuoteNumberPrefix = "NP-";
        await h.Db.Profiles.SaveAsync(profile);

        var first = await h.Creation.CreateDraftAsync("First");
        var second = await h.Creation.CreateDraftAsync("Second");

        Assert.Equal((QuoteStatus.Draft, 9.975m), (second.Status, second.TaxRate));
        Assert.Equal($"NP-{DateTime.UtcNow.Year}-0001", first.QuoteNumber);
        Assert.Equal($"NP-{DateTime.UtcNow.Year}-0002", second.QuoteNumber);
        Assert.InRange((second.ValidUntil!.Value - DateTimeOffset.UtcNow).TotalDays, 6.9, 7.1);
        Assert.Equal(2, (await h.Db.Quotes.GetAllAsync()).Count);
    }

    [Fact]
    public async Task A_typed_client_name_links_the_client_with_that_name()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes");

        var linked = await h.Creation.CreateDraftAsync("Furnace", " dana reyes ");
        var unlinked = await h.Creation.CreateDraftAsync("Furnace", "Walk-in");
        var none = await h.Creation.CreateDraftAsync("Furnace", "  ");

        Assert.Equal((dana.Id, "dana reyes"), (linked.ClientId, linked.ClientName));
        Assert.Equal(((string?)null, "Walk-in"), (unlinked.ClientId, unlinked.ClientName));
        Assert.Equal(((string?)null, (string?)null), (none.ClientId, none.ClientName));
    }

    [Fact]
    public async Task A_draft_for_a_client_keeps_that_client_even_when_another_has_the_same_name()
    {
        using var h = new ModelHarness();
        await h.AddClientAsync("Sam Lee");
        var second = await h.AddClientAsync("Sam Lee");

        var quote = await h.Creation.CreateDraftForClientAsync("New Quote", second);

        Assert.Equal(second.Id, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.ClientId);
    }

    [Fact]
    public async Task Duplicate_copies_everything_but_the_number_status_and_dates()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes");
        var source = await h.AddQuoteAsync("Boiler", QuoteStatus.Sent, dana, taxRate: 13m,
            validUntil: DateTimeOffset.UtcNow.AddDays(-2), items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        source.Notes = "Gate code 4412.";
        await h.Db.Quotes.SaveAsync(source);

        var copy = (await h.Creation.DuplicateAsync(source.Id))!;
        var stored = (await h.Db.Quotes.GetByIdAsync(copy.Id))!;

        Assert.Equal(("Boiler (Copy)", QuoteStatus.Draft, dana.Id, "Dana Reyes", 13m, "Gate code 4412."),
            (stored.Title, stored.Status, stored.ClientId, stored.ClientName, stored.TaxRate, stored.Notes));
        Assert.NotEqual(source.QuoteNumber, stored.QuoteNumber);
        Assert.True(stored.ValidUntil > DateTimeOffset.UtcNow);
        Assert.Equal(source.LineItems.Select(i => (i.Description, i.UnitPrice, i.Quantity)),
            stored.LineItems.Select(i => (i.Description, i.UnitPrice, i.Quantity)));
        Assert.Null(await h.Creation.DuplicateAsync("gone"));
    }
}
