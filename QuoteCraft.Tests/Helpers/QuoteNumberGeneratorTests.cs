using Microsoft.Data.Sqlite;
using QuoteCraft.Data;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Tests.Data;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>Quote numbering against a real database: the per-year counter, the unique index and migration 6.</summary>
public class QuoteNumberGeneratorTests
{
    private static readonly int Year = DateTime.UtcNow.Year;

    [Fact]
    public async Task The_first_number_uses_the_prefix_and_the_year()
    {
        using var test = new TestDatabase();
        var profile = await test.Profiles.GetAsync();
        profile.QuoteNumberPrefix = "INV-";
        await test.Profiles.SaveAsync(profile);

        var number = await new QuoteNumberGenerator(test.Quotes, test.Profiles).GenerateAsync();

        Assert.Equal($"INV-{Year}-0001", number);
    }

    [Fact]
    public async Task A_number_is_not_reused_after_a_delete()
    {
        using var test = new TestDatabase();
        var generator = new QuoteNumberGenerator(test.Quotes, test.Profiles);
        var quotes = new List<QuoteEntity>();
        for (var i = 0; i < 3; i++)
        {
            var quote = new QuoteEntity { Title = $"Quote {i}", QuoteNumber = await generator.GenerateAsync() };
            await test.Quotes.SaveAsync(quote);
            quotes.Add(quote);
        }

        await test.Quotes.DeleteAsync(quotes[2].Id);
        await test.Quotes.DeleteAsync(quotes[1].Id);

        Assert.Equal($"QC-{Year}-0004", await generator.GenerateAsync());
    }

    [Fact]
    public async Task A_number_a_quote_already_has_is_skipped()
    {
        using var test = new TestDatabase();
        await test.Quotes.SaveAsync(new QuoteEntity { Title = "Imported", QuoteNumber = $"QC-{Year}-0001" });

        var number = await new QuoteNumberGenerator(test.Quotes, test.Profiles).GenerateAsync();

        Assert.Equal($"QC-{Year}-0002", number);
    }

    [Fact]
    public async Task Each_year_counts_from_one()
    {
        using var test = new TestDatabase();

        Assert.Equal("QC-2025-0001", await test.Quotes.NextQuoteNumberAsync("QC-", 2025));
        Assert.Equal("QC-2025-0002", await test.Quotes.NextQuoteNumberAsync("QC-", 2025));
        Assert.Equal("QC-2026-0001", await test.Quotes.NextQuoteNumberAsync("QC-", 2026));
    }

    [Fact]
    public async Task Two_quotes_cannot_share_a_number()
    {
        using var test = new TestDatabase();
        await test.Quotes.SaveAsync(new QuoteEntity { Title = "First", QuoteNumber = "QC-2026-0007" });

        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            test.Quotes.SaveAsync(new QuoteEntity { Title = "Second", QuoteNumber = "QC-2026-0007" }));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Fact]
    public async Task The_migration_renumbers_shared_numbers_and_continues_the_count()
    {
        using var test = new TestDatabase();
        await test.Database.GetConnectionAsync();

        // Put the database back to version 5, with two quotes that share a number, as the count-based numbering left it.
        await ExecuteAsync(test.Database, """
            DROP INDEX ux_quotes_quote_number;
            DROP TABLE quote_counters;
            DELETE FROM schema_version WHERE version >= 6;
            """);
        var older = new QuoteEntity { Title = "Older", QuoteNumber = "QC-2026-0002", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) };
        var newer = new QuoteEntity { Title = "Newer", QuoteNumber = "QC-2026-0002", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var first = new QuoteEntity { Title = "First", QuoteNumber = "QC-2026-0001", CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) };
        foreach (var quote in new[] { older, newer, first })
        {
            await test.Quotes.SaveAsync(quote);
        }

        // A new AppDatabase on the same file runs the migrations again from version 5.
        SqliteConnection.ClearAllPools();
        var reopened = new AppDatabase(test.Folder);
        var quotes = new QuoteRepository(reopened, NSubstitute.Substitute.For<QuoteCraft.Services.IPhotoService>());

        Assert.Equal("QC-2026-0002", (await quotes.GetByIdAsync(older.Id))!.QuoteNumber);
        Assert.Equal("QC-2026-0003", (await quotes.GetByIdAsync(newer.Id))!.QuoteNumber);
        Assert.Equal("QC-2026-0004", await quotes.NextQuoteNumberAsync("QC-", 2026));
        await Assert.ThrowsAsync<SqliteException>(() =>
            quotes.SaveAsync(new QuoteEntity { Title = "Again", QuoteNumber = "QC-2026-0001" }));
    }

    [Theory]
    [InlineData("QC-2026-0012", "QC-", 2026, 12)]
    [InlineData("2026-0003", "", 2026, 3)]
    [InlineData("INV-A-2025-10000", "INV-A-", 2025, 10000)]
    public void A_number_splits_into_prefix_year_and_sequence(string number, string prefix, int year, int sequence)
    {
        Assert.True(QuoteNumberGenerator.TryParse(number, out var p, out var y, out var s));
        Assert.Equal((prefix, year, sequence), (p, y, s));
        Assert.Equal(number, QuoteNumberGenerator.Format(p, y, s));
    }

    [Fact]
    public void A_number_in_another_shape_does_not_parse()
    {
        Assert.False(QuoteNumberGenerator.TryParse("Kitchen job", out _, out _, out _));
    }

    private static async Task ExecuteAsync(AppDatabase database, string sql)
    {
        var connection = await database.GetConnectionAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
