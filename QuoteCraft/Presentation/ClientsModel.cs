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

    // Feature gate state
    public IState<string> UpgradeMessage => State<string>.Value(this, () => string.Empty);

    public IListFeed<ClientDisplayItem> Clients =>
        Feed.Combine(SearchText, Version).SelectAsync(async (inputs, ct) =>
        {
            var (search, _) = inputs;
            var clients = await _clientRepo.GetAllAsync();
            var quotes = await _quoteRepo.GetAllAsync();

            var items = clients.Select(c =>
            {
                var clientQuotes = quotes.Where(q => q.ClientId == c.Id).ToList();
                var initials = GetInitials(c.Name);
                var city = ParseCity(c.Address);
                var totalValue = clientQuotes.Sum(q => q.Total);

                return new ClientDisplayItem(c.Id, c.Name, initials, clientQuotes.Count, city, (double)totalValue, c);
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

    public IFeed<int> ClientCount => Feed.Async(async ct => (await _clientRepo.GetAllAsync()).Count);

    // Combined detail feed: client + quotes (avoids nested FeedView with ElementName)
    public IFeed<ClientDetail> SelectedClientDetail => SelectedClient
        .SelectAsync(async (client, ct) =>
        {
            var all = await _quoteRepo.GetAllAsync();
            var quotes = all.Where(q => q.ClientId == client.Id).ToImmutableList();
            return new ClientDetail(client, quotes);
        });

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

    public async ValueTask AddClient(CancellationToken ct)
    {
        if (!await _featureGate.CanAddClientAsync())
        {
            await UpgradeMessage.UpdateAsync(_ => _featureGate.GetUpgradeMessage("clients"), ct);
            return;
        }
        await _navigator.NavigateRouteAsync(this, "ClientEditor", data: new ClientEntity());
    }

    public async ValueTask DismissUpgrade(CancellationToken ct)
    {
        await UpgradeMessage.UpdateAsync(_ => string.Empty, ct);
    }

    public async ValueTask EditClient(CancellationToken ct)
    {
        var client = await SelectedClient;
        if (client is not null)
            await _navigator.NavigateRouteAsync(this, "ClientEditor", data: client!.Entity);
    }

    public async ValueTask CreateQuoteForClient(CancellationToken ct)
    {
        var client = await SelectedClient;
        if (client is null) return;

        if (!await _featureGate.CanCreateQuoteAsync())
        {
            await UpgradeMessage.UpdateAsync(_ => _featureGate.GetUpgradeMessage("quotes"), ct);
            return;
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
