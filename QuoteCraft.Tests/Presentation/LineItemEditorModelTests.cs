using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The line-item dialog: what Save refuses, the message as fields are fixed, and what it returns.</summary>
public class LineItemEditorModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<LineItemEditorModel> Create(ModelHarness h, LineItemEntity? item = null)
    {
        var model = new LineItemEditorModel(h.Navigator, h.Currency, new LineItemEditRequest(item));
        _ = (await model.Description, await model.UnitPrice, await model.Quantity, await model.ValidationError);
        return model;
    }

    [Fact]
    public async Task Save_says_what_is_missing_and_the_message_follows_the_fields()
    {
        using var h = new ModelHarness();
        var model = await Create(h);

        await model.Save(None);
        Assert.Equal("Enter a description.", await model.ValidationError);

        await model.Description.UpdateAsync(_ => "Sink", None);
        var next = await Settled(async () => await model.ValidationError ?? string.Empty, e => e != "Enter a description.");
        Assert.StartsWith("Enter the unit price", next);

        await model.UnitPrice.UpdateAsync(_ => "350", None);
        Assert.Equal(string.Empty, await Settled(async () => await model.ValidationError ?? string.Empty, e => e.Length == 0));
        Assert.Empty(h.Navigator.Requests);
    }

    [Fact]
    public async Task Typing_before_the_first_save_shows_nothing()
    {
        using var h = new ModelHarness();
        var model = await Create(h);

        await model.UnitPrice.UpdateAsync(_ => "abc", None);
        await Task.Delay(100);

        Assert.Equal(string.Empty, await model.ValidationError);
    }

    [Fact]
    public async Task Save_returns_the_item_keeping_its_id_when_editing()
    {
        using var h = new ModelHarness();
        var existing = new LineItemEntity { Description = "Sink", UnitPrice = 350m, Quantity = 1 };
        var model = await Create(h, existing);

        await model.IncreaseQuantity(None);
        await model.Save(None);

        var result = Assert.IsType<LineItemEditResult>(h.Navigator.BackResult);
        Assert.Equal((existing.Id, "Sink", 350m, 2), (result.Saved!.Id, result.Saved.Description, result.Saved.UnitPrice, result.Saved.Quantity));
    }

    [Fact]
    public async Task The_price_header_and_the_line_total_are_in_the_business_currency()
    {
        using var h = new ModelHarness();
        h.Currency.Code = "EUR";
        var model = await Create(h);

        await model.UnitPrice.UpdateAsync(_ => Money.FormatAmountInput(12.5m), None);
        await model.Quantity.UpdateAsync(_ => "2", None);

        Assert.Equal($"Unit Price ({Money.Symbol("EUR")})", model.UnitPriceHeader);
        var expected = Money.Format(25m, "EUR");
        Assert.Equal(expected, await Settled(async () => await model.LineTotal ?? string.Empty, t => t == expected));
    }
}
