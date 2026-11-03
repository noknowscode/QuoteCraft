namespace QuoteCraft.Presentation;

/// <summary>What the Send dialog starts from: the quote, its client's email and the business's name.</summary>
public record QuoteSendDetails(
    string QuoteId,
    string QuoteNumber,
    string Title,
    string? ClientName,
    string ClientEmail,
    string BusinessName,
    DateTimeOffset? ValidUntil);

public partial record DashboardModel
{
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ICatalogItemRepository _catalogRepo;
    private readonly QuoteNumberGenerator _quoteNumberGen;
    private readonly Services.IShareService _shareService;
    private readonly Services.IFeatureGateService _featureGate;
    private readonly Services.QuoteStatusService _status;
    private readonly Services.IEmailLauncherService _email;
    private readonly ILogger<DashboardModel> _logger;

    public DashboardModel(
        INavigator navigator,
        IQuoteRepository quoteRepo,
        IClientRepository clientRepo,
        IBusinessProfileRepository profileRepo,
        ICatalogItemRepository catalogRepo,
        QuoteNumberGenerator quoteNumberGen,
        Services.IShareService shareService,
        Services.IFeatureGateService featureGate,
        Services.QuoteStatusService status,
        Services.IEmailLauncherService email,
        ILogger<DashboardModel> logger)
    {
        _status = status;
        _email = email;
        _logger = logger;
        _navigator = navigator;
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
        _profileRepo = profileRepo;
        _catalogRepo = catalogRepo;
        _quoteNumberGen = quoteNumberGen;
        _shareService = shareService;
        _featureGate = featureGate;
    }

    public IState<string> SelectedFilter => State<string>.Value(this, () => "All");
    public IState<string> SearchText => State<string>.Value(this, () => string.Empty);
    public IState<QuoteEntity> SelectedQuote => State<QuoteEntity>.Empty(this);
    public IState<bool> IsPreviewMode => State<bool>.Value(this, () => false);
    public IState<int> DetailVersion => State<int>.Value(this, () => 0);

    // Feature gate state: shown when a limit is hit
    public IState<string> UpgradeMessage => State<string>.Value(this, () => string.Empty);

    /// <summary>What the last action could not do (a PDF that could not be written, say); shown in a banner.</summary>
    public IState<string> ActionError => State<string>.Value(this, () => string.Empty);

    public async ValueTask DismissError(CancellationToken ct)
    {
        await ActionError.UpdateAsync(_ => string.Empty, ct);
    }

