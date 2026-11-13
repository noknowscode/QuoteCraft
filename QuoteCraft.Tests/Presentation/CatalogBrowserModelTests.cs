using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Add from Catalog: the categories, search and filter, and a tap adding the item with the default markup.</summary>
public class CatalogBrowserModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<CatalogBrowserModel> Create(ModelHarness h, QuoteEntity quote)
    {
        var model = new CatalogBrowserModel(new CatalogBrowserRequest(quote.Id), h.Catalog, h.Db.Quotes, h.Db.Profiles);
        _ = (await model.SearchText, await model.SelectedCategory, await model.Status);
        return model;
    }

    [Fact]
    public async Task All_comes_first_then_the_catalogs_categories_in_order()
    {
        using var h = new ModelHarness();
        var categories = (await h.Catalog.GetAllAsync()).Select(i => i.Category).Distinct().Order();

        var model = await Create(h, await h.AddQuoteAsync("Boiler"));

        Assert.Equal(["All", .. categories], await model.Categories);
    }

    [Fact]
    public async Task A_category_and_a_search_narrow_the_items()
    {
        using var h = new ModelHarness();
        var plumbing = (await h.Catalog.GetAllAsync()).Where(i => i.Category == "Plumbing").ToList();
        var model = await Create(h, await h.AddQuoteAsync("Boiler"));

        await model.SelectedCategory.UpdateAsync(_ => "Plumbing", None);
        var items = await Settled(async () => await ListOf(model.Items), l => l.Count == plumbing.Count);
        Assert.All(items, i => Assert.Equal("Plumbing", i.Category));

        var word = plumbing[0].Description.Split(' ')[0];
        await model.SearchText.UpdateAsync(_ => word, None);
        var found = await Settled(async () => await ListOf(model.Items), l => l.Count < plumbing.Count || l.All(i => i.Description.Contains(word, StringComparison.OrdinalIgnoreCase)));
        Assert.All(found, i => Assert.Contains(word, i.Description, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(found, i => i.Id == plumbing[0].Id);
    }

    [Fact]
    public async Task A_tap_adds_one_of_the_item_with_the_markup_and_says_so()
    {
        using var h = new ModelHarness();
        var profile = await h.Db.Profiles.GetAsync();
        profile.DefaultMarkup = 10m;
        await h.Db.Profiles.SaveAsync(profile);
        var quote = await h.AddQuoteAsync("Boiler");
        var item = (await h.Catalog.GetAllAsync()).First();
        var model = await Create(h, quote);

        await model.AddItem(item, None);
        await model.AddItem(item, None);

        var lines = await h.Db.Quotes.GetLineItemsAsync(quote.Id);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal((item.Description, Money.WithMarkup(item.UnitPrice, 10m), 1), (l.Description, l.UnitPrice, l.Quantity)));
        Assert.Equal($"Added \"{item.Description}\" to the quote.", await model.Status);
    }
}
