using QuoteCraft.Helpers;
using QuoteCraft.Models;
using Xunit;

namespace QuoteCraft.Tests.Data;

/// <summary>
/// MVUX feeds read on background threads while pages write on the UI thread. With one connection shared by all of
/// them, a command could run inside another thread's transaction or beside its open reader.
/// </summary>
public class ConcurrentAccessTests
{
    [Fact]
    public async Task Reads_writes_and_transactions_on_many_threads_do_not_collide()
    {
        using var test = new TestDatabase();
        var generator = new QuoteNumberGenerator(test.Quotes, test.Profiles);
        const int Quotes = 16;

        await Task.WhenAll(Enumerable.Range(0, Quotes).Select(i => Task.Run(async () =>
        {
            var quote = new QuoteEntity { Title = $"Quote {i}", QuoteNumber = await generator.GenerateAsync() };
            await test.Quotes.SaveAsync(quote);
            for (var n = 0; n < 3; n++)
            {
                await test.Quotes.SaveLineItemAsync(new LineItemEntity { QuoteId = quote.Id, Description = $"Item {n}", UnitPrice = 10m });
                await test.Quotes.GetAllAsync();
            }

            var reversed = (await test.Quotes.GetLineItemsAsync(quote.Id)).Select(item => item.Id).Reverse().ToList();
            await test.Quotes.ReorderLineItemsAsync(quote.Id, reversed);
            await test.History.RecordAsync(quote.Id, nameof(QuoteStatus.Sent));
        })));

        var all = await test.Quotes.GetAllAsync();
        Assert.Equal(Quotes, all.Count);
        Assert.Equal(Quotes, all.Select(q => q.QuoteNumber).Distinct().Count());
        Assert.All(all, q => Assert.Equal(new[] { "Item 2", "Item 1", "Item 0" }, q.LineItems.Select(item => item.Description)));
        Assert.All(all, q => Assert.Equal(30m, q.Subtotal));
    }
}