    /// <summary>Runs a bound command's work; a failure is logged and shown in the banner instead of vanishing.</summary>
    private async ValueTask ReportFailureAsync(string failure, Func<Task> action, CancellationToken ct)
    {
        await ActionError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Failure}", failure);
            await ActionError.UpdateAsync(_ => ErrorReporter.Describe(failure, ex), ct);
        }
    }

    // Cached quote list: refreshed once per DetailVersion bump, shared across derived feeds
    private List<QuoteEntity>? _cachedQuotes;
    private int _cachedVersion = -1;

    private async Task<List<QuoteEntity>> GetCachedQuotesAsync(int version)
    {
        if (_cachedQuotes is not null && _cachedVersion == version)
            return _cachedQuotes;
        _cachedQuotes = await _quoteRepo.GetAllAsync();
        _cachedVersion = version;
        return _cachedQuotes;
    }

    private IListFeed<QuoteEntity>? _quotesFeed;

    public IListFeed<QuoteEntity> Quotes => _quotesFeed ??=
        Feed.Combine(SelectedFilter, SearchText, DetailVersion)
            .SelectAsync(async (inputs, ct) =>
            {
                var (filter, search, version) = inputs;
                var all = await GetCachedQuotesAsync(version);

                IEnumerable<QuoteEntity> filtered = all;

                if (!string.IsNullOrEmpty(filter) && filter != "All")
                    filtered = filtered.Where(q => Enum.TryParse<QuoteStatus>(filter, out var s) && q.Status == s);

                if (!string.IsNullOrWhiteSpace(search))
                    filtered = filtered.Where(q =>
                        q.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (q.ClientName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        q.QuoteNumber.Contains(search, StringComparison.OrdinalIgnoreCase));

                return (IImmutableList<QuoteEntity>)filtered.ToImmutableList();
            })
            .AsListFeed();

    public IFeed<int> TotalCount => DetailVersion
        .SelectAsync(async (version, ct) => (await GetCachedQuotesAsync(version)).Count);

    public IFeed<QuoteStatusCounts> StatusCounts => DetailVersion
        .SelectAsync(async (version, ct) =>
        {
            var all = await GetCachedQuotesAsync(version);
            return new QuoteStatusCounts(
                all.Count,
                all.Count(q => q.Status == QuoteStatus.Draft),
                all.Count(q => q.Status == QuoteStatus.Sent),
                all.Count(q => q.Status == QuoteStatus.Viewed),
                all.Count(q => q.Status == QuoteStatus.Accepted),
                all.Count(q => q.Status == QuoteStatus.Declined),
                all.Count(q => q.Status == QuoteStatus.Expired));
        });

    // Analytics: computed from all quotes, with 7-day sparkline data
    public IFeed<DashboardAnalytics> Analytics => DetailVersion
        .SelectAsync(async (version, ct) =>
        {
            var all = await GetCachedQuotesAsync(version);
            var now = DateTimeOffset.UtcNow;
            var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

            var thisMonth = all.Where(q => q.CreatedAt >= startOfMonth).ToList();

            var totalQuoted = thisMonth.Sum(q => q.Total);
            var sent = thisMonth.Count(q => q.Status != QuoteStatus.Draft);

            var resolved = all.Count(q =>
                q.Status == QuoteStatus.Accepted || q.Status == QuoteStatus.Declined);
            var accepted = all.Count(q => q.Status == QuoteStatus.Accepted);
            var rate = resolved > 0 ? Math.Round((double)accepted / resolved * 100, 1) : 0;

            // 7-day sparkline: group quotes by day bucket once, then index
            const int days = 7;
            var quotedDaily = new double[days];
            var sentDaily = new double[days];
            var acceptanceDaily = new double[days];
            var baseDate = now.Date.AddDays(-(days - 1));

            var byDay = all.Where(q => q.CreatedAt >= baseDate)
                .GroupBy(q => (int)(q.CreatedAt.Date - baseDate).TotalDays)
                .Where(g => g.Key >= 0 && g.Key < days)
                .ToDictionary(g => g.Key, g => g.ToList());

            var allResolved = all.Where(q =>
                q.Status == QuoteStatus.Accepted || q.Status == QuoteStatus.Declined).ToList();
            var allAccepted = allResolved.Count(q => q.Status == QuoteStatus.Accepted);
            var baseRate = allResolved.Count > 0
                ? Math.Round((double)allAccepted / allResolved.Count * 100, 1) : 0;

            for (int i = 0; i < days; i++)
            {
                if (byDay.TryGetValue(i, out var dayQuotes))
                {
                    quotedDaily[i] = (double)dayQuotes.Sum(q => q.Total);
                    sentDaily[i] = dayQuotes.Count(q => q.Status != QuoteStatus.Draft);
                }
                acceptanceDaily[i] = baseRate;
            }

            return new DashboardAnalytics(totalQuoted, sent, rate,
                quotedDaily, sentDaily, acceptanceDaily);
        });

    // Shared detail data: single DB fetch for both detail and preview views
    private IFeed<QuotePreviewData> QuoteDetailData =>
        Feed.Combine(SelectedQuote, DetailVersion)
            .SelectAsync(async (inputs, ct) =>
            {
                var (quote, _) = inputs;
                var freshQuote = await _quoteRepo.GetByIdAsync(quote.Id) ?? quote;
                ClientEntity? client = null;
                if (!string.IsNullOrEmpty(freshQuote.ClientId))
                    client = await _clientRepo.GetByIdAsync(freshQuote.ClientId);
                var profile = await _profileRepo.GetAsync();
                return new QuotePreviewData(freshQuote, freshQuote.LineItems.ToImmutableList(), profile, client);
            });

    public IFeed<QuoteDetail> SelectedQuoteDetail => QuoteDetailData
        .SelectAsync(async (d, ct) => new QuoteDetail(d.Quote, d.LineItems, d.Client));

    public IFeed<QuotePreviewData> PreviewData => QuoteDetailData;

    public async ValueTask RefreshDetail(CancellationToken ct)
    {
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask ExpireOverdueQuotes(CancellationToken ct)
    {
        var expired = await _status.ExpireOverdueAsync(DateTimeOffset.UtcNow);
        if (expired.Count > 0)
            await DetailVersion.UpdateAsync(v => v + 1, ct);
    }

    /// <summary>The expired banner's Reopen: back to Draft, valid again, ready to edit and send (PRD F17.3).</summary>
    public ValueTask ReopenQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be reopened.", async () =>
    {
        if (await SelectedQuote is { } quote && await _status.ReopenAsync(quote.Id) is { } reopened)
        {
            await SelectedQuote.UpdateAsync(_ => reopened, ct);
            await DetailVersion.UpdateAsync(v => v + 1, ct);
        }
    }, ct);

    public async ValueTask OpenQuote(QuoteEntity quote, CancellationToken ct)
    {
        await IsPreviewMode.UpdateAsync(_ => false, ct);
        await SelectedQuote.UpdateAsync(_ => quote, ct);
    }

    public async ValueTask EditQuote(CancellationToken ct)
    {
        var quote = await SelectedQuote;
        if (quote is not null)
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote!);
    }

    public async ValueTask DeleteQuote(CancellationToken ct)
    {
        var quote = await SelectedQuote;
        if (quote is not null)
        {
            await _quoteRepo.DeleteAsync(quote.Id);
            await SelectedQuote.UpdateAsync(_ => null!, ct);
            await DetailVersion.UpdateAsync(v => v + 1, ct);
        }
    }

    public async ValueTask PreviewQuote(CancellationToken ct)
    {
        await IsPreviewMode.UpdateAsync(_ => true, ct);
    }

    public async ValueTask BackToDetail(CancellationToken ct)
    {
        await IsPreviewMode.UpdateAsync(_ => false, ct);
    }

    /// <summary>One-pane layouts (below 1080): close the detail and show the list again.</summary>
    public async ValueTask BackToList(CancellationToken ct)
    {
        await IsPreviewMode.UpdateAsync(_ => false, ct);
        await SelectedQuote.UpdateAsync(_ => null!, ct);
    }

    /// <summary>Plans and the upgrade live in Settings.</summary>
    public async ValueTask OpenSettings(CancellationToken ct)
    {
        await _navigator.NavigateRouteAsync(this, "Settings", cancellation: ct);
    }

    public ValueTask DownloadPdf(CancellationToken ct) => ReportFailureAsync("The PDF could not be created.", async () =>
    {
        if (await SelectedQuote is { } quote)
        {
            await ExportPdfAsync(quote.Id);
        }
    }, ct);

    /// <summary>Saves the quote's PDF (<see cref="Services.IShareService"/>); the quote's status does not change.</summary>
    public async Task ExportPdfAsync(string quoteId)
    {
        if (await _quoteRepo.GetByIdAsync(quoteId) is { } quote)
        {
            await _shareService.GenerateAndDownloadPdfAsync(quote);
        }
    }

    /// <summary>Null when a quote can be created; otherwise the plan-limit message (only where plans exist).</summary>
    public async Task<string?> GetQuoteLimitMessageAsync() =>
        await _featureGate.CanCreateQuoteAsync() ? null : _featureGate.GetUpgradeMessage("quotes");

    public async Task<IReadOnlyList<string>> GetClientNamesAsync() =>
        (await _clientRepo.GetAllAsync()).Select(c => c.Name).ToArray();

    /// <summary>The New Quote dialog's Create: a Draft linked to the named client (if there is one), opened in the editor.</summary>
    public async Task CreateQuoteAsync(string title, string? clientName)
    {
        var profile = await _profileRepo.GetAsync();
        var name = clientName?.Trim();
        var quote = new QuoteEntity
        {
            Title = title.Trim(),
            QuoteNumber = await _quoteNumberGen.GenerateAsync(),
            Status = QuoteStatus.Draft,
            TaxRate = profile.DefaultTaxRate,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(profile.QuoteValidDays),
            ClientName = string.IsNullOrEmpty(name) ? null : name,
            ClientId = ClientLink.Resolve(name, null, null, await _clientRepo.GetAllAsync()),
        };
        await _quoteRepo.SaveAsync(quote);
        await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote);
    }

    /// <summary>The selected quote as the Send dialog fills it in, or null when none is selected.</summary>
    public async Task<QuoteSendDetails?> GetSendDetailsAsync()
    {
        if (await SelectedQuote is not { } selected || await _quoteRepo.GetByIdAsync(selected.Id) is not { } quote)
        {
            return null;
        }

        var client = string.IsNullOrEmpty(quote.ClientId) ? null : await _clientRepo.GetByIdAsync(quote.ClientId);
        var profile = await _profileRepo.GetAsync();
        return new QuoteSendDetails(quote.Id, quote.QuoteNumber, quote.Title, quote.ClientName, client?.Email ?? string.Empty,
            profile.BusinessName ?? "Our Company", quote.ValidUntil);
    }

    /// <summary>
    /// The Send dialog's Send via Email: saves the PDF to attach, opens the email, and marks the quote Sent (with a
    /// fresh validity period) unless the client has already answered.
    /// </summary>
    public async Task SendByEmailAsync(string quoteId, string recipient, string subject, string body)
    {
        await ExportPdfAsync(quoteId);

        if (!await _email.ComposeEmailAsync(recipient, subject, body))
        {
            throw new InvalidOperationException("No email app opened, so the quote is not marked as sent.");
        }
        await _status.MarkSentAsync(quoteId);
        await DetailVersion.UpdateAsync(v => v + 1, CancellationToken.None);
    }

    public ValueTask DuplicateQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be duplicated.", async () =>
    {
        var quote = await SelectedQuote;
        if (quote is null) return;

        // Feature gate: check quote limit
        if (!await _featureGate.CanCreateQuoteAsync())
        {
            await UpgradeMessage.UpdateAsync(_ => _featureGate.GetUpgradeMessage("quotes"), ct);
            return;
        }

        var freshQuote = await _quoteRepo.GetByIdAsync(quote.Id);
        if (freshQuote is null) return;

        var quoteNumber = await _quoteNumberGen.GenerateAsync();
        var newQuote = new QuoteEntity
        {
            Title = freshQuote.Title + " (Copy)",
            QuoteNumber = quoteNumber,
            Status = QuoteStatus.Draft,
            ClientId = freshQuote.ClientId,
            ClientName = freshQuote.ClientName,
            Notes = freshQuote.Notes,
            TaxRate = freshQuote.TaxRate,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(
                (await _profileRepo.GetAsync()).QuoteValidDays),
        };
        await _quoteRepo.SaveAsync(newQuote);

        // Clone line items
        foreach (var item in freshQuote.LineItems)
        {
            var newItem = new LineItemEntity
            {
                QuoteId = newQuote.Id,
                Description = item.Description,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
            };
            await _quoteRepo.SaveLineItemAsync(newItem);
        }

        // Open the duplicated quote in the editor
        await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: newQuote);
    }, ct);

    public async ValueTask DismissUpgrade(CancellationToken ct)
    {
        await UpgradeMessage.UpdateAsync(_ => string.Empty, ct);
    }

    // ── Inline Edit Support ──────────────────────────────────────────────

    public async ValueTask SaveInlineNotes(string notes, CancellationToken ct)
    {
        var quote = await SelectedQuote;
        if (quote is null) return;

        var fresh = await _quoteRepo.GetByIdAsync(quote.Id);
        if (fresh is not null && fresh.Notes != notes)
        {
            fresh.Notes = notes;
            await _quoteRepo.SaveAsync(fresh);
            await DetailVersion.UpdateAsync(v => v + 1, ct);
        }
    }

    public async ValueTask SaveInlineLineItem(LineItemEntity item, CancellationToken ct)
    {
        // A new item goes last; an edited one keeps its place (the dialog has validated it).
        var quote = await SelectedQuote;
        if (quote is null) return;

        item.QuoteId = quote.Id;
        await _quoteRepo.SaveLineItemAsync(item);
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask DeleteInlineLineItem(string lineItemId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(lineItemId)) return;
        await _quoteRepo.DeleteLineItemAsync(lineItemId);
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask AddInlineFromCatalog(CatalogItemEntity catalogItem, CancellationToken ct)
    {
        var quote = await SelectedQuote;
        if (quote is null) return;

        var profile = await _profileRepo.GetAsync();

        var item = new LineItemEntity
        {
            QuoteId = quote.Id,
            Description = catalogItem.Description,
            UnitPrice = Money.WithMarkup(catalogItem.UnitPrice, profile.DefaultMarkup),
            Quantity = 1,
        };
        await _quoteRepo.SaveLineItemAsync(item);
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }

    public Task<List<CatalogItemEntity>> GetCatalogItemsAsync() => _catalogRepo.GetAllAsync();

    public async Task<string> GetSelectedQuoteNotesAsync()
    {
        var quote = await SelectedQuote;
        if (quote is not null)
        {
            var fresh = await _quoteRepo.GetByIdAsync(quote.Id);
            return fresh?.Notes ?? string.Empty;
        }
        return string.Empty;
    }
}
