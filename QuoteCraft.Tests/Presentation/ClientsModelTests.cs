using NSubstitute;
using QuoteCraft.Models;
using QuoteCraft.Presentation;
using QuoteCraft.Services;
using Uno.Extensions.Reactive;
using Xunit;
using static QuoteCraft.Tests.Presentation.ModelHarness;

namespace QuoteCraft.Tests.Presentation;

/// <summary>The client directory: the list and search, the detail, and the client and quote actions.</summary>
public class ClientsModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The page once its selection and search hold their first values (see QuoteEditorModelTests.Create).</summary>
    private static async Task<ClientsModel> Create(ModelHarness h)
    {
        var model = new ClientsModel(h.Navigator, h.Db.Clients, h.Db.Quotes, h.Db.Profiles, h.Numbers, h.FeatureGate);
        _ = (await model.SelectedClient, await model.SearchText, await model.Version);
        return model;
    }

    [Fact]
    public async Task The_list_carries_each_clients_quotes_and_city_and_searches_name_and_city()
    {
        using var h = new ModelHarness();
        var bob = await h.AddClientAsync("Bob Johnson", address: "12 Elm Street\nRiverton 40210");
        await h.AddClientAsync("Sarah Williams", address: "7 Harbour Road, Lakeside");
        await h.AddQuoteAsync("Sink", QuoteStatus.Accepted, bob, items: ("Sink", 350m, 1));
        await h.AddQuoteAsync("Pipe", QuoteStatus.Draft, bob, items: ("Pipe", 12.5m, 4));
        var model = await Create(h);

        var row = (await model.Clients).Single(c => c.Name == "Bob Johnson");
        Assert.Equal(("BJ", 2, "Riverton", 400m), (row.Initials, row.QuoteCount, row.City, row.TotalValue));

        await model.SearchText.UpdateAsync(_ => "lakeside", None);
        Assert.Equal(["Sarah Williams"], (await Settled(async () => await model.Clients, l => l.Count == 1)).Select(c => c.Name));

        var analytics = (await model.Analytics)!;
        Assert.Equal((2, 350m, 175m), (analytics.TotalClients, analytics.TotalRevenue, analytics.AvgPerClient));
    }

    [Fact]
    public async Task The_detail_lists_the_clients_quotes()
    {
        using var h = new ModelHarness();
        var bob = await h.AddClientAsync("Bob Johnson");
        await h.AddQuoteAsync("Sink", client: bob);
        await h.AddQuoteAsync("Someone else's");
        var model = await Create(h);

        await model.SelectClient((await model.Clients).Single(), None);

        Assert.Equal(["Sink"], (await model.SelectedClientDetail)!.Quotes.Select(q => q.Title));
    }

    [Fact]
    public async Task Add_client_opens_an_empty_client_dialog_and_the_list_reads_the_new_client()
    {
        using var h = new ModelHarness();
        var model = await Create(h);
        h.Navigator.Answer("ClientEditor", data =>
        {
            var request = Assert.IsType<ClientEditRequest>(data);
            Assert.True(request.IsNew);
            request.Client.Name = "Dana Reyes";
            h.Db.Clients.SaveAsync(request.Client).Wait();
            return request.Client;
        });

        await model.AddClient(None);

        Assert.Equal(["Dana Reyes"], (await Settled(async () => await ListOf(model.Clients), l => l.Count == 1)).Select(c => c.Name));
    }

    [Fact]
    public async Task At_the_client_limit_add_offers_the_upgrade_instead()
    {
        using var h = new ModelHarness();
        h.Subscription.IsAvailable.Returns(true);
        h.Subscription.CurrentTier.Returns(SubscriptionTier.Free);
        for (var i = 0; i < 10; i++)
        {
            await h.AddClientAsync($"Client {i}");
        }
        h.Navigator.Confirm(true);

        await (await Create(h)).AddClient(None);

        Assert.Equal(["!Confirm", "Settings"], h.Navigator.Routes);
        Assert.Equal("Client Limit Reached", Assert.IsType<ConfirmRequest>(h.Navigator.DataFor("Confirm")).Title);
    }

    [Fact]
    public async Task Edit_opens_the_client_as_stored()
    {
        using var h = new ModelHarness();
        await h.AddClientAsync("Bob Johnson", "bob@example.com");
        var model = await Create(h);
        await model.SelectClient((await model.Clients).Single(), None);

        await model.EditClient(None);

        var request = Assert.IsType<ClientEditRequest>(h.Navigator.DataFor("ClientEditor"));
        Assert.Equal(("Bob Johnson", "bob@example.com", false), (request.Client.Name, request.Client.Email, request.IsNew));
    }

    [Fact]
    public async Task Delete_asks_then_removes_the_client_and_closes_the_detail()
    {
        using var h = new ModelHarness();
        var bob = await h.AddClientAsync("Bob Johnson");
        var model = await Create(h);
        await model.SelectClient((await model.Clients).Single(), None);
        h.Navigator.Confirm(true);

        await model.DeleteClient(None);

        Assert.Null(await h.Db.Clients.GetByIdAsync(bob.Id));
        Assert.Null(await model.SelectedClient);
    }

    [Fact]
    public async Task New_quote_for_a_client_creates_a_linked_draft_and_opens_it()
    {
        using var h = new ModelHarness();
        var bob = await h.AddClientAsync("Bob Johnson");
        var model = await Create(h);
        await model.SelectClient((await model.Clients).Single(), None);

        await model.CreateQuoteForClient(None);

        var quote = Assert.IsType<QuoteEntity>(h.Navigator.DataFor("QuoteEditor"));
        var stored = (await h.Db.Quotes.GetByIdAsync(quote.Id))!;
        Assert.Equal(("New Quote", QuoteStatus.Draft, bob.Id, "Bob Johnson"), (stored.Title, stored.Status, stored.ClientId, stored.ClientName));
    }

    [Fact]
    public async Task A_quote_row_opens_the_quote_in_the_editor()
    {
        using var h = new ModelHarness();
        var quote = await h.AddQuoteAsync("Sink");

        await (await Create(h)).OpenQuote(quote, None);

        Assert.Equal(["QuoteEditor"], h.Navigator.Routes);
        Assert.Same(quote, h.Navigator.DataFor("QuoteEditor"));
    }
}
