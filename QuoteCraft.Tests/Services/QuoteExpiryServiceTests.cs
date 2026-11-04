using Microsoft.Extensions.Logging.Abstractions;
using QuoteCraft.Helpers;
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

    [Theory]
    [InlineData(0, "Quote #QC-2026-0001 expires today.")]
    [InlineData(1, "Quote #QC-2026-0001 expires in 1 day.")]
    [InlineData(2, "Quote #QC-2026-0001 expires in 2 days.")]
    public async Task The_reminder_counts_calendar_days_to_a_date_picked_in_the_editor(int daysAhead, string message)
    {
        using var test = new TestDatabase();
        var day = DateTimeOffset.UtcNow.AddDays(daysAhead);
        var quote = new QuoteEntity
        {
            Title = "Picked", QuoteNumber = "QC-2026-0001", Status = QuoteStatus.Sent,
            ValidUntil = ValidUntilDate.FromPicker(day),
        };
        await test.Quotes.SaveAsync(quote);
        var notifications = new NotificationService(test.Database, NullLogger<NotificationService>.Instance);
        var service = new QuoteExpiryService(test.Quotes, new QuoteStatusService(test.Quotes, test.History, test.Profiles),
            notifications, NullLogger<QuoteExpiryService>.Instance);

        await service.CheckExpirationsAsync();

        Assert.Equal(message, Assert.Single(await notifications.GetAllAsync()).Message);
    }
}
