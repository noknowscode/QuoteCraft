using System.Globalization;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>The Settings preview quote, worked out from the defaults as typed.</summary>
public class SettingsPreviewTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Prices_carry_the_markup_and_tax_is_the_default_rate_on_the_subtotal()
    {
        var preview = SettingsPreview.From("QC-", "8.25", "10", "14", "USD", Today);

        Assert.Equal(["$13.75", "$82.50", "$4.95"], preview.Lines.Select(l => l.UnitPrice));
        Assert.Equal(("$507.10", "Tax (8.25 %)", "$41.84", "$548.94"), (preview.Subtotal, preview.TaxLabel, preview.Tax, preview.Total));
        Assert.Equal(("QC-2026-0001", "USD"), (preview.QuoteNumber, preview.Currency));
        Assert.Equal(Today.AddDays(14).ToString("MMM dd, yyyy", CultureInfo.CurrentCulture), preview.ValidUntil);
    }

    [Fact]
    public void A_rate_or_validity_that_does_not_read_is_left_out_rather_than_guessed()
    {
        var preview = SettingsPreview.From("", "abc", "", "0", "", Today);

        Assert.Equal(("Tax (enter a rate from 0 to 100)", "$0.00", "$461.00", "—"), (preview.TaxLabel, preview.Tax, preview.Total, preview.ValidUntil));
        Assert.Equal("QC-2026-0001", preview.QuoteNumber);
    }

    [Fact]
    public void Amounts_are_shown_in_the_currency_typed()
    {
        var preview = SettingsPreview.From("QC-", "0", "0", "14", "eur", Today);

        Assert.Equal("EUR", preview.Currency);
        Assert.Equal(Money.Format(461m, "EUR"), preview.Total);
    }
}
