using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The full editor: fields, totals as the rate is typed, line items, status changes and leaving.</summary>
public class QuoteEditorModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>
    /// The editor once its fields hold the quote. On the page the bindings read every state long before anyone types;
    /// reading them here first does the same, so an update is never made before a state has its first value.
    /// </summary>
    internal static async Task<QuoteEditorModel> Create(ModelHarness h, QuoteEntity quote)
    {
        var model = new QuoteEditorModel(quote, h.Navigator, h.Db.Quotes, h.Db.Clients, h.Share, h.Photos, h.Db.History, h.Status, h.Dispatcher, h.Board);
        _ = (await model.Title, await model.Notes, await model.TaxRate, await model.ClientName, await model.ValidationError);
        return model;
    }

    [Fact]
    public async Task Save_writes_the_fields_links_the_named_client_and_goes_back()
    {
        using var h = new ModelHarness();
        var mike = await h.AddClientAsync("Mike Chen", "mike@example.com");
        var quote = await h.AddQuoteAsync("Service call", taxRate: 13m);
        var model = await Create(h, quote);

        await model.Title.UpdateAsync(_ => "Service call - furnace", None);
        await model.TaxRate.UpdateAsync(_ => "8.25", None);
        await model.ClientName.UpdateAsync(_ => "Mike Chen", None);
        await model.Notes.UpdateAsync(_ => "Gate code 4412.", None);
        await model.Save(None);

        var saved = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(("Service call - furnace", 8.25m, mike.Id, "Mike Chen", "Gate code 4412."),
            (saved.Title, saved.TaxRate, saved.ClientId, saved.ClientName, saved.Notes));
        Assert.Equal(["-"], h.Navigator.Routes);
        Assert.Equal(string.Empty, await model.ValidationError);
    }

    [Theory]
    [InlineData("  ", "13", "Quote title is required.")]
    [InlineData("Service call", "abc", "Tax rate must be a number between 0 and 100.")]
    [InlineData("Service call", "101", "Tax rate must be a number between 0 and 100.")]
    public async Task Save_refuses_what_does_not_read_and_says_why(string title, string rate, string message)
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call", taxRate: 13m);
        var model = await Create(h, quote);

        await model.Title.UpdateAsync(_ => title, None);
        await model.TaxRate.UpdateAsync(_ => rate, None);
        await model.Save(None);

        Assert.Equal(message, await model.ValidationError);
        Assert.Empty(h.Navigator.Requests);
        Assert.Equal(("Service call", 13m), ((await h.Db.Quotes.GetByIdAsync(quote.Id))!.Title, (await h.Db.Quotes.GetByIdAsync(quote.Id))!.TaxRate));
    }

    [Fact]
    public async Task Totals_follow_the_rate_as_it_is_typed()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call", taxRate: 13m, items: [("Panel", 1000m, 1), ("Labor", 455m, 2)]);
        var model = await Create(h, quote);

        var totals = (await model.Totals)!;
        Assert.Equal((1910m, "Tax (13 %)", 248.30m, 2158.30m), (totals.Subtotal, totals.TaxLabel, totals.Tax, totals.Total));

        await model.TaxRate.UpdateAsync(_ => "9.975", None);
        totals = (await Settled(async () => await model.Totals, t => t?.TaxLabel == "Tax (9.975 %)"))!;
        Assert.Equal(190.52m, totals.Tax);

        await model.TaxRate.UpdateAsync(_ => "abc", None);
        totals = (await Settled(async () => await model.Totals, t => t?.Tax == 0m))!;
        Assert.Equal(("Tax (enter a rate from 0 to 100)", 1910m), (totals.TaxLabel, totals.Total));
    }

    [Fact]
    public async Task Back_without_changes_leaves_at_once()
    {
        using var h = new ModelHarness();
        var model = await Create(h, await h.AddQuoteAsync("Service call"));

        await model.Back(None);

        Assert.Equal(["-"], h.Navigator.Routes);
    }

    [Fact]
    public async Task Back_with_unsaved_changes_asks_and_stay_keeps_the_editor_open()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h, quote);
        await model.Title.UpdateAsync(_ => "Changed", None);

        h.Navigator.Confirm(false);
        await model.Back(None);
        Assert.Equal(["!Confirm"], h.Navigator.Routes);

        h.Navigator.Confirm(true);
        await model.Back(None);
        Assert.Equal(["!Confirm", "!Confirm", "-"], h.Navigator.Routes);
        Assert.Equal("Service call", (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Title);
    }

    [Fact]
    public async Task Mark_sent_keeps_the_edits_and_records_the_change()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h, quote);
        await model.Title.UpdateAsync(_ => "Service call - furnace", None);

        await model.MarkSent(None);

        var saved = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(("Service call - furnace", QuoteStatus.Sent), (saved.Title, saved.Status));
        Assert.Equal(["Sent"], (await h.Db.History.GetByQuoteIdAsync(quote.Id)).Select(e => e.Status));
    }

    [Fact]
    public async Task Moving_an_item_swaps_it_with_its_neighbour()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call", items: [("A", 1m, 1), ("B", 1m, 1), ("C", 1m, 1)]);
        var model = await Create(h, quote);
        var items = await h.Db.Quotes.GetLineItemsAsync(quote.Id);

        await model.MoveLineItemDown(items[0], None);
        await model.MoveLineItemUp(items[2], None);
        await model.MoveLineItemUp((await h.Db.Quotes.GetLineItemsAsync(quote.Id))[0], None);

        Assert.Equal(["B", "C", "A"], (await h.Db.Quotes.GetLineItemsAsync(quote.Id)).Select(i => i.Description));
    }

    [Fact]
    public async Task Export_pdf_saves_the_edits_first_and_reports_a_failure()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h, quote);
        await model.Notes.UpdateAsync(_ => "Bring the ladder.", None);

        await model.ExportPdf(None);
        await h.Share.Received(1).GenerateAndDownloadPdfAsync(Arg.Is<QuoteEntity>(q => q.Notes == "Bring the ladder."));

        h.Share.GenerateAndDownloadPdfAsync(default!).ThrowsAsyncForAnyArgs(new IOException("disk full"));
        await model.ExportPdf(None);
        Assert.Equal("The PDF could not be created. Try again.", await model.ValidationError);
    }

    [Fact]
    public async Task Client_suggestions_match_what_is_typed_and_leave_out_an_exact_match()
    {
        using var h = new ModelHarness();
        await h.AddClientAsync("Mike Chen");
        await h.AddClientAsync("Michelle Ortiz");
        await h.AddClientAsync("Dana Reyes");
        var model = await Create(h, await h.AddQuoteAsync("Service call"));

        await model.ClientName.UpdateAsync(_ => "mi", None);
        Assert.Equal(["Michelle Ortiz", "Mike Chen"], (await Settled(async () => await model.ClientSuggestions, l => l.Count == 2)).Order());

        await model.ClientName.UpdateAsync(_ => "Mike Chen", None);
        Assert.Empty(await Settled(async () => await ListOf(model.ClientSuggestions), l => l.Count == 0));
    }

    [Fact]
    public async Task Catalog_items_added_in_the_browser_show_when_it_closes()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h, quote);
        Assert.Empty(await ListOf(model.LineItems));
        h.Navigator.Answer("CatalogBrowser", data =>
        {
            Assert.Equal(quote.Id, Assert.IsType<CatalogBrowserRequest>(data).QuoteId);
            h.Db.Quotes.SaveLineItemAsync(new LineItemEntity { QuoteId = quote.Id, Description = "Sink Installation", UnitPrice = 350m }).Wait();
            return null;
        });

        await model.OpenCatalog(None);

        Assert.Equal(["Sink Installation"], (await Settled(async () => await model.LineItems, l => l.Count == 1)).Select(i => i.Description));
    }

    [Fact]
    public async Task Back_after_export_pdf_saved_the_edits_does_not_ask()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h, quote);
        await model.Notes.UpdateAsync(_ => "Bring the ladder.", None);

        await model.ExportPdf(None);
        await model.Back(None);

        Assert.Equal(["-"], h.Navigator.Routes);
        Assert.Equal("Bring the ladder.", (await h.Db.Quotes.GetByIdAsync(quote.Id))!.Notes);
    }

    [Fact]
    public async Task When_no_app_opens_the_exported_pdf_the_message_says_where_it_is()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        h.Share.GenerateAndDownloadPdfAsync(default!).ReturnsForAnyArgs("QC-2026-0001.pdf is saved in /data/exports, but no app on this computer opened it.");
        var model = await Create(h, quote);

        await model.ExportPdf(None);

        Assert.StartsWith("QC-2026-0001.pdf is saved in", await model.ValidationError);
    }
}
