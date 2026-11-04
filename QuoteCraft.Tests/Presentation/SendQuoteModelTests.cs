using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;

namespace QuoteCraft.Tests.Presentation;

/// <summary>Send Quote: the message it starts from, and the choice it returns.</summary>
public class SendQuoteModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly QuoteSendDetails Quote = new("q-1", "QC-2026-0002", "Drain cleaning", "Sarah Williams",
        "sarah@williams.example", "Northside Plumbing & Heating", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_message_starts_from_the_quote_the_client_and_the_business()
    {
        using var h = new ModelHarness();
        var model = new SendQuoteModel(h.Navigator, Quote);

        Assert.Equal("Send QC-2026-0002 to Sarah Williams", model.Heading);
        Assert.Equal(("sarah@williams.example", "Quote QC-2026-0002 - Drain cleaning"), (await model.Recipient, await model.Subject));
        var body = await model.Body;
        Assert.StartsWith("Hi Sarah Williams,", body);
        Assert.Contains("valid until December 1, 2026.", body);
        Assert.EndsWith("Best regards,\nNorthside Plumbing & Heating", body);
    }

    [Fact]
    public async Task A_quote_without_a_client_or_a_date_still_reads()
    {
        using var h = new ModelHarness();
        var model = new SendQuoteModel(h.Navigator, Quote with { ClientName = null, ValidUntil = null });

        Assert.Equal("Send QC-2026-0002 to client", model.Heading);
        var body = await model.Body;
        Assert.StartsWith("Hi there,", body);
        Assert.Contains("valid until further notice.", body);
    }

    [Fact]
    public async Task Send_by_email_returns_the_edited_message_trimmed()
    {
        using var h = new ModelHarness();
        var model = new SendQuoteModel(h.Navigator, Quote);
        _ = (await model.Recipient, await model.Subject, await model.Body);

        await model.Recipient.UpdateAsync(_ => " office@williams.example ", None);
        await model.Body.UpdateAsync(_ => "See the attached quote.\n", None);
        await model.SendByEmail(None);

        var choice = Assert.IsType<SendQuoteChoice>(h.Navigator.BackResult);
        Assert.Equal((false, "office@williams.example", "Quote QC-2026-0002 - Drain cleaning", "See the attached quote."),
            (choice.PdfOnly, choice.Recipient, choice.Subject, choice.Body));
    }

    [Fact]
    public async Task Download_pdf_only_returns_that_choice()
    {
        using var h = new ModelHarness();
        var model = new SendQuoteModel(h.Navigator, Quote);

        await model.DownloadPdfOnly(None);

        Assert.True(Assert.IsType<SendQuoteChoice>(h.Navigator.BackResult).PdfOnly);
    }
}
