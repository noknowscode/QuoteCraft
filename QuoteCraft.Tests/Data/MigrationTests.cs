using QuoteCraft.Models;
using Xunit;

namespace QuoteCraft.Tests.Data;

public class MigrationTests
{
    [Fact]
    public async Task Upgrading_leaves_a_quote_that_shares_a_sample_title_alone()
    {
        using var test = new TestDatabase();
        var mine = new QuoteEntity
        {
            Title = "Bathroom Remodel",
            QuoteNumber = "QC-2026-0001",
            Status = QuoteStatus.Accepted,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(-30),
        };
        await test.Quotes.SaveAsync(mine);

        // Back to version 4, so the next start runs migration 5 (which used to set "Bathroom Remodel" to Sent) and 6.
        await test.ExecuteAsync("""
            DROP INDEX ux_quotes_quote_number;
            DROP TABLE quote_counters;
            DELETE FROM schema_version WHERE version >= 5;
            """);
        var saved = await test.Reopen().GetByIdAsync(mine.Id);

        Assert.Equal(QuoteStatus.Accepted, saved!.Status);
        Assert.Equal(mine.ValidUntil, saved.ValidUntil);
    }
}
