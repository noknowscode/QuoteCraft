namespace QuoteCraft.Presentation;

/// <summary>New Quote (route <c>!NewQuote</c>): a title and an optional client; Create saves a Draft and returns it.</summary>
public partial record NewQuoteModel
{
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly QuoteNumberGenerator _quoteNumberGen;

    public NewQuoteModel(
        INavigator navigator,
        IQuoteRepository quoteRepo,
        IClientRepository clientRepo,
        IBusinessProfileRepository profileRepo,
        QuoteNumberGenerator quoteNumberGen)
    {
        _navigator = navigator;
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
        _profileRepo = profileRepo;
        _quoteNumberGen = quoteNumberGen;
    }

    public IState<string> QuoteTitle => State<string>.Value(this, () => string.Empty);

    public IState<string> ClientName => State<string>.Value(this, () => string.Empty);

    public IState<string> ValidationError => State<string>.Value(this, () => string.Empty);

    /// <summary>Up to eight client names containing what is typed.</summary>
    public IListFeed<string> ClientSuggestions => ClientName
        .SelectAsync(async (typed, ct) =>
        {
            var query = typed?.Trim() ?? string.Empty;
            if (query.Length == 0)
            {
                return ImmutableList<string>.Empty;
            }
            var clients = await _clientRepo.GetAllAsync();
            return (IImmutableList<string>)clients.Select(c => c.Name)
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToImmutableList();
        })
        .AsListFeed();

    /// <summary>A Draft linked to the named client (if there is one); the caller opens it in the editor.</summary>
    public async ValueTask Create(CancellationToken ct)
    {
        var title = (await QuoteTitle)?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            await ValidationError.UpdateAsync(_ => "Enter a title for the quote.", ct);
            return;
        }

        try
        {
            var profile = await _profileRepo.GetAsync();
            var name = (await ClientName)?.Trim();
            var quote = new QuoteEntity
            {
                Title = title,
                QuoteNumber = await _quoteNumberGen.GenerateAsync(),
                Status = QuoteStatus.Draft,
                TaxRate = profile.DefaultTaxRate,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(profile.QuoteValidDays),
                ClientName = string.IsNullOrEmpty(name) ? null : name,
                ClientId = ClientLink.Resolve(name, null, null, await _clientRepo.GetAllAsync()),
            };
            await _quoteRepo.SaveAsync(quote);
            await _navigator.NavigateBackWithResultAsync(this, data: quote, cancellation: ct);
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Create quote");
            await ValidationError.UpdateAsync(_ => ErrorReporter.Describe("The quote could not be created.", ex), ct);
        }
    }
}
