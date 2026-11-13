using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The line-item dialog and Add from Catalog that the editor and the open quote share.</summary>
public class QuoteLineItemDialogsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task A_cancelled_dialog_changes_nothing()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler", items: [("Pump", 100m, 1)]);
        // No answer: the dialog was cancelled.

        Assert.False(await h.LineItemDialogs.EditAsync(h.Navigator, this, quote.Id, quote.LineItems[0], None));

        Assert.Equal(["Pump"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));
    }

    [Fact]
    public async Task A_new_item_goes_last_an_edit_keeps_its_place_and_delete_removes_it()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler", items: [("Pump", 100m, 1), ("Valve", 20m, 2)]);
        var pump = quote.LineItems[0];

        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(new LineItemEntity { Description = "Gasket", UnitPrice = 5m, Quantity = 4 }, null));
        Assert.True(await h.LineItemDialogs.EditAsync(h.Navigator, this, quote.Id, null, None));
        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(new LineItemEntity { Id = pump.Id, Description = "Pump, 2 hp", UnitPrice = 120m, Quantity = 1 }, null));
        Assert.True(await h.LineItemDialogs.EditAsync(h.Navigator, this, quote.Id, pump, None));
        Assert.Equal(["Pump, 2 hp", "Valve", "Gasket"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));

        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(null, pump.Id));
        Assert.True(await h.LineItemDialogs.EditAsync(h.Navigator, this, quote.Id, pump, None));
        Assert.Equal(["Valve", "Gasket"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));
    }

    [Fact]
    public async Task Add_from_catalog_opens_the_browser_for_the_quote()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler");

        await h.LineItemDialogs.OpenCatalogAsync(h.Navigator, this, quote.Id, None);

        Assert.Equal(quote.Id, Assert.IsType<CatalogBrowserRequest>(h.Navigator.DataFor("CatalogBrowser")).QuoteId);
    }
}
