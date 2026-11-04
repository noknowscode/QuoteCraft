using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The open quote on the Quotes page: its detail and its actions.</summary>
public class QuoteDetailModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The open quote once its states hold their first values (see QuoteEditorModelTests.Create).</summary>
    internal static async Task<QuoteDetailModel> Create(ModelHarness h, QuoteEntity quote)
    {
        h.Board.Open(quote.Id);
        var model = new QuoteDetailModel(quote, h.Navigator, h.Db.Quotes, h.Delivery, h.LineItemDialogs, h.Creation,
            h.FeatureGate, h.Status, h.Board);
        _ = (await model.IsEditMode, await model.EditingNotes, await model.IsPreviewMode, await model.ActionError, await model.UpgradeMessage);
        return model;
    }

    [Fact]
    public async Task The_detail_reads_the_items_and_the_client()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes", "dana@example.com");
        var quote = await h.AddQuoteAsync("Boiler service", QuoteStatus.Sent, dana, items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);

        var model = await Create(h, quote);

        var detail = (await model.Detail)!;
        Assert.Equal(["Boiler service", "Valve"], detail.LineItems.Select(i => i.Description));
        Assert.Equal("dana@example.com", detail.Client?.Email);
        Assert.Equal([false, true], detail.Rows.Select(r => r.IsAlternate));
        var preview = (await model.Preview)!;
        Assert.Equal((400m, quote.QuoteNumber), (preview.Quote.Subtotal, preview.Quote.QuoteNumber));
    }

    [Fact]
    public async Task Delete_asks_first_and_removes_and_closes_the_quote_only_when_confirmed()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await Create(h, quote);

        h.Navigator.Confirm(false);
        await model.DeleteQuote(None);
        Assert.NotNull(await h.Db.Quotes.GetByIdAsync(quote.Id));
        Assert.Equal(quote.Id, h.Board.Current.OpenQuoteId);

        h.Navigator.Confirm(true);
        await model.DeleteQuote(None);
        Assert.Null(await h.Db.Quotes.GetByIdAsync(quote.Id));
        Assert.Null(h.Board.Current.OpenQuoteId);
        Assert.Equal("Delete Quote?", Assert.IsType<ConfirmRequest>(h.Navigator.DataFor("Confirm")).Title);
    }

    [Fact]
    public async Task Duplicate_copies_the_items_into_a_new_draft_and_opens_it()
    {
        using var h = new ModelHarness();
        var dana = await h.AddClientAsync("Dana Reyes");
        var quote = await h.AddQuoteAsync("Boiler service", QuoteStatus.Accepted, dana, taxRate: 8.25m,
            items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        var model = await Create(h, quote);

        await model.DuplicateQuote(None);

        var copy = Assert.IsType<QuoteEntity>(h.Navigator.DataFor("QuoteEditor"));
        var stored = (await h.Db.Quotes.GetByIdAsync(copy.Id))!;
        Assert.Equal(("Boiler service (Copy)", QuoteStatus.Draft, dana.Id, 8.25m), (stored.Title, stored.Status, stored.ClientId, stored.TaxRate));
        Assert.NotEqual(quote.QuoteNumber, stored.QuoteNumber);
        Assert.Equal(["Boiler service", "Valve"], stored.LineItems.Select(i => i.Description));
        Assert.Equal(quote.Total, stored.Total);
    }

    [Fact]
    public async Task At_the_quote_limit_duplicate_shows_the_upgrade_banner()
    {
        using var h = new ModelHarness();
        h.Subscription.IsAvailable.Returns(true);
        h.Subscription.CurrentTier.Returns(SubscriptionTier.Free);
        QuoteEntity quote = null!;
        for (var i = 0; i < 5; i++)
        {
            quote = await h.AddQuoteAsync($"Quote {i}");
        }
        var model = await Create(h, quote);

        await model.DuplicateQuote(None);

        Assert.NotEqual(string.Empty, await model.UpgradeMessage);
        Assert.Equal(5, (await h.Db.Quotes.GetAllAsync()).Count);
        await model.DismissUpgrade(None);
        Assert.Equal(string.Empty, await model.UpgradeMessage);
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
        var model = await Create(h, quote);
        var before = h.Board.Current.Version;

        await model.SendQuote(None);

        await h.Share.Received(1).GenerateAndDownloadPdfAsync(Arg.Is<QuoteEntity>(q => q.Id == quote.Id));
        await h.Email.Received(1).ComposeEmailAsync("dana@example.com", "Quote", "Hi Dana,\n\nLine two.");
        Assert.Equal(QuoteStatus.Sent, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Status);
        Assert.Equal(string.Empty, await model.ActionError);
        Assert.True(h.Board.Current.Version > before);
    }

    [Fact]
    public async Task Send_without_an_email_app_says_so_and_leaves_the_quote_as_it_was()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Navigator.Answer("SendQuote", new SendQuoteChoice(PdfOnly: false, "dana@example.com", "Quote", "Hi"));
        var model = await Create(h, quote);

        await model.SendQuote(None);

        Assert.Equal(QuoteStatus.Draft, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Status);
        Assert.Equal("The quote could not be sent. No email app opened, so the quote is not marked as sent.", await model.ActionError);
    }

    [Fact]
    public async Task Download_pdf_only_saves_the_pdf_and_changes_nothing_else()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Navigator.Answer("SendQuote", new SendQuoteChoice(PdfOnly: true));
        var model = await Create(h, quote);

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
        var model = await Create(h, quote);

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
        var model = await Create(h, quote);
        Assert.True((await model.Detail)!.IsExpired);

        await model.ReopenQuote(None);

        var reopened = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(QuoteStatus.Draft, reopened.Status);
        Assert.True(reopened.ValidUntil > DateTimeOffset.UtcNow);
        Assert.False((await Settled(async () => await model.Detail, d => d?.IsExpired == false))!.IsExpired);
    }

    [Fact]
    public async Task Inline_edit_saves_the_notes_on_done()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await Create(h, quote);

        await model.StartEdit(None);
        Assert.True(await model.IsEditMode);
        await model.EditingNotes.UpdateAsync(_ => "Gate code 4412.", None);
        await model.FinishEdit(None);

        Assert.False(await model.IsEditMode);
        Assert.Equal("Gate code 4412.", (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Notes);
        Assert.Equal("Gate code 4412.", (await Settled(async () => await model.Detail, d => d?.Quote.Notes is not null))!.Quote.Notes);
    }

    [Fact]
    public async Task While_editing_a_new_line_item_goes_last_and_a_deleted_one_goes()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service", items: [("Boiler service", 320m, 1), ("Valve", 40m, 2)]);
        var model = await Create(h, quote);
        var valve = (await h.Db.Quotes.GetLineItemsAsync(quote.Id))[1];

        // Not editing: a tap on a row does nothing.
        await model.EditLineItem(new LineItemRow(valve, IsAlternate: true), None);
        Assert.Empty(h.Navigator.Requests);

        await model.StartEdit(None);
        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(new LineItemEntity { Description = "Flue", UnitPrice = 90m, Quantity = 1 }, null));
        await model.AddLineItem(None);
        Assert.Equal(["Boiler service", "Valve", "Flue"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));

        h.Navigator.Answer("LineItemEditor", new LineItemEditResult(null, valve.Id));
        await model.EditLineItem(new LineItemRow(valve, IsAlternate: true), None);
        Assert.Equal(["Boiler service", "Flue"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));
        Assert.Equal(["Boiler service", "Flue"], (await Settled(async () => await model.Detail, d => d?.LineItems.Count == 2))!.LineItems.Select(i => i.Description));
    }

    [Fact]
    public async Task The_full_editor_opens_the_quote_as_stored_now()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Old title");
        var model = await Create(h, quote);
        var stored = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        stored.Title = "New title";
        await h.Db.Quotes.SaveAsync(stored);

        await model.EditQuote(None);

        Assert.Equal("New title", Assert.IsType<QuoteEntity>(h.Navigator.DataFor("QuoteEditor")).Title);
    }

    [Fact]
    public async Task Preview_and_back_to_detail_switch_the_view()
    {
        using var h = new ModelHarness();
        var model = await Create(h, await h.AddQuoteAsync("Boiler service"));

        await model.PreviewQuote(None);
        Assert.True(await model.IsPreviewMode);
        await model.BackToDetail(None);
        Assert.False(await model.IsPreviewMode);
    }

    [Fact]
    public async Task When_no_app_opens_the_saved_pdf_the_banner_says_where_it_is()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        h.Share.GenerateAndDownloadPdfAsync(default!).ReturnsForAnyArgs("QC-2026-0001.pdf is saved in /data/exports, but no app on this computer opened it.");
        var model = await Create(h, quote);

        await model.DownloadPdf(None);

        Assert.Equal("QC-2026-0001.pdf is saved in /data/exports, but no app on this computer opened it.", await model.ActionError);
    }
}
