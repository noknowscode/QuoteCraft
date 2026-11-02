using System.Globalization;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Tests.Data;
using Xunit;

namespace QuoteCraft.Tests.Services;

/// <summary>The line-item form and the order of a quote's items.</summary>
public class LineItemTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("Faucet", "$150", "2", 150, 2)]
    [InlineData(" Faucet ", "1,250.00", "1", 1250, 1)]
    public void The_form_reads_a_price_with_a_symbol_or_separators(string description, string price, string quantity,
        double expectedPrice, int expectedQuantity)
    {
        var error = LineItemInput.TryRead(description, price, quantity, out var d, out var p, out var q, English);

        Assert.Null(error);
        Assert.Equal(("Faucet", (decimal)expectedPrice, expectedQuantity), (d, p, q));
    }

    [Theory]
    [InlineData("", "150", "1", "description")]
    [InlineData("Faucet", "1,5", "1", "unit price")]   // was read as 15
    [InlineData("Faucet", "abc", "1", "unit price")]   // was saved as 0
    [InlineData("Faucet", "", "1", "unit price")]
    [InlineData("Faucet", "150", "0", "quantity")]
    [InlineData("Faucet", "150", "1.5", "quantity")]
    public void The_form_says_what_is_wrong_instead_of_guessing(string description, string price, string quantity, string field)
    {
        var error = LineItemInput.TryRead(description, price, quantity, out _, out _, out _, English);

        Assert.Contains(field, error);
    }

    [Fact]
    public async Task An_edited_item_keeps_its_place_and_a_new_one_goes_last()
    {
        using var test = new TestDatabase();
        var quoteId = Guid.NewGuid().ToString();
        var items = new[] { "A", "B", "C" }.Select(d => new LineItemEntity { QuoteId = quoteId, Description = d, UnitPrice = 1m }).ToList();
        foreach (var item in items)
        {
            await test.Quotes.SaveLineItemAsync(item);
        }

        // What the dialog returns for an edit: the id and the new values, SortOrder left at 0.
        await test.Quotes.SaveLineItemAsync(new LineItemEntity { Id = items[2].Id, QuoteId = quoteId, Description = "C2", UnitPrice = 2m });
        await test.Quotes.SaveLineItemAsync(new LineItemEntity { QuoteId = quoteId, Description = "D", UnitPrice = 1m });

        var saved = await test.Quotes.GetLineItemsAsync(quoteId);
        Assert.Equal(new[] { "A", "B", "C2", "D" }, saved.Select(i => i.Description));
    }

    [Fact]
    public async Task Moving_works_when_items_share_a_position()
    {
        using var test = new TestDatabase();
        var quoteId = Guid.NewGuid().ToString();
        foreach (var description in new[] { "A", "B", "C" })
        {
            await test.Quotes.SaveLineItemAsync(new LineItemEntity { QuoteId = quoteId, Description = description });
        }

        // Items saved by the old dialog all had position 0, so swapping two positions changed nothing.
        var connection = await test.Database.GetConnectionAsync();
        var reset = connection.CreateCommand();
        reset.CommandText = "UPDATE line_items SET sort_order = 0";
        await reset.ExecuteNonQueryAsync();

        var ids = (await test.Quotes.GetLineItemsAsync(quoteId)).Select(i => i.Id).ToList();
        (ids[1], ids[2]) = (ids[2], ids[1]);
        await test.Quotes.ReorderLineItemsAsync(quoteId, ids);

        var saved = await test.Quotes.GetLineItemsAsync(quoteId);
        Assert.Equal(new[] { "A", "C", "B" }, saved.Select(i => i.Description));
        Assert.Equal(new[] { 0, 1, 2 }, saved.Select(i => i.SortOrder));
    }
}
