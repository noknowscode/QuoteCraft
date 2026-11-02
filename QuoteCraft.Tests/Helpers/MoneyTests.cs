using System.Globalization;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Tests.Data;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

public class MoneyTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Theory]
    [InlineData("8.25", 8.25)]
    [InlineData("9.975", 9.975)]
    [InlineData("13", 13)]
    [InlineData("8.25 %", 8.25)]
    public void A_rate_is_read_and_shown_without_rounding(string typed, double expected)
    {
        Assert.True(Money.TryParseRate(typed, out var rate, English));
        Assert.Equal((decimal)expected, rate);
        Assert.Equal(typed.TrimEnd('%', ' '), Money.FormatRate(rate, English));
    }

    [Fact]
    public void Saving_a_rate_twice_keeps_it()
    {
        // The editor shows the stored rate and reads it back on every save: 8.25 % used to become 8.3 %.
        var rate = 8.25m;
        for (var save = 0; save < 2; save++)
        {
            Assert.True(Money.TryParseRate(Money.FormatRate(rate, English), out rate, English));
        }
        Assert.Equal(8.25m, rate);
    }

    [Fact]
    public void A_decimal_comma_culture_reads_and_shows_its_own_separator()
    {
        Assert.True(Money.TryParseRate("8,25", out var rate, German));
        Assert.Equal(8.25m, rate);
        Assert.Equal("9,975", Money.FormatRate(9.975m, German));

        Assert.True(Money.TryParseAmount("1.250,50", out var amount, German));
        Assert.Equal(1250.50m, amount);
        Assert.True(Money.TryParseAmount("150,5", out amount, German));
        Assert.Equal(150.5m, amount);
        Assert.Equal("150,00", Money.FormatAmountInput(150m, German));
    }

    [Theory]
    [InlineData("150", 150)]
    [InlineData("$150", 150)]
    [InlineData(" 1,250.00 ", 1250)]
    [InlineData("0.35", 0.35)]
    public void An_amount_is_read_in_the_users_culture(string typed, double expected)
    {
        Assert.True(Money.TryParseAmount(typed, out var amount, English));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("1,5")]      // a decimal comma in English: not fifteen
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("1.255")]    // not whole cents
    public void A_bad_amount_is_refused_rather_than_guessed(string typed)
    {
        Assert.False(Money.TryParseAmount(typed, out _, English));
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("-1")]
    [InlineData("eight")]
    public void A_bad_rate_is_refused(string typed)
    {
        Assert.False(Money.TryParseRate(typed, out _, English));
    }

    [Fact]
    public void Tax_on_ten_dollars_at_8_25_percent_rounds_half_away_from_zero()
    {
        var quote = new QuoteEntity
        {
            TaxRate = 8.25m,
            LineItems = [new LineItemEntity { UnitPrice = 10.00m, Quantity = 1 }],
        };

        Assert.Equal(10.00m, quote.Subtotal);
        Assert.Equal(0.83m, quote.TaxAmount);   // 0.825: half to even would give 0.82
        Assert.Equal(10.83m, quote.Total);
    }

    [Theory]
    [InlineData(200.00, 9.975, 19.95)]
    [InlineData(10.00, 9.975, 1.00)]
    [InlineData(99.99, 13, 13.00)]
    public void Tax_is_rounded_once_to_the_cent(double subtotal, double rate, double tax)
    {
        Assert.Equal((decimal)tax, Money.Tax((decimal)subtotal, (decimal)rate));
    }

    [Fact]
    public void A_catalog_price_with_markup_is_whole_cents()
    {
        Assert.Equal(115.01m, Money.WithMarkup(100.01m, 15m));
    }

    [Fact]
    public void Amounts_show_in_the_currency_of_the_business()
    {
        Assert.Equal("$1,250.00", Money.Format(1250m, "USD"));
        Assert.Equal("$", Money.Symbol("CAD"));
        Assert.Equal("XYZ 1,250.00", Money.Format(1250m, "XYZ"));
    }

    [Theory]
    [InlineData(8.25)]
    [InlineData(9.975)]
    public async Task A_rate_survives_the_database(double value)
    {
        using var test = new TestDatabase();
        var quote = new QuoteEntity { Title = "Rate", QuoteNumber = "QC-2026-0001", TaxRate = (decimal)value };
        await test.Quotes.SaveAsync(quote);

        var saved = await test.Quotes.GetByIdAsync(quote.Id);

        Assert.Equal((decimal)value, saved!.TaxRate);
    }
}
