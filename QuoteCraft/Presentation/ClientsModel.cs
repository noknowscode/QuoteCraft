namespace QuoteCraft.Presentation;

public partial record ClientsModel
{
    private readonly INavigator _navigator;
    private readonly IClientRepository _clientRepo;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly QuoteNumberGenerator _quoteNumberGen;
    private readonly Services.IFeatureGateService _featureGate;

    public ClientsModel(
        INavigator navigator,
        IClientRepository clientRepo,
        IQuoteRepository quoteRepo,
        IBusinessProfileRepository profileRepo,
        QuoteNumberGenerator quoteNumberGen,
        Services.IFeatureGateService featureGate)
    {
        _navigator = navigator;
        _clientRepo = clientRepo;
        _quoteRepo = quoteRepo;
        _profileRepo = profileRepo;
        _quoteNumberGen = quoteNumberGen;
        _featureGate = featureGate;
    }

    public IState<string> SearchText => State<string>.Value(this, () => string.Empty);
    public IState<ClientDisplayItem> SelectedClient => State<ClientDisplayItem>.Empty(this);
    public IState<int> Version => State<int>.Value(this, () => 0);

    public IListFeed<ClientDisplayItem> Clients =>
        Feed.Combine(SearchText, Version).SelectAsync(async (inputs, ct) =>
        {
            var (search, _) = inputs;
            var clients = await _clientRepo.GetAllAsync();
            var quotes = await _quoteRepo.GetAllAsync();

            // Pre-group quotes by ClientId: O(N+M) instead of O(N*M)
            var quotesByClient = quotes
                .GroupBy(q => q.ClientId ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            var items = clients.Select(c =>
            {
                var clientQuotes = quotesByClient.GetValueOrDefault(c.Id, []);
                var initials = GetInitials(c.Name);
                var city = ParseCity(c.Address);
                var totalValue = clientQuotes.Sum(q => q.Total);

                return new ClientDisplayItem(c.Id, c.Name, initials, clientQuotes.Count, city, totalValue, c);
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                items = items.Where(i =>
                    i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.City.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return (IImmutableList<ClientDisplayItem>)items.ToImmutableList();
        })
        .AsListFeed();

    public IFeed<int> ClientCount => Version
        .SelectAsync(async (_, ct) => (await _clientRepo.GetAllAsync()).Count);

    public IFeed<ClientsAnalytics> Analytics => Version
        .SelectAsync(async (_, ct) =>
        {
            var clients = await _clientRepo.GetAllAsync();
            var quotes = await _quoteRepo.GetAllAsync();

            var totalClients = clients.Count;
            var totalRevenue = quotes
                .Where(q => q.Status == QuoteStatus.Accepted)
                .Sum(q => q.Total);
            var avgPerClient = totalClients > 0 ? Money.RoundToCents(totalRevenue / totalClients) : 0m;
            var newThisMonth = clients
                .Count(c => c.UpdatedAt >= new DateTimeOffset(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, TimeSpan.Zero));

            return new ClientsAnalytics(totalClients, totalRevenue, avgPerClient, newThisMonth);
        });

    // Combined detail feed: client + quotes (avoids nested FeedView with ElementName). Read again on every Version
    // bump, so an edit shows in the detail and Retry reloads it.
    public IFeed<ClientDetail> SelectedClientDetail => Feed.Combine(SelectedClient, Version)
        .SelectAsync(async (inputs, ct) =>
        {
            var (client, _) = inputs;
            var entity = await _clientRepo.GetByIdAsync(client.Id) ?? client.Entity;
            var all = await _quoteRepo.GetAllAsync();
            var quotes = all.Where(q => q.ClientId == client.Id).ToImmutableList();
            var item = new ClientDisplayItem(entity.Id, entity.Name, GetInitials(entity.Name), quotes.Count,
                ParseCity(entity.Address), quotes.Sum(q => q.Total), entity);
            return new ClientDetail(item, quotes);
        });

    /// <summary>One-pane layouts (below 1080): close the detail and show the list again.</summary>
    public async ValueTask BackToList(CancellationToken ct)
    {
        await SelectedClient.UpdateAsync(_ => null!, ct);
    }

    public async ValueTask SelectClient(ClientDisplayItem item, CancellationToken ct)
    {
        await SelectedClient.UpdateAsync(_ => item, ct);
    }

    public async ValueTask RefreshList(CancellationToken ct)
    {
        await Version.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask DeleteClient(CancellationToken ct)
    {
        var client = await SelectedClient;
        if (client is not null)
        {
            await _clientRepo.DeleteAsync(client.Id);
            await SelectedClient.UpdateAsync(_ => null!, ct);
            await Version.UpdateAsync(v => v + 1, ct);
        }
    }

    /// <summary>Null when a client can be added; otherwise the plan-limit message (only where plans exist).</summary>
    public async Task<string?> GetClientLimitMessageAsync() =>
        await _featureGate.CanAddClientAsync() ? null : _featureGate.GetUpgradeMessage("clients");

    /// <summary>The selected client as stored now (the edit dialog starts from it), or null.</summary>
    public async Task<ClientEntity?> GetSelectedClientAsync() =>
        await SelectedClient is { } selected ? await _clientRepo.GetByIdAsync(selected.Id) : null;

    /// <summary>The client dialog's Save; the list and the detail read the client again.</summary>
    public async Task SaveClientAsync(ClientEntity client)
    {
        client.UpdatedAt = DateTimeOffset.UtcNow;
        await _clientRepo.SaveAsync(client);
        await Version.UpdateAsync(v => v + 1, CancellationToken.None);
    }

    /// <summary>Plans and the upgrade live in Settings.</summary>
    public async ValueTask OpenSettings(CancellationToken ct)
    {
        await _navigator.NavigateRouteAsync(this, "Settings", cancellation: ct);
    }

    public async ValueTask CreateQuoteForClient(CancellationToken ct)
    {
        var client = await SelectedClient;
        if (client is null) return;

        // A plan limit (only where plans exist) is reported like any failure: the page's handler shows it.
        if (!await _featureGate.CanCreateQuoteAsync())
        {
            throw new InvalidOperationException(_featureGate.GetUpgradeMessage("quotes"));
        }

        var profile = await _profileRepo.GetAsync();
        var quoteNumber = await _quoteNumberGen.GenerateAsync();
        var quote = new QuoteEntity
        {
            Title = "New Quote",
            QuoteNumber = quoteNumber,
            Status = QuoteStatus.Draft,
            ClientId = client.Id,
            ClientName = client.Name,
            TaxRate = profile.DefaultTaxRate,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(profile.QuoteValidDays),
        };
        await _quoteRepo.SaveAsync(quote);
        await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote);
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}";
        return parts[0].Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[0][1])}"
            : $"{char.ToUpper(parts[0][0])}";
    }

    private static string ParseCity(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;
        var lines = address.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length >= 2)
        {
            var cityLine = lines[^1].Trim();
            var parts = cityLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cityParts = parts.TakeWhile(p => !p.All(c => char.IsDigit(c) || c == '-')).ToArray();
            return cityParts.Length > 0 ? string.Join(" ", cityParts) : cityLine;
        }
        return address.Trim();
    }
}
