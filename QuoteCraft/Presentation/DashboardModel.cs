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

/// <summary>A quote in the list, and whether it is the one open in the detail.</summary>
public partial record QuoteListItem(QuoteEntity Quote, bool IsSelected)
{
    public string Key => Quote.Id;
}

public partial record DashboardModel
{
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly QuoteCreationService _creation;
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
        QuoteCreationService creation,
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
        _creation = creation;
        _shareService = shareService;
        _featureGate = featureGate;
    }

    public IListFeed<string> FilterNames => ListFeed.Async(async ct => (IImmutableList<string>)ImmutableList.Create(
        "All", "Draft", "Sent", "Viewed", "Accepted", "Declined", "Expired"));

    public IState<string> SelectedFilter => State<string>.Value(this, () => "All");
    public IState<string> SearchText => State<string>.Value(this, () => string.Empty);
    public IState<QuoteEntity> SelectedQuote => State<QuoteEntity>.Empty(this);

    /// <summary>The open quote's id, for the list's selected card (empty when none; a None state would hold the list back).</summary>
    public IState<string> SelectedQuoteId => State<string>.Value(this, () => string.Empty);

    /// <summary>Edit (line items and notes in place) is on; Done saves the notes.</summary>
    public IState<bool> IsEditMode => State<bool>.Value(this, () => false);

    public IState<string> EditingNotes => State<string>.Value(this, () => string.Empty);
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
        if (_cachedQuotes is null)
        {
            // Overdue quotes expire as the list first opens (the expiry service also checks every few minutes).
            await ErrorReporter.RunQuietlyAsync("Expiring overdue quotes", () => _status.ExpireOverdueAsync(DateTimeOffset.UtcNow));
        }
        _cachedQuotes = await _quoteRepo.GetAllAsync();
        _cachedVersion = version;
        return _cachedQuotes;
    }

    private IListFeed<QuoteListItem>? _quotesFeed;

    public IListFeed<QuoteListItem> Quotes => _quotesFeed ??=
        Feed.Combine(SelectedFilter, SearchText, DetailVersion, SelectedQuoteId)
            .SelectAsync(async (inputs, ct) =>
            {
                var (filter, search, version, selectedId) = inputs;
                var all = await GetCachedQuotesAsync(version);

                IEnumerable<QuoteEntity> filtered = all;

                if (!string.IsNullOrEmpty(filter) && filter != "All")
                    filtered = filtered.Where(q => Enum.TryParse<QuoteStatus>(filter, out var s) && q.Status == s);

                if (!string.IsNullOrWhiteSpace(search))
                    filtered = filtered.Where(q =>
                        q.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (q.ClientName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        q.QuoteNumber.Contains(search, StringComparison.OrdinalIgnoreCase));

                return (IImmutableList<QuoteListItem>)filtered.Select(q => new QuoteListItem(q, q.Id == selectedId)).ToImmutableList();
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

    /// <summary>The expired banner's Reopen: back to Draft, valid again, ready to edit and send (PRD F17.3).</summary>
    public ValueTask ReopenQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be reopened.", async () =>
    {
        if (await SelectedQuote is { } quote && await _status.ReopenAsync(quote.Id) is { } reopened)
        {
            await SelectedQuote.UpdateAsync(_ => reopened, ct);
            await DetailVersion.UpdateAsync(v => v + 1, ct);
        }
    }, ct);

    /// <summary>A tap (or Enter) on a list card.</summary>
    public async ValueTask OpenQuote(QuoteListItem item, CancellationToken ct)
    {
        await IsEditMode.UpdateAsync(_ => false, ct);
        await IsPreviewMode.UpdateAsync(_ => false, ct);
        await SelectedQuote.UpdateAsync(_ => item.Quote, ct);
        await SelectedQuoteId.UpdateAsync(_ => item.Quote.Id, ct);
    }

    public async ValueTask EditQuote(CancellationToken ct)
    {
        var quote = await SelectedQuote;
        if (quote is not null)
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote!);
    }

    public ValueTask DeleteQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be deleted.", async () =>
    {
        if (await SelectedQuote is not { } quote
            || !await _navigator.ConfirmAsync(this, new ConfirmRequest("\uE74D", "Delete Quote?",
                "This quote and all its line items will be permanently removed.", "Delete", "Cancel"), ct))
        {
            return;
        }

        await _quoteRepo.DeleteAsync(quote.Id);
        await CloseDetailAsync(ct);
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }, ct);

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
        await CloseDetailAsync(ct);
    }

    private async ValueTask CloseDetailAsync(CancellationToken ct)
    {
        await IsEditMode.UpdateAsync(_ => false, ct);
        await SelectedQuote.UpdateAsync(_ => null!, ct);
        await SelectedQuoteId.UpdateAsync(_ => string.Empty, ct);
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
    private async Task ExportPdfAsync(string quoteId)
    {
        if (await _quoteRepo.GetByIdAsync(quoteId) is { } quote)
        {
            await _shareService.GenerateAndDownloadPdfAsync(quote);
        }
    }

    /// <summary>New Quote: the plan limit first (only where plans exist), then the dialog, then the editor.</summary>
    public ValueTask CreateQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be created.", async () =>
    {
        if (!await _featureGate.CanCreateQuoteAsync())
        {
            if (await _navigator.ConfirmAsync(this, new ConfirmRequest("\uE1D0", "Quote Limit Reached",
                    _featureGate.GetUpgradeMessage("quotes"), "Upgrade", "OK"), ct))
            {
                await OpenSettings(ct);
            }
            return;
        }

        if (await _navigator.ShowDialogAsync<QuoteEntity>(this, "NewQuote", null, ct) is { } quote)
        {
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote, cancellation: ct);
        }
    }, ct);

    /// <summary>
    /// Send: the dialog, then either the PDF alone, or the PDF, the email and the quote marked Sent (with a fresh
    /// validity period) unless the client has already answered. Without an email app the quote stays as it was.
    /// </summary>
    public ValueTask SendQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be sent.", async () =>
    {
        if (await GetSendDetailsAsync() is not { } details
            || await _navigator.ShowDialogAsync<SendQuoteChoice>(this, "SendQuote", details, ct) is not { } choice)
        {
            return;
        }

        await ExportPdfAsync(details.QuoteId);
        if (choice.PdfOnly)
        {
            return;
        }

        if (!await _email.ComposeEmailAsync(choice.Recipient, choice.Subject, choice.Body))
        {
            throw new InvalidOperationException("No email app opened, so the quote is not marked as sent.");
        }
        await _status.MarkSentAsync(details.QuoteId);
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }, ct);

    /// <summary>The selected quote as the Send dialog fills it in, or null when none is selected.</summary>
    private async Task<QuoteSendDetails?> GetSendDetailsAsync()
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

        if (await _creation.DuplicateAsync(quote.Id) is { } copy)
        {
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: copy);
        }
    }, ct);

    public async ValueTask DismissUpgrade(CancellationToken ct)
    {
        await UpgradeMessage.UpdateAsync(_ => string.Empty, ct);
    }

    // Inline edit: line items and notes, in the detail

    /// <summary>Edit: the notes go into the editor box, and taps on line items open them.</summary>
    public ValueTask StartEdit(CancellationToken ct) => ReportFailureAsync("The quote could not be opened for editing.", async () =>
    {
        if (await SelectedQuote is { } quote)
        {
            var fresh = await _quoteRepo.GetByIdAsync(quote.Id);
            await EditingNotes.UpdateAsync(_ => fresh?.Notes ?? string.Empty, ct);
            await IsEditMode.UpdateAsync(_ => true, ct);
        }
    }, ct);

    /// <summary>Done: saves the notes if they changed.</summary>
    public ValueTask FinishEdit(CancellationToken ct) => ReportFailureAsync("Your notes could not be saved.", async () =>
    {
        if (await SelectedQuote is { } quote && await _quoteRepo.GetByIdAsync(quote.Id) is { } fresh)
        {
            var notes = await EditingNotes ?? string.Empty;
            if (fresh.Notes != notes)
            {
                fresh.Notes = notes;
                await _quoteRepo.SaveAsync(fresh);
                await DetailVersion.UpdateAsync(v => v + 1, ct);
            }
        }
        await IsEditMode.UpdateAsync(_ => false, ct);
    }, ct);

    public ValueTask AddLineItem(CancellationToken ct) => EditLineItemAsync(null, ct);

    /// <summary>A tap on a line item: opens it while editing, does nothing otherwise.</summary>
    public async ValueTask EditLineItem(LineItemRow row, CancellationToken ct)
    {
        if (await IsEditMode)
        {
            await EditLineItemAsync(row.Item, ct);
        }
    }

    /// <summary>The line-item dialog: a new item goes last; an edited one keeps its place.</summary>
    private ValueTask EditLineItemAsync(LineItemEntity? item, CancellationToken ct) => ReportFailureAsync("The line item could not be saved.", async () =>
    {
        if (await SelectedQuote is not { } quote
            || await _navigator.ShowDialogAsync<LineItemEditResult>(this, "LineItemEditor", new LineItemEditRequest(item), ct) is not { } result)
        {
            return;
        }

        if (result.Saved is { } saved)
        {
            saved.QuoteId = quote.Id;
            await _quoteRepo.SaveLineItemAsync(saved);
        }
        else if (result.DeletedId is { Length: > 0 } deletedId)
        {
            await _quoteRepo.DeleteLineItemAsync(deletedId);
        }
        await DetailVersion.UpdateAsync(v => v + 1, ct);
    }, ct);

    /// <summary>From Catalog: the browser adds items as they are tapped; the detail reloads when it closes.</summary>
    public ValueTask OpenCatalog(CancellationToken ct) => ReportFailureAsync("The catalog could not be opened.", async () =>
    {
        if (await SelectedQuote is { } quote)
        {
            await _navigator.ShowDialogAsync<object>(this, "CatalogBrowser", new CatalogBrowserRequest(quote.Id), ct);
            await DetailVersion.UpdateAsync(v => v + 1, ct);
        }
    }, ct);
}
