using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The line-item dialog: what Save refuses, the message as fields are fixed, and what it returns.</summary>
public class LineItemEditorModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<LineItemEditorModel> Create(ModelHarness h, LineItemEntity? item = null)
    {
        var model = new LineItemEditorModel(h.Navigator, new LineItemEditRequest(item));
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
}
