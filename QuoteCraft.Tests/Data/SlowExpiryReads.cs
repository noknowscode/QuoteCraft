using QuoteCraft.Data;
using QuoteCraft.Models;

namespace QuoteCraft.Tests.Data;

/// <summary>
/// The repository with a pause after it lists the quotes to expire, so expiry runs started together overlap the way
/// they can in the app (a slow disk, a busy database).
/// </summary>
public sealed class SlowExpiryReads(IQuoteRepository inner) : IQuoteRepository
{
    public async Task<List<QuoteEntity>> GetExpirableAsync(DateTimeOffset now)
    {
        var quotes = await inner.GetExpirableAsync(now);
        await Task.Delay(100);
        return quotes;
    }

    public Task<List<QuoteEntity>> GetAllAsync() => inner.GetAllAsync();
    public Task<QuoteEntity?> GetByIdAsync(string id) => inner.GetByIdAsync(id);
    public Task SaveAsync(QuoteEntity quote) => inner.SaveAsync(quote);
    public Task DeleteAsync(string id) => inner.DeleteAsync(id);
    public Task<string> NextQuoteNumberAsync(string prefix, int year) => inner.NextQuoteNumberAsync(prefix, year);
    public Task SaveLineItemAsync(LineItemEntity item) => inner.SaveLineItemAsync(item);
    public Task ReorderLineItemsAsync(string quoteId, IReadOnlyList<string> orderedIds) => inner.ReorderLineItemsAsync(quoteId, orderedIds);
    public Task DeleteLineItemAsync(string id) => inner.DeleteLineItemAsync(id);
    public Task<List<LineItemEntity>> GetLineItemsAsync(string quoteId) => inner.GetLineItemsAsync(quoteId);
    public Task<int> CountCreatedSinceAsync(DateTimeOffset since) => inner.CountCreatedSinceAsync(since);
    public Task<bool> MarkExpiredAsync(string id) => inner.MarkExpiredAsync(id);
}
