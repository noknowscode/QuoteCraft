namespace QuoteCraft.Services;

/// <summary>
/// Moves a quote between statuses and records each change in its history (PRD F3, F17). The rules live here:
/// <list type="bullet">
/// <item>Only a quote a client has (Sent or Viewed) expires. A Draft has not been offered to anyone, so its
/// valid-until date is a plan, not a deadline.</item>
/// <item>Sending starts the validity period: the quote stays valid for the profile's number of days from today, or
/// until its own date if that is later.</item>
/// <item>Reopening an expired quote makes it a Draft again with a fresh validity period, ready to send again
/// (F17.3).</item>
/// </list>
/// </summary>
public class QuoteStatusService
{
    private readonly IQuoteRepository _quoteRepo;
    private readonly IStatusHistoryRepository _history;
    private readonly IBusinessProfileRepository _profileRepo;

    public QuoteStatusService(IQuoteRepository quoteRepo, IStatusHistoryRepository history, IBusinessProfileRepository profileRepo)
    {
        _quoteRepo = quoteRepo;
        _history = history;
        _profileRepo = profileRepo;
    }

    /// <summary>An explicit status change (the editor's Mark Sent, Accepted and Declined).</summary>
    public async Task<QuoteEntity?> SetStatusAsync(string quoteId, QuoteStatus status, string changedBy = "user")
    {
        var quote = await _quoteRepo.GetByIdAsync(quoteId);
        if (quote is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        quote.Status = status;
        if (status is QuoteStatus.Sent or QuoteStatus.Draft)
        {
            var validFrom = now.AddDays((await _profileRepo.GetAsync()).QuoteValidDays);
            quote.ValidUntil = quote.ValidUntil is { } current && current > validFrom ? current : validFrom;
        }
        if (status == QuoteStatus.Sent)
        {
            quote.SentAt = now;
        }

        quote.UpdatedAt = now;
        await _quoteRepo.SaveAsync(quote);
        await _history.RecordAsync(quoteId, status.ToString(), changedBy);
        return quote;
    }

    /// <summary>
    /// The quote went out by email: Sent, unless the client has already answered (Accepted or Declined stay).
    /// </summary>
    public async Task<QuoteEntity?> MarkSentAsync(string quoteId)
    {
        var quote = await _quoteRepo.GetByIdAsync(quoteId);
        return quote is null or { Status: QuoteStatus.Accepted or QuoteStatus.Declined }
            ? quote
            : await SetStatusAsync(quoteId, QuoteStatus.Sent);
    }

    /// <summary>An expired quote back to Draft, valid again; any other status is left as it is.</summary>
    public async Task<QuoteEntity?> ReopenAsync(string quoteId)
    {
        var quote = await _quoteRepo.GetByIdAsync(quoteId);
        return quote is { Status: QuoteStatus.Expired }
            ? await SetStatusAsync(quoteId, QuoteStatus.Draft)
            : quote;
    }

    /// <summary>Sent and Viewed quotes past their valid-until date become Expired.</summary>
    public async Task<IReadOnlyList<QuoteEntity>> ExpireOverdueAsync(DateTimeOffset now)
    {
        var overdue = await _quoteRepo.GetExpirableAsync(now);
        foreach (var quote in overdue)
        {
            quote.Status = QuoteStatus.Expired;
            quote.UpdatedAt = DateTimeOffset.UtcNow;
            await _quoteRepo.SaveAsync(quote);
            await _history.RecordAsync(quote.Id, nameof(QuoteStatus.Expired), "system");
        }
        return overdue;
    }
}
