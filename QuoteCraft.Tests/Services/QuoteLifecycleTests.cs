using QuoteCraft.Models;
using QuoteCraft.Services;
using QuoteCraft.Tests.Data;
using Xunit;

namespace QuoteCraft.Tests.Services;

/// <summary>Expiry, sending and reopening (PRD F17), against a real database.</summary>
public class QuoteLifecycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task<QuoteEntity> AddAsync(TestDatabase test, QuoteStatus status, DateTimeOffset validUntil)
    {
        var quote = new QuoteEntity
        {
            Title = status.ToString(),
            QuoteNumber = await test.Quotes.NextQuoteNumberAsync("QC-", Now.Year),
            Status = status,
            ValidUntil = validUntil,
        };
        await test.Quotes.SaveAsync(quote);
        return quote;
    }

    private static QuoteStatusService Status(TestDatabase test) => new(test.Quotes, test.History, test.Profiles);

    [Fact]
    public async Task A_draft_past_its_date_does_not_expire()
    {
        using var test = new TestDatabase();
        var draft = await AddAsync(test, QuoteStatus.Draft, Now.AddDays(-1));

        var expired = await Status(test).ExpireOverdueAsync(Now);

        Assert.Empty(expired);
        Assert.Equal(QuoteStatus.Draft, (await test.Quotes.GetByIdAsync(draft.Id))!.Status);
    }

    [Theory]
    [InlineData(QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Viewed)]
    public async Task A_quote_the_client_has_expires_after_its_date(QuoteStatus status)
    {
        using var test = new TestDatabase();
        var overdue = await AddAsync(test, status, Now.AddDays(-1));
        var current = await AddAsync(test, status, Now.AddDays(1));

        await Status(test).ExpireOverdueAsync(Now);

        Assert.Equal(QuoteStatus.Expired, (await test.Quotes.GetByIdAsync(overdue.Id))!.Status);
        Assert.Equal(status, (await test.Quotes.GetByIdAsync(current.Id))!.Status);
        var history = await test.History.GetByQuoteIdAsync(overdue.Id);
        Assert.Equal(("Expired", "system"), (history.Single().Status, history.Single().ChangedBy));
    }

    [Fact]
    public async Task Expiry_asked_for_from_several_places_at_once_expires_each_quote_once()
    {
        using var test = new TestDatabase();
        var overdue = new[]
        {
            await AddAsync(test, QuoteStatus.Sent, Now.AddDays(-1)),
            await AddAsync(test, QuoteStatus.Sent, Now.AddDays(-2)),
            await AddAsync(test, QuoteStatus.Viewed, Now.AddDays(-3)),
        };
        // The app's service (the Quotes list's four feeds and the expiry timer share it), and a second one over the same
        // database, as another instance of the app would have; their reads are slow enough for the runs to overlap.
        var slow = new SlowExpiryReads(test.Quotes);
        var app = new QuoteStatusService(slow, test.History, test.Profiles);
        var other = new QuoteStatusService(slow, test.History, test.Profiles);

        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => (i % 4 == 3 ? other : app).ExpireOverdueAsync(Now))));

        Assert.Equal(overdue.Select(q => q.Id).Order(), runs.SelectMany(r => r).Select(q => q.Id).Order());
        foreach (var quote in overdue)
        {
            Assert.Equal(QuoteStatus.Expired, (await test.Quotes.GetByIdAsync(quote.Id))!.Status);
            Assert.Equal(["Expired"], (await test.History.GetByQuoteIdAsync(quote.Id)).Select(e => e.Status));
        }
    }

    [Fact]
    public async Task Sending_starts_the_validity_period_so_the_quote_does_not_expire_at_once()
    {
        using var test = new TestDatabase();
        var draft = await AddAsync(test, QuoteStatus.Draft, Now.AddDays(-10));

        var sent = await Status(test).SetStatusAsync(draft.Id, QuoteStatus.Sent);
        await Status(test).ExpireOverdueAsync(DateTimeOffset.UtcNow);

        var saved = (await test.Quotes.GetByIdAsync(draft.Id))!;
        Assert.Equal(QuoteStatus.Sent, saved.Status);
        Assert.NotNull(saved.SentAt);
        Assert.True(saved.ValidUntil > Now.AddDays(13), $"valid until {saved.ValidUntil}");
        Assert.Equal(sent!.ValidUntil, saved.ValidUntil);
    }

    [Fact]
    public async Task Sending_keeps_a_later_valid_until_date()
    {
        using var test = new TestDatabase();
        var later = Now.AddDays(60);
        var draft = await AddAsync(test, QuoteStatus.Draft, later);

        await Status(test).SetStatusAsync(draft.Id, QuoteStatus.Sent);

        Assert.Equal(later, (await test.Quotes.GetByIdAsync(draft.Id))!.ValidUntil);
    }

    [Fact]
    public async Task Sending_keeps_a_valid_until_date_that_is_ahead_even_when_sooner_than_the_default()
    {
        using var test = new TestDatabase();
        var soon = Now.AddDays(3);
        var draft = await AddAsync(test, QuoteStatus.Draft, soon);

        await Status(test).MarkSentAsync(draft.Id);

        var sent = (await test.Quotes.GetByIdAsync(draft.Id))!;
        Assert.Equal((QuoteStatus.Sent, soon), (sent.Status, sent.ValidUntil));
    }

    [Fact]
    public async Task An_expired_quote_can_be_reopened_and_sent_again()
    {
        using var test = new TestDatabase();
        var quote = await AddAsync(test, QuoteStatus.Sent, Now.AddDays(-1));
        var status = Status(test);
        await status.ExpireOverdueAsync(Now);

        var reopened = await status.ReopenAsync(quote.Id);
        Assert.Equal(QuoteStatus.Draft, reopened!.Status);
        Assert.True(reopened.ValidUntil > Now);

        var resent = await status.MarkSentAsync(quote.Id);
        await status.ExpireOverdueAsync(DateTimeOffset.UtcNow);

        Assert.Equal(QuoteStatus.Sent, resent!.Status);
        Assert.Equal(QuoteStatus.Sent, (await test.Quotes.GetByIdAsync(quote.Id))!.Status);
        var history = (await test.History.GetByQuoteIdAsync(quote.Id)).Select(h => h.Status);
        Assert.Equal(new[] { "Expired", "Draft", "Sent" }, history);
    }

    [Fact]
    public async Task Reopen_leaves_a_quote_that_has_not_expired()
    {
        using var test = new TestDatabase();
        var accepted = await AddAsync(test, QuoteStatus.Accepted, Now.AddDays(5));

        var result = await Status(test).ReopenAsync(accepted.Id);

        Assert.Equal(QuoteStatus.Accepted, result!.Status);
    }

    [Fact]
    public async Task Emailing_an_answered_quote_keeps_its_answer()
    {
        using var test = new TestDatabase();
        var accepted = await AddAsync(test, QuoteStatus.Accepted, Now.AddDays(5));

        await Status(test).MarkSentAsync(accepted.Id);

        Assert.Equal(QuoteStatus.Accepted, (await test.Quotes.GetByIdAsync(accepted.Id))!.Status);
    }
}
