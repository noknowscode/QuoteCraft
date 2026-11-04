using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuoteCraft.Data;
using QuoteCraft.Helpers;
using QuoteCraft.Models;
using QuoteCraft.Services;
using QuoteCraft.Tests.Data;

namespace QuoteCraft.Tests.Presentation;

/// <summary>
/// What a page model gets from the app's container, over a real temporary database (<see cref="TestDatabase"/>):
/// the repositories and services the app registers, with fakes only where the app would reach outside (the window's
/// navigator, the PDF hand-off, the email app, the theme service).
/// </summary>
public sealed class ModelHarness : IDisposable
{
    public ModelHarness()
    {
        // In the app, model code runs on background threads, not on a UI context. xUnit's async context serializes
        // continuations on a few threads, and a state update racing the state's first value there could be dropped.
        SynchronizationContext.SetSynchronizationContext(null);
        Db = new TestDatabase();
        Catalog = new CatalogItemRepository(Db.Database);
        Status = new QuoteStatusService(Db.Quotes, Db.History, Db.Profiles);
        Numbers = new QuoteNumberGenerator(Db.Quotes, Db.Profiles);
        Subscription = Substitute.For<ISubscriptionService>();
        FeatureGate = new FeatureGateService(Db.Quotes, Db.Clients, Subscription);
        Notifications = new NotificationService(Db.Database, NullLogger<NotificationService>.Instance);
    }

    public TestDatabase Db { get; }
    public FakeNavigator Navigator { get; } = new();
    public CatalogItemRepository Catalog { get; }
    public QuoteStatusService Status { get; }
    public QuoteNumberGenerator Numbers { get; }
    public ISubscriptionService Subscription { get; }
    public FeatureGateService FeatureGate { get; }
    public NotificationService Notifications { get; }
    public IShareService Share { get; } = Substitute.For<IShareService>();
    public IEmailLauncherService Email { get; } = Substitute.For<IEmailLauncherService>();
    public IPhotoService Photos { get; } = Substitute.For<IPhotoService>();
    public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();
    public ThemeChoiceNotifier ThemeChoice { get; } = new();

    /// <summary>A client in the database.</summary>
    public async Task<ClientEntity> AddClientAsync(string name, string? email = null, string? address = null)
    {
        var client = new ClientEntity { Name = name, Email = email, Address = address };
        await Db.Clients.SaveAsync(client);
        return client;
    }

    /// <summary>A quote in the database with the given items (description, unit price, quantity).</summary>
    public async Task<QuoteEntity> AddQuoteAsync(string title, QuoteStatus status = QuoteStatus.Draft, ClientEntity? client = null,
        decimal taxRate = 0m, DateTimeOffset? validUntil = null, params (string Description, decimal Price, int Quantity)[] items)
    {
        var quote = new QuoteEntity
        {
            Title = title,
            Status = status,
            QuoteNumber = await Numbers.GenerateAsync(),
            ClientId = client?.Id,
            ClientName = client?.Name,
            TaxRate = taxRate,
            ValidUntil = validUntil ?? DateTimeOffset.UtcNow.AddDays(14),
        };
        await Db.Quotes.SaveAsync(quote);
        foreach (var (description, price, quantity) in items)
        {
            await Db.Quotes.SaveLineItemAsync(new LineItemEntity { QuoteId = quote.Id, Description = description, UnitPrice = price, Quantity = quantity });
        }
        return (await Db.Quotes.GetByIdAsync(quote.Id))!;
    }

    /// <summary>
    /// Reads a feed until <paramref name="done"/> holds (or 5 s pass) and returns the last value. A feed recomputes on a
    /// background task after a state changes, so the read straight after an update can still see the previous value.
    /// </summary>
    public static async Task<T> Settled<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > until)
            {
                return value;
            }
            await Task.Delay(20);
        }
    }

    /// <summary>A list feed's items; an empty list feed reads as None (no list), which this returns as empty.</summary>
    public static async Task<IImmutableList<T>> ListOf<T>(Uno.Extensions.Reactive.IListFeed<T> feed) =>
        await feed ?? ImmutableList<T>.Empty;

    public void Dispose() => Db.Dispose();
}
