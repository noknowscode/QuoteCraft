using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The catalog-item dialog: its fields, its checks, Save and Delete.</summary>
public class CatalogItemEditorModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly string[] Categories = ["HVAC", "Plumbing"];

    private static async Task<CatalogItemEditorModel> Create(ModelHarness h, CatalogItemEntity item, bool isNew)
    {
        var model = new CatalogItemEditorModel(h.Navigator, h.Catalog, h.Currency, new CatalogItemEditRequest(item, isNew, Categories));
        _ = (await model.Description, await model.UnitPrice, await model.Category, await model.ValidationError);
        return model;
    }

    [Fact]
    public async Task A_new_item_starts_empty_in_the_category_it_was_added_to()
    {
        using var h = new ModelHarness();
        h.Currency.Code = "CAD";

        var model = await Create(h, new CatalogItemEntity { Category = "Plumbing" }, isNew: true);

        Assert.Equal(("Add Catalog Item", false, "Plumbing", string.Empty), (model.Heading, model.IsEditing, model.InitialCategory, await model.UnitPrice));
        Assert.Equal($"Unit Price ({Money.Symbol("CAD")})", model.UnitPriceHeader);
    }

    [Theory]
    [InlineData(" ", "10", "Enter a description.")]
    [InlineData("Valve", "ten", "Enter the unit price as an amount")]
    public async Task Save_says_what_is_wrong_and_writes_nothing(string description, string price, string message)
    {
        using var h = new ModelHarness();
        var before = (await h.Catalog.GetAllAsync()).Count;
        var model = await Create(h, new CatalogItemEntity(), isNew: true);

        await model.Description.UpdateAsync(_ => description, None);
        await model.UnitPrice.UpdateAsync(_ => price, None);
        await model.Save(None);

        Assert.StartsWith(message, await model.ValidationError);
        Assert.Equal(before, (await h.Catalog.GetAllAsync()).Count);
        Assert.Empty(h.Navigator.Requests);
    }

    [Fact]
    public async Task Save_writes_the_item_and_an_empty_price_is_zero()
    {
        using var h = new ModelHarness();
        var item = new CatalogItemEntity();
        var model = await Create(h, item, isNew: true);

        await model.Description.UpdateAsync(_ => " Site visit ", None);
        await model.Category.UpdateAsync(_ => "HVAC", None);
        await model.Save(None);

        var stored = (await h.Catalog.GetByIdAsync(item.Id))!;
        Assert.Equal(("Site visit", "HVAC", 0m), (stored.Description, stored.Category, stored.UnitPrice));
        Assert.False(Assert.IsType<CatalogItemEditResult>(h.Navigator.BackResult).DeleteRequested);
    }

    [Fact]
    public async Task An_edit_reads_the_price_in_the_users_format_and_delete_asks_the_caller()
    {
        using var h = new ModelHarness();
        var item = (await h.Catalog.GetAllAsync()).First();
        var model = await Create(h, item, isNew: false);

        Assert.Equal(Money.FormatAmountInput(item.UnitPrice), await model.UnitPrice);
        await model.UnitPrice.UpdateAsync(_ => Money.FormatAmountInput(42.5m), None);
        await model.Save(None);
        Assert.Equal(42.5m, (await h.Catalog.GetByIdAsync(item.Id))!.UnitPrice);

        await model.Delete(None);
        Assert.True(Assert.IsType<CatalogItemEditResult>(h.Navigator.BackResult).DeleteRequested);
        Assert.NotNull(await h.Catalog.GetByIdAsync(item.Id));
    }
}
