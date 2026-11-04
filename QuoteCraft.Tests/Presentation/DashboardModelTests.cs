using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The Quotes page: the list, its counts and analytics, and the open quote's actions.</summary>
public class DashboardModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The page once its states hold their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<DashboardModel> Create(ModelHarness h)
    {
        var model = new DashboardModel(h.Navigator, h.Db.Quotes, h.Db.Clients, h.Db.Profiles, h.Creation,
            h.Share, h.FeatureGate, h.Status, h.Email, NullLogger<DashboardModel>.Instance);
        _ = (await model.SelectedFilter, await model.SearchText, await model.SelectedQuote, await model.SelectedQuoteId,
            await model.IsEditMode, await model.EditingNotes, await model.IsPreviewMode, await model.DetailVersion,
            await model.ActionError, await model.UpgradeMessage);
        return model;
    }

    private static async Task<DashboardModel> OpenAsync(ModelHarness h, QuoteEntity quote)
    {
        var model = await Create(h);
        await model.OpenQuote(new QuoteListItem(quote, IsSelected: false), None);
        return model;
    }

    [Fact]
    public async Task The_list_filters_by_status_and_searches_title_client_and_number()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes");
        await h.AddQuoteAsync("Kitchen sink", QuoteStatus.Draft);
        var boiler = await h.AddQuoteAsync("Boiler service", QuoteStatus.Sent, dana);
        var model = await Create(h);

        Assert.Equal(2, (await model.Quotes).Count);

        await model.SelectedFilter.UpdateAsync(_ => "Sent", None);
        var sent = await Settled(async () => await model.Quotes, l => l.Count == 1);
        Assert.Equal(["Boiler service"], sent.Select(q => q.Quote.Title));

        await model.SelectedFilter.UpdateAsync(_ => "All", None);
        await model.SearchText.UpdateAsync(_ => "dana", None);
        Assert.Equal(["Boiler service"], (await Settled(async () => await model.Quotes, l => l.Count == 1)).Select(q => q.Quote.Title));

        await model.SearchText.UpdateAsync(_ => boiler.QuoteNumber, None);
        Assert.Equal(["Boiler service"], (await Settled(async () => await model.Quotes, l => l.Count == 1)).Select(q => q.Quote.Title));
    }

    [Fact]
    public async Task Counts_and_acceptance_rate_come_from_every_quote()
    {
        using var h = new ModelHarness();
        await h.AddQuoteAsync("A", QuoteStatus.Accepted, items: ("Work", 100m, 1));
        await h.AddQuoteAsync("B", QuoteStatus.Accepted, items: ("Work", 50m, 2));
        await h.AddQuoteAsync("C", QuoteStatus.Declined);
        await h.AddQuoteAsync("D", QuoteStatus.Draft, items: ("Work", 10m, 1));
        var model = await Create(h);

        var counts = (await model.StatusCounts)!;
        Assert.Equal((4, 1, 2, 1), (counts.All, counts.Draft, counts.Accepted, counts.Declined));
        Assert.Equal(4, await model.TotalCount);

        var analytics = (await model.Analytics)!;
        Assert.Equal(66.7, analytics.AcceptanceRate);
        Assert.Equal(210m, analytics.TotalQuotedThisMonth);
        Assert.Equal(3, analytics.QuotesSentThisMonth);
    }

    [Fact]
    public async Task Opening_a_quote_selects_its_card_and_reads_its_items_and_client()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes", "dana@example.com");
        var quote = await h.AddQuoteAsync("Boiler service", QuoteStatus.Sent, dana, items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        await h.AddQuoteAsync("Other");

        var model = await OpenAsync(h, quote);

        var detail = (await model.SelectedQuoteDetail)!;
        Assert.Equal(["Boiler service", "Valve"], detail.LineItems.Select(i => i.Description));
        Assert.Equal("dana@example.com", detail.Client?.Email);
        Assert.Equal([false, true], detail.Rows.Select(r => r.IsAlternate));
        var list = await Settled(async () => await model.Quotes, l => l.Any(i => i.IsSelected));
        Assert.Equal(quote.Id, list.Single(i => i.IsSelected).Quote.Id);
    }

    [Fact]
    public async Task New_quote_opens_the_dialog_then_the_editor_with_the_created_quote()
    {
        using var h = new ModelHarness();
        var created = new QuoteEntity { Title = "Created" };
        h.Navigator.Answer("NewQuote", created);

        await (await Create(h)).CreateQuote(None);

        Assert.Equal(["!NewQuote", "QuoteEditor"], h.Navigator.Routes);
        Assert.Same(created, h.Navigator.DataFor("QuoteEditor"));
    }

    [Fact]
    public async Task Cancelling_new_quote_stays_on_the_list()
    {
        using var h = new ModelHarness();

        await (await Create(h)).CreateQuote(None);

        Assert.Equal(["!NewQuote"], h.Navigator.Routes);
    }

    [Fact]
    public async Task Delete_asks_first_and_removes_the_quote_only_when_confirmed()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await OpenAsync(h, quote);

        h.Navigator.Confirm(false);
        await model.DeleteQuote(None);
        Assert.NotNull(await h.Db.Quotes.GetByIdAsync(quote.Id));

        h.Navigator.Confirm(true);
        await model.DeleteQuote(None);
        Assert.Null(await h.Db.Quotes.GetByIdAsync(quote.Id));
        Assert.Empty(await Settled(async () => await model.Quotes, l => l.Count == 0));
    }

    [Fact]
    public async Task Duplicate_copies_the_items_into_a_new_draft_and_opens_it()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes");
        var quote = await h.AddQuoteAsync("Boiler service", QuoteStatus.Accepted, dana, taxRate: 8.25m,
            items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        var model = await OpenAsync(h, quote);

        await model.DuplicateQuote(None);

        var copy = Assert.IsType<QuoteEntity>(h.Navigator.DataFor("QuoteEditor"));
        var stored = (await h.Db.Quotes.GetByIdAsync(copy.Id))!;
        Assert.Equal(("Boiler service (Copy)", QuoteStatus.Draft, dana.Id, 8.25m), (stored.Title, stored.Status, stored.ClientId, stored.TaxRate));
        Assert.NotEqual(quote.QuoteNumber, stored.QuoteNumber);
        Assert.Equal(["Boiler service", "Valve"], stored.LineItems.Select(i => i.Description));
        Assert.Equal(quote.Total, stored.Total);
    }

    [Fact]
    public async Task Send_saves_the_pdf_opens_the_email_with_the_whole_message_and_marks_the_quote_sent()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes", "dana@example.com");
        var quote = await h.AddQuoteAsync("Boiler service", QuoteStatus.Draft, dana, items: ("Boiler service", 320m, 1));
        h.Navigator.Answer("SendQuote", data =>
        {
            var details = Assert.IsType<QuoteSendDetails>(data);
            Assert.Equal(("dana@example.com", quote.QuoteNumber), (details.ClientEmail, details.QuoteNumber));
            return new SendQuoteChoice(PdfOnly: false, "dana@example.com", "Quote", "Hi Dana,\n\nLine two.");
        });
        h.Email.ComposeEmailAsync(default!, default!, default!).ReturnsForAnyArgs(true);
        var model = await OpenAsync(h, quote);

        await model.SendQuote(None);

        await h.Share.Received(1).GenerateAndDownloadPdfAsync(Arg.Is<QuoteEntity>(q => q.Id == quote.Id));
        await h.Email.Received(1).ComposeEmailAsync("dana@example.com", "Quote", "Hi Dana,\n\nLine two.");
        Assert.Equal(QuoteStatus.Sent, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Status);
        Assert.Equal(string.Empty, await model.ActionError);
    }

    [Fact]
    public async Task Send_without_an_email_app_says_so_and_leaves_the_quote_as_it_was()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Navigator.Answer("SendQuote", new SendQuoteChoice(PdfOnly: false, "dana@example.com", "Quote", "Hi"));
        var model = await OpenAsync(h, quote);

        await model.SendQuote(None);

        Assert.Equal(QuoteStatus.Draft, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Status);
        Assert.Contains("No email app opened", await model.ActionError);
    }

    [Fact]
    public async Task Download_pdf_only_saves_the_pdf_and_changes_nothing_else()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Navigator.Answer("SendQuote", new SendQuoteChoice(PdfOnly: true));
        var model = await OpenAsync(h, quote);

        await model.SendQuote(None);

        await h.Share.Received(1).GenerateAndDownloadPdfAsync(Arg.Any<QuoteEntity>());
        await h.Email.DidNotReceiveWithAnyArgs().ComposeEmailAsync(default!, default!, default!);
        Assert.Equal(QuoteStatus.Draft, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Status);
    }

    [Fact]
    public async Task A_pdf_that_cannot_be_written_shows_in_the_banner()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Share.GenerateAndDownloadPdfAsync(default!).ThrowsAsyncForAnyArgs(new InvalidOperationException("Close the PDF in the other app."));
        var model = await OpenAsync(h, quote);

        await model.DownloadPdf(None);

        Assert.Equal("The PDF could not be created. Close the PDF in the other app.", await model.ActionError);
        await model.DismissError(None);
        Assert.Equal(string.Empty, await model.ActionError);
    }

    [Fact]
    public async Task Reopen_makes_an_expired_quote_a_draft_again()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Old", QuoteStatus.Expired, validUntil: DateTimeOffset.UtcNow.AddDays(-3));
        var model = await OpenAsync(h, quote);

        await model.ReopenQuote(None);

        var reopened = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(QuoteStatus.Draft, reopened.Status);
        Assert.True(reopened.ValidUntil > DateTimeOffset.UtcNow);
        Assert.False((await Settled(async () => await model.SelectedQuoteDetail, d => d?.IsExpired == false))!.IsExpired);
    }

    [Fact]
    public async Task Inline_edit_saves_the_notes_on_done()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await OpenAsync(h, quote);

        await model.StartEdit(None);
        Assert.True(await model.IsEditMode);
        await model.EditingNotes.UpdateAsync(_ => "Gate code 4412.", None);
        await model.FinishEdit(None);

        Assert.False(await model.IsEditMode);
        Assert.Equal("Gate code 4412.", (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Notes);
    }

    [Fact]
    public async Task While_editing_a_new_line_item_goes_last_and_a_deleted_one_goes()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service", items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        var model = await OpenAsync(h, quote);
        await model.StartEdit(None);

        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(new LineItemEntity { Description = "Flue", UnitPrice = 90m, Quantity = 1 }, null));
        await model.AddLineItem(None);
        Assert.Equal(["Boiler service", "Valve", "Flue"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));

        var valve = (await h.Db.Quotes.GetLineItemsAsync(quote.Id))[1];
        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(null, valve.Id));
        await model.EditLineItem(new LineItemRow(valve, IsAlternate: true), None);
        Assert.Equal(["Boiler service", "Flue"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));
    }

    [Fact]
    public async Task Back_to_list_closes_the_quote()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await OpenAsync(h, quote);

        await model.BackToList(None);

        Assert.Null(await model.SelectedQuote);
        Assert.DoesNotContain(await Settled(async () => await model.Quotes, l => !l.Any(i => i.IsSelected)), i => i.IsSelected);
    }
}
