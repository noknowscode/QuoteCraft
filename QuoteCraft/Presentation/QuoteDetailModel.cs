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

/// <summary>
/// The open quote on the Quotes page (route <c>./QuoteDetail</c>, the page's detail region): its detail and print
/// preview, inline edits of line items and notes, and its actions (send, PDF, duplicate, delete, reopen, the full
/// editor). The list is <see cref="DashboardModel"/>; the two share <see cref="QuoteBoard"/>.
/// </summary>
public partial record QuoteDetailModel
{
    private readonly string _quoteId;
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly QuoteCreationService _creation;
    private readonly IShareService _shareService;
    private readonly IFeatureGateService _featureGate;
    private readonly QuoteStatusService _status;
    private readonly IEmailLauncherService _email;
    private readonly QuoteBoard _board;
    private readonly ILogger<QuoteDetailModel> _logger;

    public QuoteDetailModel(
        QuoteEntity? quote,
        INavigator navigator,
        IQuoteRepository quoteRepo,
        IClientRepository clientRepo,
        IBusinessProfileRepository profileRepo,
        QuoteCreationService creation,
        IShareService shareService,
        IFeatureGateService featureGate,
        QuoteStatusService status,
        IEmailLauncherService email,
        QuoteBoard board,
        ILogger<QuoteDetailModel> logger)
    {
        // Navigation can restore the detail region's last route without its data (back from the full editor, when the
        // Quotes page is made again): there is no quote to show then, and the new page has none open (DashboardModel).
        _quoteId = quote?.Id ?? string.Empty;
        _navigator = navigator;
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
        _profileRepo = profileRepo;
        _creation = creation;
        _shareService = shareService;
        _featureGate = featureGate;
        _status = status;
        _email = email;
        _board = board;
        _logger = logger;
    }

    public string QuoteId => _quoteId;

    /// <summary>Edit (line items and notes in place) is on; Done saves the notes.</summary>
    public IState<bool> IsEditMode => State<bool>.Value(this, () => false);

    public IState<string> EditingNotes => State<string>.Value(this, () => string.Empty);

    public IState<bool> IsPreviewMode => State<bool>.Value(this, () => false);

    /// <summary>Shown when a plan limit stopped Duplicate (only where plans exist, D3).</summary>
    public IState<string> UpgradeMessage => State<string>.Value(this, () => string.Empty);

    /// <summary>What the last action could not do (a PDF that could not be written, say); shown in a banner.</summary>
    public IState<string> ActionError => State<string>.Value(this, () => string.Empty);

    /// <summary>The quote, its items, its client and the business, read again whenever quotes change.</summary>
    private IFeed<QuotePreviewData> Data => Feed.AsyncEnumerable(_board.WatchAsync)
        .Select(state => state.Version)
        .SelectAsync(async (_, ct) =>
        {
            if (await _quoteRepo.GetByIdAsync(_quoteId) is not { } quote)
            {
                return null!;
            }
            var client = string.IsNullOrEmpty(quote.ClientId) ? null : await _clientRepo.GetByIdAsync(quote.ClientId);
            var profile = await _profileRepo.GetAsync();
            return new QuotePreviewData(quote, quote.LineItems.ToImmutableList(), profile, client);
        });

    public IFeed<QuoteDetail> Detail => Data.Select(d => new QuoteDetail(d.Quote, d.LineItems, d.Client));

    public IFeed<QuotePreviewData> Preview => Data;

    public async ValueTask DismissError(CancellationToken ct) => await ActionError.UpdateAsync(_ => string.Empty, ct);

    public async ValueTask DismissUpgrade(CancellationToken ct) => await UpgradeMessage.UpdateAsync(_ => string.Empty, ct);

