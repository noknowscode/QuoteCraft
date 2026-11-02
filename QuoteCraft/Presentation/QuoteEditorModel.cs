using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public partial record QuoteEditorModel
{
    private readonly INavigator _navigator;
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;
    private readonly ICatalogItemRepository _catalogRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IShareService _shareService;
    private readonly string _quoteId;
    private readonly IPhotoService _photoService;
    private readonly IStatusHistoryRepository _statusHistoryRepo;
    private readonly QuoteStatusService _status;

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
        ICatalogItemRepository catalogRepo,
        IBusinessProfileRepository profileRepo,
        IShareService shareService,
        IPhotoService photoService,
        IStatusHistoryRepository statusHistoryRepo,
        QuoteStatusService status)
    {
        _status = status;
        _navigator = navigator;
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
        _catalogRepo = catalogRepo;
        _profileRepo = profileRepo;
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

    // Empty where PDF export works; otherwise the note shown in place of the Export PDF button.
    public IFeed<string> PdfUnavailableNote => Feed.Async(ct => ValueTask.FromResult(
        _shareService.CanExportPdf ? string.Empty : UnsupportedPdfGenerator.UnavailableMessage));
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

    public async ValueTask AddPhotoFromFile(Windows.Storage.StorageFile file, CancellationToken ct)
    {
        string? result;
        using (var source = await file.OpenStreamForReadAsync())
        {
            result = await _photoService.AddPhotoAsync(_quoteId, source, file.FileType);
        }
        if (result is not null)
            await PhotosVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask DeletePhoto(string photoPath, CancellationToken ct)
    {
        _photoService.DeletePhoto(photoPath);
        await PhotosVersion.UpdateAsync(v => v + 1, ct);
    }

    public IListFeed<LineItemEntity> LineItems => LineItemsVersion
        .SelectAsync(async (_, ct) =>
            (IImmutableList<LineItemEntity>)(await _quoteRepo.GetLineItemsAsync(_quoteId)).ToImmutableList())
        .AsListFeed();

    /// <summary>A new item goes last; an edited one keeps its place (the dialog has validated it).</summary>
    public async ValueTask SaveLineItem(LineItemEntity item, CancellationToken ct)
    {
        item.QuoteId = _quoteId;
        await _quoteRepo.SaveLineItemAsync(item);
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask AddFromCatalog(CatalogItemEntity catalogItem, CancellationToken ct)
    {
        var profile = await _profileRepo.GetAsync();

        var item = new LineItemEntity
        {
            QuoteId = _quoteId,
            Description = catalogItem.Description,
            UnitPrice = Money.WithMarkup(catalogItem.UnitPrice, profile.DefaultMarkup),
            Quantity = 1,
        };
        await _quoteRepo.SaveLineItemAsync(item);
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask DeleteLineItem(LineItemEntity item, CancellationToken ct)
    {
        await _quoteRepo.DeleteLineItemAsync(item.Id);
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
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
        await LineItemsVersion.UpdateAsync(v => v + 1, ct);
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
        catch (Exception)
        {
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

    public async ValueTask MarkSent(CancellationToken ct)
    {
        await UpdateStatus(QuoteStatus.Sent);
    }

    public async ValueTask MarkAccepted(CancellationToken ct)
    {
        await UpdateStatus(QuoteStatus.Accepted);
    }

    public async ValueTask MarkDeclined(CancellationToken ct)
    {
        await UpdateStatus(QuoteStatus.Declined);
    }

    public async ValueTask ExportPdf(CancellationToken ct)
    {
        await PersistEditsAsync();

        var quote = await _quoteRepo.GetByIdAsync(_quoteId);
        if (quote is null) return;

        await _shareService.GenerateAndDownloadPdfAsync(quote);
    }

    public async ValueTask CopyShareLink(CancellationToken ct)
    {
        await PersistEditsAsync();

        var quote = await _quoteRepo.GetByIdAsync(_quoteId);
        if (quote is null) return;

        await _shareService.CopyShareLinkAsync(quote);
    }

    public async ValueTask SendShareLink(CancellationToken ct)
    {
        await PersistEditsAsync();

        var quote = await _quoteRepo.GetByIdAsync(_quoteId);
        if (quote is null) return;

        var link = await _shareService.GenerateShareLinkAsync(quote);

        // Open email with link
        var subject = $"Quote #{quote.QuoteNumber} - {quote.Title}";
        var body = $"Please review your quote at: {link}";
        var mailto = $"mailto:{Uri.EscapeDataString(quote.ClientName ?? "")}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        await Windows.System.Launcher.LaunchUriAsync(new Uri(mailto));

        await _status.MarkSentAsync(quote.Id);
    }

    public async Task<bool> HasUnsavedChangesAsync()
    {
        return await Title != _initialTitle ||
               await Notes != _initialNotes ||
               await TaxRate != _initialTaxRate ||
               await ClientName != _initialClientName;
    }

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

    public Task<List<CatalogItemEntity>> GetCatalogItemsAsync() => _catalogRepo.GetAllAsync();

    public async Task<List<string>> SearchClientNamesAsync(string query)
    {
        var clients = await _clientRepo.GetAllAsync();
        return clients
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .Take(8)
            .ToList();
    }

    private async Task PersistEditsAsync()
    {
        var existing = await _quoteRepo.GetByIdAsync(_quoteId);
        if (existing is null) return;

        existing.Title = await Title ?? "Untitled";
        existing.Notes = await Notes;
        existing.ClientName = await ClientName;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        if (Money.TryParseRate(await TaxRate, out var tax))
            existing.TaxRate = tax;

        await _quoteRepo.SaveAsync(existing);
    }

    public IListFeed<StatusHistoryEntry> StatusHistory => LineItemsVersion
        .SelectAsync(async (_, ct) =>
            (IImmutableList<StatusHistoryEntry>)(await _statusHistoryRepo.GetByQuoteIdAsync(_quoteId)).ToImmutableList())
        .AsListFeed();

    private async Task UpdateStatus(QuoteStatus status)
    {
        // The edits first, so a status change does not drop them.
        await PersistEditsAsync();
        await _status.SetStatusAsync(_quoteId, status);
        await GoBack(CancellationToken.None);
    }
}
