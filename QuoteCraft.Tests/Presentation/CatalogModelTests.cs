using QuoteCraft.Presentation;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The catalog: categories from the seeded catalog, search, a category's items and the item dialog.</summary>
public class CatalogModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The page once its states hold their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<CatalogModel> Create(ModelHarness h)
    {
        var model = new CatalogModel(h.Navigator, h.Catalog);
        _ = (await model.SearchText, await model.SelectedCategory, await model.Version);
        return model;
    }

    [Fact]
    public async Task Categories_come_from_the_seeded_catalog_with_their_counts()
    {
        using var h = new ModelHarness();
        var all = await h.Catalog.GetAllAsync();
        var model = await Create(h);

        var cards = await model.CategoryCards;

        Assert.Equal(all.Select(i => i.Category).Distinct().Order(), cards.Select(c => c.Name));
        Assert.Equal(all.Count, cards.Sum(c => c.ItemCount));
        Assert.Equal(all.Count, await model.CatalogItemCount);
    }

    [Fact]
    public async Task Search_keeps_the_categories_with_a_matching_item()
    {
        using var h = new ModelHarness();
        var model = await Create(h);

        await model.SearchText.UpdateAsync(_ => "water heater", None);

        var cards = await Settled(async () => await ListOf(model.CategoryCards), l => l.Count < 7);
        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.True(c.ItemCount < 10));
    }

    [Fact]
    public async Task A_category_opens_its_items_and_a_second_tap_closes_it()
    {
        using var h = new ModelHarness();
        var plumbing = (await h.Catalog.GetAllAsync()).Where(i => i.Category == "Plumbing").ToList();
        var model = await Create(h);
        var card = (await model.CategoryCards).Single(c => c.Name == "Plumbing");

        await model.SelectCategoryCard(card, None);
        var detail = (await model.SelectedCategoryDetail)!;
        Assert.Equal(plumbing.Select(i => i.Description).Order(), detail.Items.Select(i => i.Description).Order());

        await model.SelectCategoryCard(card, None);
        Assert.Null(await model.SelectedCategory);
    }

    [Fact]
    public async Task Back_to_list_closes_the_category()
    {
        using var h = new ModelHarness();
        var model = await Create(h);
        await model.SelectCategoryCard((await model.CategoryCards).First(), None);

        await model.BackToList(None);

        Assert.Null(await model.SelectedCategory);
        Assert.Null(await model.SelectedCategoryDetail);
    }

    [Fact]
    public async Task Add_item_in_an_open_category_fills_the_category_in()
    {
        using var h = new ModelHarness();
        var model = await Create(h);
        await model.SelectCategoryCard((await model.CategoryCards).Single(c => c.Name == "HVAC"), None);

        await model.AddItemToCategory(None);

        var request = Assert.IsType<CatalogItemEditRequest>(h.Navigator.DataFor("CatalogItemEditor"));
        Assert.Equal(("HVAC", true), (request.Item.Category, request.IsNew));
        Assert.Contains("HVAC", request.Categories);
    }

    [Fact]
    public async Task Delete_in_the_item_dialog_asks_once_more_then_removes_the_item()
    {
        using var h = new ModelHarness();
        var item = (await h.Catalog.GetAllAsync()).First();
        var model = await Create(h);
        h.Navigator.Answer("CatalogItemEditor", new CatalogItemEditResult(DeleteRequested: true));

        h.Navigator.Confirm(false);
        await model.EditItem(CatalogDisplayItem.FromEntity(item), None);
        Assert.NotNull(await h.Catalog.GetByIdAsync(item.Id));

        h.Navigator.Confirm(true);
        await model.EditItem(CatalogDisplayItem.FromEntity(item), None);
        Assert.Null(await h.Catalog.GetByIdAsync(item.Id));
        Assert.Equal("Delete Catalog Item?", Assert.IsType<ConfirmRequest>(h.Navigator.DataFor("Confirm")).Title);
    }
}