    /// <summary>Retry after a failed read: every view of the quotes reads them again.</summary>
    public ValueTask Refresh(CancellationToken ct)
    {
        _board.QuotesChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>One-pane layouts (below 1080): close the quote and show the list again.</summary>
    public ValueTask BackToList(CancellationToken ct)
    {
        _board.Close();
        return ValueTask.CompletedTask;
    }

    public async ValueTask PreviewQuote(CancellationToken ct) => await IsPreviewMode.UpdateAsync(_ => true, ct);

    public async ValueTask BackToDetail(CancellationToken ct) => await IsPreviewMode.UpdateAsync(_ => false, ct);

    /// <summary>The full editor (title, client, tax rate), with the quote as stored now.</summary>
    public ValueTask EditQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be opened.", async () =>
    {
        if (await _quoteRepo.GetByIdAsync(_quoteId) is { } quote)
        {
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: quote, cancellation: ct);
        }
    }, ct);

    /// <summary>Plans and the upgrade live in Settings.</summary>
    public async ValueTask OpenSettings(CancellationToken ct) =>
        await _navigator.NavigateRouteAsync(this, "Settings", cancellation: ct);

    /// <summary>The expired banner's Reopen: back to Draft, valid again, ready to edit and send (PRD F17.3).</summary>
    public ValueTask ReopenQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be reopened.", async () =>
    {
        if (await _status.ReopenAsync(_quoteId) is not null)
        {
            _board.QuotesChanged();
        }
    }, ct);

    public ValueTask DeleteQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be deleted.", async () =>
    {
        if (!await _navigator.ConfirmAsync(this, new ConfirmRequest("", "Delete Quote?",
                "This quote and its line items will be removed from your quotes.", "Delete", "Cancel"), ct))
        {
            return;
        }

        await _quoteRepo.DeleteAsync(_quoteId);
        _board.Close();
        _board.QuotesChanged();
    }, ct);

    public ValueTask DownloadPdf(CancellationToken ct) =>
        ReportFailureAsync("The PDF could not be created.", ExportPdfAsync, ct);

    /// <summary>
    /// Saves the quote's PDF (<see cref="IShareService"/>); the quote's status does not change. When nothing opened the
    /// saved file, the banner says where it is.
    /// </summary>
    private async Task ExportPdfAsync()
    {
        if (await _quoteRepo.GetByIdAsync(_quoteId) is { } quote
            && await _shareService.GenerateAndDownloadPdfAsync(quote) is { Length: > 0 } note)
        {
            await ActionError.UpdateAsync(_ => note, CancellationToken.None);
        }
    }

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

        await ExportPdfAsync();
        if (choice.PdfOnly)
        {
            return;
        }

        if (!await _email.ComposeEmailAsync(choice.Recipient, choice.Subject, choice.Body))
        {
            throw new InvalidOperationException("No email app opened, so the quote is not marked as sent.");
        }
        await _status.MarkSentAsync(_quoteId);
        _board.QuotesChanged();
    }, ct);

    /// <summary>The quote as the Send dialog fills it in, or null when it is gone.</summary>
    private async Task<QuoteSendDetails?> GetSendDetailsAsync()
    {
        if (await _quoteRepo.GetByIdAsync(_quoteId) is not { } quote)
        {
            return null;
        }

        var client = string.IsNullOrEmpty(quote.ClientId) ? null : await _clientRepo.GetByIdAsync(quote.ClientId);
        var profile = await _profileRepo.GetAsync();
        return new QuoteSendDetails(quote.Id, quote.QuoteNumber, quote.Title, quote.ClientName, client?.Email ?? string.Empty,
            profile.BusinessName ?? "Our Company", quote.ValidUntil);
    }

    /// <summary>A Draft copy with the same items, opened in the editor; the plan limit first (only where plans exist).</summary>
    public ValueTask DuplicateQuote(CancellationToken ct) => ReportFailureAsync("The quote could not be duplicated.", async () =>
    {
        if (!await _featureGate.CanCreateQuoteAsync())
        {
            await UpgradeMessage.UpdateAsync(_ => _featureGate.GetUpgradeMessage("quotes"), ct);
            return;
        }

        if (await _creation.DuplicateAsync(_quoteId) is { } copy)
        {
            _board.QuotesChanged();
            await _navigator.NavigateRouteAsync(this, "QuoteEditor", data: copy, cancellation: ct);
        }
    }, ct);

    /// <summary>Edit: the notes go into the editor box, and taps on line items open them.</summary>
    public ValueTask StartEdit(CancellationToken ct) => ReportFailureAsync("The quote could not be opened for editing.", async () =>
    {
        var quote = await _quoteRepo.GetByIdAsync(_quoteId);
        await EditingNotes.UpdateAsync(_ => quote?.Notes ?? string.Empty, ct);
        await IsEditMode.UpdateAsync(_ => true, ct);
    }, ct);

    /// <summary>Done: saves the notes if they changed.</summary>
    public ValueTask FinishEdit(CancellationToken ct) => ReportFailureAsync("Your notes could not be saved.", async () =>
    {
        if (await _quoteRepo.GetByIdAsync(_quoteId) is { } quote)
        {
            var notes = await EditingNotes ?? string.Empty;
            if (quote.Notes != notes)
            {
                quote.Notes = notes;
                await _quoteRepo.SaveAsync(quote);
                _board.QuotesChanged();
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
        if (await _navigator.ShowDialogAsync<LineItemEditResult>(this, "LineItemEditor", new LineItemEditRequest(item), ct) is not { } result)
        {
            return;
        }

        if (result.Saved is { } saved)
        {
            saved.QuoteId = _quoteId;
            await _quoteRepo.SaveLineItemAsync(saved);
        }
        else if (result.DeletedId is { Length: > 0 } deletedId)
        {
            await _quoteRepo.DeleteLineItemAsync(deletedId);
        }
        _board.QuotesChanged();
    }, ct);

    /// <summary>From Catalog: the browser adds items as they are tapped; the quote reloads when it closes.</summary>
    public ValueTask OpenCatalog(CancellationToken ct) => ReportFailureAsync("The catalog could not be opened.", async () =>
    {
        await _navigator.ShowDialogAsync<object>(this, "CatalogBrowser", new CatalogBrowserRequest(_quoteId), ct);
        _board.QuotesChanged();
    }, ct);

    /// <summary>Runs a bound command's work; a failure is logged and shown in the banner instead of vanishing.</summary>
    private async ValueTask ReportFailureAsync(string failure, Func<Task> action, CancellationToken ct)
    {
        await ActionError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Failure}", failure);
            await ActionError.UpdateAsync(_ => ErrorReporter.Describe(failure, ex), ct);
        }
    }
}
