using Microsoft.Extensions.Logging.Abstractions;
using QuoteCraft.Models;
using QuoteCraft.Services;
using QuoteCraft.Tests.Data;
using Xunit;

namespace QuoteCraft.Tests.Services;

public class QuoteExpiryServiceTests
{
    [Fact]
    public async Task A_check_expires_overdue_quotes_and_reminds_once_before_expiry()
    {
        using var test = new TestDatabase();
        var now = DateTimeOffset.UtcNow;
        var overdue = new QuoteEntity { Title = "Overdue", QuoteNumber = "QC-2026-0001", Status = QuoteStatus.Sent, ValidUntil = now.AddDays(-1) };
        var soon = new QuoteEntity { Title = "Soon", QuoteNumber = "QC-2026-0002", Status = QuoteStatus.Viewed, ValidUntil = now.AddDays(2) };
        var draft = new QuoteEntity { Title = "Draft", QuoteNumber = "QC-2026-0003", Status = QuoteStatus.Draft, ValidUntil = now.AddDays(1) };
        foreach (var quote in new[] { overdue, soon, draft })
        {
            await test.Quotes.SaveAsync(quote);
        }

        var notifications = new NotificationService(test.Database, NullLogger<NotificationService>.Instance);
        var service = new QuoteExpiryService(test.Quotes, new QuoteStatusService(test.Quotes, test.History, test.Profiles),
            notifications, NullLogger<QuoteExpiryService>.Instance);

        // The timer runs the check every 5 minutes.
        await service.CheckExpirationsAsync();
        await service.CheckExpirationsAsync();

        Assert.Equal(QuoteStatus.Expired, (await test.Quotes.GetByIdAsync(overdue.Id))!.Status);
        Assert.Equal(QuoteStatus.Draft, (await test.Quotes.GetByIdAsync(draft.Id))!.Status);
        var reminder = Assert.Single(await notifications.GetAllAsync());
        Assert.Equal(soon.Id, reminder.QuoteId);
        Assert.Equal("Quote #QC-2026-0002 expires in 2 days.", reminder.Message);
    }
}
