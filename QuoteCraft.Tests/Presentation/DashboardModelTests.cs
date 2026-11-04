using Microsoft.Extensions.Logging.Abstractions;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using QuoteCraft.Tests.Data;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The Quotes list: search, status filter, counts and analytics, New Quote, and opening a quote.</summary>
public class DashboardModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The page once its states hold their first values (see QuoteEditorModelTests.Create).</summary>
    internal static async Task<DashboardModel> Create(ModelHarness h)
    {
        var model = new DashboardModel(h.Navigator, h.Db.Quotes, h.FeatureGate, h.Status, h.Board);
        _ = (await model.SelectedFilter, await model.SearchText, await model.ActionError);
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
        Assert.Equal(210d, analytics.QuotedDailyValues[^1]);
    }

    [Fact]
    public async Task A_viewed_quote_from_an_older_install_counts_and_filters_as_sent()
    {
        using var h = new ModelHarness();
        await h.AddQuoteAsync("Boiler service", QuoteStatus.Sent);
        await h.AddQuoteAsync("Office fit-out", QuoteStatus.Viewed);
        await h.AddQuoteAsync("Draft", QuoteStatus.Draft);
        var model = await Create(h);

        Assert.DoesNotContain("Viewed", await ListOf(model.FilterNames));
        Assert.Equal((3, 2), ((await model.StatusCounts)!.All, (await model.StatusCounts)!.Sent));

        await model.SelectedFilter.UpdateAsync(_ => "Sent", None);
        var sent = await Settled(async () => await model.Quotes, l => l.Count == 2);
        Assert.Equal(["Boiler service", "Office fit-out"], sent.Select(q => q.Quote.Title).Order());
    }

    [Fact]
    public async Task Opening_a_quote_shows_it_in_the_detail_region_and_selects_its_card()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        await h.AddQuoteAsync("Other");
        var model = await Create(h);
        Assert.False(await model.IsQuoteOpen);

        await model.OpenQuote(new QuoteListItem(quote, IsSelected: false), None);

        Assert.Equal(["./QuoteDetail"], h.Navigator.Routes);
        Assert.Same(quote, h.Navigator.DataFor("QuoteDetail"));
        Assert.True(await Settled(async () => await model.IsQuoteOpen, open => open));
        var list = await Settled(async () => await model.Quotes, l => l.Any(i => i.IsSelected));
        Assert.Equal(quote.Id, list.Single(i => i.IsSelected).Quote.Id);
    }

    [Fact]
    public async Task Closing_the_quote_clears_the_selection()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        var model = await Create(h);
        await model.OpenQuote(new QuoteListItem(quote, IsSelected: false), None);
        var detail = await QuoteDetailModelTests.Create(h, quote);

        await detail.BackToList(None);

        Assert.False(await Settled(async () => await model.IsQuoteOpen, open => !open));
        Assert.DoesNotContain(await Settled(async () => await model.Quotes, l => !l.Any(i => i.IsSelected)), i => i.IsSelected);
    }

    [Fact]
    public async Task A_deleted_quote_leaves_the_list()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Boiler service");
        await h.AddQuoteAsync("Other");
        var model = await Create(h);
        Assert.Equal(2, (await model.Quotes).Count);
        var detail = await QuoteDetailModelTests.Create(h, quote);
        h.Navigator.Confirm(true);

        await detail.DeleteQuote(None);

        Assert.Equal(["Other"], (await Settled(async () => await model.Quotes, l => l.Count == 1)).Select(i => i.Quote.Title));
        Assert.Equal(1, await Settled(async () => await model.TotalCount, n => n == 1));
    }

    [Fact]
    public async Task A_save_in_the_editor_shows_in_the_list()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Service call");
        var model = await Create(h);
        Assert.Equal("Service call", (await model.Quotes).Single().Quote.Title);
        var editor = await QuoteEditorModelTests.Create(h, quote);

        await editor.Title.UpdateAsync(_ => "Service call - furnace", None);
        await editor.Save(None);

        Assert.Equal("Service call - furnace",
            (await Settled(async () => await model.Quotes, l => l.Single().Quote.Title != "Service call")).Single().Quote.Title);
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
    public async Task A_new_page_starts_with_no_quote_open()
    {
        using var h = new ModelHarness();
        h.Board.Open("left over");

        var model = await Create(h);

        Assert.Null(h.Board.Current.OpenQuoteId);
        Assert.False(await model.IsQuoteOpen);
    }

    [Fact]
    public async Task The_first_load_expires_overdue_quotes_once_while_the_expiry_check_runs_too()
    {
        using var h = new ModelHarness();
        var overdue = await h.AddQuoteAsync("Boiler", QuoteStatus.Sent, validUntil: DateTimeOffset.UtcNow.AddDays(-1));
        // One status service, as the app registers it, with reads slow enough for the five runs to overlap.
        var status = new QuoteStatusService(new SlowExpiryReads(h.Db.Quotes), h.Db.History, h.Db.Profiles);
        var expiry = new QuoteExpiryService(h.Db.Quotes, status, h.Notifications, NullLogger<QuoteExpiryService>.Instance);
        var model = new DashboardModel(h.Navigator, h.Db.Quotes, h.FeatureGate, status, h.Board);
        _ = (await model.SelectedFilter, await model.SearchText, await model.ActionError);

        // The list, the total, the counts and the analytics all ask on the page's first load; the timer checks at launch.
        await Task.WhenAll(
            Task.Run(async () => await model.Quotes),
            Task.Run(async () => await model.TotalCount),
            Task.Run(async () => await model.StatusCounts),
            Task.Run(async () => await model.Analytics),
            Task.Run(expiry.CheckExpirationsAsync));

        Assert.Equal(1, (await model.StatusCounts)!.Expired);
        Assert.Equal(["Expired"], (await h.Db.History.GetByQuoteIdAsync(overdue.Id)).Select(e => e.Status));
    }
}
