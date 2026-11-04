using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public partial record QuoteEditorModel
{
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly IShareService _shareService;
    private readonly string _quoteId;
    private readonly IPhotoService _photoService;
    private readonly IStatusHistoryRepository _statusHistoryRepo;
    private readonly QuoteStatusService _status;
    private readonly IDispatcher _dispatcher;
    private readonly QuoteBoard _board;

    // Track initial values for unsaved changes detection
    private readonly string _initialTitle;
    private readonly string _initialNotes;
    private readonly string _initialTaxRate;
    private readonly string _initialClientName;

    public QuoteEditorModel(
        QuoteEntity quote,
        INavigator navigator,
        IQuoteRepository quoteRepo,
        IClientRepository clientRepo,
        IShareService shareService,
        IPhotoService photoService,
        IStatusHistoryRepository statusHistoryRepo,
        QuoteStatusService status,
        IDispatcher dispatcher,
        QuoteBoard board)
    {
        _board = board;
        _status = status;
        _dispatcher = dispatcher;
        _navigator = navigator;
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
        _shareService = shareService;
        _photoService = photoService;
        _statusHistoryRepo = statusHistoryRepo;
        _quoteId = quote.Id;

        _initialTitle = quote.Title;
        _initialNotes = quote.Notes ?? string.Empty;
        _initialTaxRate = Money.FormatRate(quote.TaxRate);
        _initialClientName = quote.ClientName ?? string.Empty;

        Title = State<string>.Value(this, () => quote.Title);
        Notes = State<string>.Value(this, () => quote.Notes ?? string.Empty);
        TaxRate = State<string>.Value(this, () => Money.FormatRate(quote.TaxRate));
        ClientName = State<string>.Value(this, () => quote.ClientName ?? string.Empty);
        QuoteNumber = State<string>.Value(this, () => quote.QuoteNumber);
    }

    public IState<string> Title { get; }
    public IState<string> Notes { get; }
    public IState<string> TaxRate { get; }
    public IState<string> ClientName { get; }
    public IState<string> QuoteNumber { get; }

    public IState<string> ValidationError => State<string>.Value(this, () => string.Empty);

    /// <summary>True while Save writes the quote (the page shows a progress bar).</summary>
    public IState<bool> IsSaving => State<bool>.Value(this, () => false);

    public IState<int> LineItemsVersion => State<int>.Value(this, () => 0);
    public IState<int> PhotosVersion => State<int>.Value(this, () => 0);

    public string QuoteId => _quoteId;


    public IListFeed<string> Photos => PhotosVersion
        .SelectAsync(async (_, ct) =>
            (IImmutableList<string>)_photoService.GetPhotos(_quoteId).ToImmutableList())
        .AsListFeed();

    /// <summary>Add Photo: the limit first, then the picker; a failure shows next to the buttons.</summary>
    public ValueTask AddPhoto(CancellationToken ct) => ReportFailureAsync("The photo could not be added.", async () =>
    {
        if (_photoService.GetPhotos(_quoteId).Count >= _photoService.MaxPhotos)
        {
            await _navigator.ConfirmAsync(this, new ConfirmRequest("\uE722", "Photo Limit Reached",
                $"You can attach up to {_photoService.MaxPhotos} photos per quote."), ct);
            return;
        }

        if (await ImagePicker.PickAsync(_dispatcher, ct) is not { } file)
        {
            return;
        }

        string? result;
        using (var source = await file.OpenStreamForReadAsync())
        {
            result = await _photoService.AddPhotoAsync(_quoteId, source, file.FileType);
        }
        if (result is not null)
            await PhotosVersion.UpdateAsync(v => v + 1, ct);
    }, ct);

    public async ValueTask DeletePhoto(string photoPath, CancellationToken ct)
    {
        _photoService.DeletePhoto(photoPath);
        await PhotosVersion.UpdateAsync(v => v + 1, ct);
    }

    public IListFeed<LineItemEntity> LineItems => LineItemsVersion
        .SelectAsync(async (_, ct) =>
            (IImmutableList<LineItemEntity>)(await _quoteRepo.GetLineItemsAsync(_quoteId)).ToImmutableList())
        .AsListFeed();

    /// <summary>
    /// Subtotal, tax and total, recomputed when the items change and as the tax rate is typed. The typed text is never
    /// rewritten; until it reads as a rate (0 to 100), the tax line asks for one and the total leaves tax out.
    /// </summary>
    public IFeed<QuoteTotals> Totals => Feed.Combine(LineItemsVersion, TaxRate)
        .SelectAsync(async (inputs, ct) =>
        {
            var (_, rateText) = inputs;
            var rateReads = Money.TryParseRate(rateText, out var rate) && rate <= 100;
            var quote = new QuoteEntity
            {
                TaxRate = rateReads ? rate : 0m,
                LineItems = await _quoteRepo.GetLineItemsAsync(_quoteId),
            };
            var label = rateReads ? $"Tax ({Money.FormatRate(rate)} %)" : "Tax (enter a rate from 0 to 100)";
            return new QuoteTotals(quote.Subtotal, label, quote.TaxAmount, quote.Total);
        });

    public ValueTask AddLineItem(CancellationToken ct) => EditLineItemAsync(null, ct);

    public ValueTask EditLineItem(LineItemEntity item, CancellationToken ct) => EditLineItemAsync(item, ct);

    /// <summary>The line-item dialog: a new item goes last; an edited one keeps its place; Delete removes it.</summary>
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
        await LineItemsChangedAsync(ct);
    }, ct);

    /// <summary>From Catalog: the browser adds items as they are tapped; the list reloads when it closes.</summary>
    public ValueTask OpenCatalog(CancellationToken ct) => ReportFailureAsync("The catalog could not be opened.", async () =>
    {
        await _navigator.ShowDialogAsync<object>(this, "CatalogBrowser", new CatalogBrowserRequest(_quoteId), ct);
        await LineItemsChangedAsync(ct);
    }, ct);

    /// <summary>Runs an action's work; a failure is logged and shown in the editor's message line.</summary>
    private async ValueTask ReportFailureAsync(string failure, Func<Task> action, CancellationToken ct)
    {
        await ValidationError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, failure);
            await ValidationError.UpdateAsync(_ => ErrorReporter.Describe(failure, ex), ct);
        }
    }

    public ValueTask MoveLineItemUp(LineItemEntity item, CancellationToken ct) => MoveLineItemAsync(item, -1, ct);

    public ValueTask MoveLineItemDown(LineItemEntity item, CancellationToken ct) => MoveLineItemAsync(item, +1, ct);

    private async ValueTask MoveLineItemAsync(LineItemEntity item, int offset, CancellationToken ct)
    {
        var ids = (await _quoteRepo.GetLineItemsAsync(_quoteId)).Select(i => i.Id).ToList();
        var from = ids.IndexOf(item.Id);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= ids.Count) return;

        (ids[from], ids[to]) = (ids[to], ids[from]);
        await _quoteRepo.ReorderLineItemsAsync(_quoteId, ids);
        await LineItemsChangedAsync(ct);
    }

    /// <summary>The items changed: this page reads them again, and so do the quotes list and detail.</summary>
    private async ValueTask LineItemsChangedAsync(CancellationToken ct)
    {
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
        _board.QuotesChanged();
    }

    public async ValueTask Save(CancellationToken ct)
    {
        var title = (await Title)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            await ValidationError.UpdateAsync(_ => "Quote title is required.", ct);
            return;
        }

        if (!Money.TryParseRate(await TaxRate, out var tax) || tax > 100)
        {
            await ValidationError.UpdateAsync(_ => "Tax rate must be a number between 0 and 100.", ct);
            return;
        }

        await ValidationError.UpdateAsync(_ => string.Empty, ct);
        await IsSaving.UpdateAsync(_ => true, ct);
        try
        {
            await PersistEditsAsync();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Save quote");
            await ValidationError.UpdateAsync(_ => "The quote could not be saved. Try again.", ct);
            return;
        }
        finally
        {
            await IsSaving.UpdateAsync(_ => false, ct);
        }
        await GoBack(ct);
    }

    /// <summary>Retry after a failed load: read the line items and photos again.</summary>
    public async ValueTask Reload(CancellationToken ct)
    {
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
        await PhotosVersion.UpdateAsync(v => v + 1, ct);
    }

    public ValueTask MarkSent(CancellationToken ct) => UpdateStatus(QuoteStatus.Sent, ct);

    public ValueTask MarkAccepted(CancellationToken ct) => UpdateStatus(QuoteStatus.Accepted, ct);

    public ValueTask MarkDeclined(CancellationToken ct) => UpdateStatus(QuoteStatus.Declined, ct);

    /// <summary>Saves the edits, then saves the PDF; a failure shows on the page, next to the button.</summary>
    public async ValueTask ExportPdf(CancellationToken ct)
    {
        await ValidationError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            await PersistEditsAsync();
            if (await _quoteRepo.GetByIdAsync(_quoteId) is { } quote)
            {
                await _shareService.GenerateAndDownloadPdfAsync(quote);
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Export PDF");
            await ValidationError.UpdateAsync(_ => ErrorReporter.Describe("The PDF could not be created.", ex), ct);
        }
    }

    private async Task<bool> HasUnsavedChangesAsync()
    {
        return await Title != _initialTitle ||
               await Notes != _initialNotes ||
               await TaxRate != _initialTaxRate ||
               await ClientName != _initialClientName;
    }

    /// <summary>The back arrow: asks before dropping unsaved edits.</summary>
    public ValueTask Back(CancellationToken ct) => ReportFailureAsync("The editor could not close.", async () =>
    {
        if (await HasUnsavedChangesAsync()
            && !await _navigator.ConfirmAsync(this, new ConfirmRequest("\uE7BA", "Unsaved Changes",
                "You have unsaved changes. Are you sure you want to leave?", "Discard", "Stay"), ct))
        {
            return;
        }
        await GoBack(ct);
    }, ct);

    /// <summary>
    /// Back to where the editor was opened from; to the quotes list when there is nothing to go back to (the editor
    /// opened straight after onboarding).
    /// </summary>
    public async ValueTask GoBack(CancellationToken ct)
    {
        var response = await _navigator.NavigateBackAsync(this, cancellation: ct);
        if (response is not { Success: true })
        {
            await _navigator.NavigateRouteAsync(this, "Dashboard", cancellation: ct);
        }
    }

    /// <summary>Up to eight client names containing what is typed in the Client field.</summary>
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
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase) && n != query)
                .Take(8)
                .ToImmutableList();
        })
        .AsListFeed();

    private async Task PersistEditsAsync()
    {
        var existing = await _quoteRepo.GetByIdAsync(_quoteId);
        if (existing is null) return;

        existing.Title = await Title ?? "Untitled";
        existing.Notes = await Notes;

        // A changed name links the quote to the client with that name (or to none), so the client's counts and the
        // Send address follow the name on the quote.
        var clientName = (await ClientName)?.Trim();
        existing.ClientId = ClientLink.Resolve(clientName, existing.ClientName, existing.ClientId, await _clientRepo.GetAllAsync());
        existing.ClientName = string.IsNullOrEmpty(clientName) ? null : clientName;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        if (Money.TryParseRate(await TaxRate, out var tax))
            existing.TaxRate = tax;

        await _quoteRepo.SaveAsync(existing);
        _board.QuotesChanged();
    }

    /// <summary>When the quote was sent, accepted, declined, expired or reopened (PRD F3.4), oldest first.</summary>
    public IListFeed<StatusHistoryEntry> StatusHistory => LineItemsVersion
        .SelectAsync(async (_, ct) =>
            (IImmutableList<StatusHistoryEntry>)(await _statusHistoryRepo.GetByQuoteIdAsync(_quoteId)).ToImmutableList())
        .AsListFeed();

    private async ValueTask UpdateStatus(QuoteStatus status, CancellationToken ct)
    {
        await ValidationError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            // The edits first, so a status change does not drop them.
            await PersistEditsAsync();
            await _status.SetStatusAsync(_quoteId, status);
            _board.QuotesChanged();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, $"Mark {status}");
            await ValidationError.UpdateAsync(_ => ErrorReporter.Describe($"The quote could not be marked {status}.", ex), ct);
            return;
        }
        await GoBack(ct);
    }
}
