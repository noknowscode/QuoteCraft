using Uno.Extensions.Toolkit;

namespace QuoteCraft.Presentation;

public partial record SettingsModel
{
    private readonly INavigator _navigator;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ICatalogItemRepository _catalogRepo;
    private readonly Services.IFeatureGateService _featureGate;
    private readonly Services.ISubscriptionService _subscriptionService;
    private readonly Services.IAuthService _authService;
    private readonly Data.Remote.SupabaseConfig _supabase;
    private readonly ILogger<SettingsModel> _logger;
    private readonly IThemeService _theme;
    private readonly Services.ThemeChoiceNotifier _themeChoice;
    private readonly IDispatcher _dispatcher;

    public SettingsModel(
        INavigator navigator,
        IBusinessProfileRepository profileRepo,
        ICatalogItemRepository catalogRepo,
        Services.IFeatureGateService featureGate,
        Services.ISubscriptionService subscriptionService,
        Services.IAuthService authService,
        Data.Remote.SupabaseConfig supabase,
        IThemeService theme,
        Services.ThemeChoiceNotifier themeChoice,
        IDispatcher dispatcher,
        ILogger<SettingsModel> logger)
    {
        _dispatcher = dispatcher;
        _theme = theme;
        _themeChoice = themeChoice;
        _navigator = navigator;
        _profileRepo = profileRepo;
        _catalogRepo = catalogRepo;
        _featureGate = featureGate;
        _subscriptionService = subscriptionService;
        _authService = authService;
        _supabase = supabase;
        _logger = logger;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadProfile(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize settings");
            await LoadError.UpdateAsync(_ => "Your settings could not be loaded.", CancellationToken.None);
        }
    }

    /// <summary>Set when the profile could not be read; the page shows it with Retry.</summary>
    public IState<string> LoadError => State<string>.Value(this, () => string.Empty);

    /// <summary>What a Save found wrong, or empty.</summary>
    public IState<string> ValidationError => State<string>.Value(this, () => string.Empty);

    /// <summary>"Settings saved." after a successful Save, until the next one.</summary>
    public IState<string> SavedMessage => State<string>.Value(this, () => string.Empty);

    public IState<bool> IsSaving => State<bool>.Value(this, () => false);

    /// <summary>
    /// 0 System (the default), 1 Light, 2 Dark: the window's theme service applies and saves it. The row follows a
    /// choice made in the rail's menu too (<see cref="Services.ThemeChoiceNotifier"/>); either side changes the theme
    /// only when the choice differs from the saved one, so the two never call each other in a loop.
    /// </summary>
    public IState<int> ThemeIndex => State<int>.AsyncEnumerable(this, WatchThemeIndexAsync)
        .ForEach(async (index, ct) =>
        {
            var choice = ToTheme(index);
            if (choice != _theme.Theme)
            {
                await _theme.SetThemeAsync(choice);
                _themeChoice.Raise();
            }
        });

    private static AppTheme ToTheme(int index) => index switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.System };

    private static int ToIndex(AppTheme theme) => theme switch { AppTheme.Light => 1, AppTheme.Dark => 2, _ => 0 };

    private async IAsyncEnumerable<int> WatchThemeIndexAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var changed = new ChangeSignal();
        Action handler = changed.Raise;
        _themeChoice.Changed += handler;
        try
        {
            while (true)
            {
                yield return ToIndex(_theme.Theme);
                await changed.WaitAsync(ct);
            }
        }
        finally
        {
            _themeChoice.Changed -= handler;
        }
    }

    public async ValueTask Reload(CancellationToken ct)
    {
        await LoadError.UpdateAsync(_ => string.Empty, ct);
        await InitializeAsync();
    }

    public async ValueTask OpenCatalog(CancellationToken ct)
    {
        await _navigator.NavigateRouteAsync(this, "Catalog", cancellation: ct);
    }

    /// <summary>
    /// The quote preview beside the form (from 1400 px): sample items priced with the markup as the catalog adds them,
    /// tax at the default rate, the dates from today and the validity, and amounts in the currency, all as typed.
    /// </summary>
    public IFeed<SettingsPreview> Preview => Feed.Combine(DefaultTaxRate, DefaultMarkup, CurrencyCode, QuoteValidDays, QuoteNumberPrefix)
        .Select(inputs =>
        {
            var (tax, markup, currency, validDays, prefix) = inputs;
            return SettingsPreview.From(prefix, tax, markup, validDays, currency, DateTimeOffset.Now);
        });

    public IFeed<int> CatalogItemCount => Feed.Async(async ct =>
        await _catalogRepo.GetItemCountAsync());

    // Empty when a Supabase project is configured (plans, subscription and account sections show); otherwise the
    // local-mode note that takes their place.
    public IFeed<string> LocalModeNote => Feed.Async(ct => ValueTask.FromResult(
        _supabase.IsConfigured ? string.Empty : Data.Remote.SupabaseConfig.NotConfiguredMessage));

    // Subscription info feeds
    public IFeed<string> CurrentPlanName => Feed.Async(async ct =>
    {
        var tier = _featureGate.CurrentTier;
        var maxQuotes = _featureGate.MaxQuotesPerMonth;
        return tier switch
        {
            Services.SubscriptionTier.Free => $"Free ({maxQuotes} quotes/mo)",
            Services.SubscriptionTier.Pro => "Pro (Unlimited)",
            Services.SubscriptionTier.Business => "Business (Unlimited)",
            _ => "Free"
        };
    });

    public IFeed<string> QuotaUsage => Feed.Async(async ct =>
    {
        var used = await _featureGate.GetQuotesUsedThisMonthAsync();
        var max = _featureGate.MaxQuotesPerMonth;
        return max == int.MaxValue ? $"{used} this month" : $"{used} of {max}";
    });

    public IState<string> BusinessName => State<string>.Value(this, () => string.Empty);
    public IState<string> Phone => State<string>.Value(this, () => string.Empty);
    public IState<string> Email => State<string>.Value(this, () => string.Empty);
    public IState<string> Address => State<string>.Value(this, () => string.Empty);
    public IState<string> Website => State<string>.Value(this, () => string.Empty);
    public IState<string> BusinessNumber => State<string>.Value(this, () => string.Empty);
    public IState<string> DefaultTaxRate => State<string>.Value(this, () => Money.FormatRate(13m));
    public IState<string> DefaultMarkup => State<string>.Value(this, () => "0");
    public IState<string> CurrencyCode => State<string>.Value(this, () => "USD");
    public IState<string> QuoteValidDays => State<string>.Value(this, () => "14");
    public IState<string> QuoteNumberPrefix => State<string>.Value(this, () => "QC-");
    public IState<string> CustomFooter => State<string>.Value(this, () => string.Empty);
    public IState<string> LogoPath => State<string>.Value(this, () => string.Empty);

    /// <summary>The logo's file name, beside its thumbnail.</summary>
    public IFeed<string> LogoFileName => LogoPath.Select(path => string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path));

    /// <summary>What Upload could not do; shown under the logo row.</summary>
    public IState<string> LogoError => State<string>.Value(this, () => string.Empty);

    public async ValueTask LoadProfile(CancellationToken ct)
    {
        var profile = await _profileRepo.GetAsync();
        await BusinessName.UpdateAsync(_ => profile.BusinessName ?? string.Empty, ct);
        await Phone.UpdateAsync(_ => profile.Phone ?? string.Empty, ct);
        await Email.UpdateAsync(_ => profile.Email ?? string.Empty, ct);
        await Address.UpdateAsync(_ => profile.Address ?? string.Empty, ct);
        await Website.UpdateAsync(_ => profile.Website ?? string.Empty, ct);
        await BusinessNumber.UpdateAsync(_ => profile.BusinessNumber ?? string.Empty, ct);
        await DefaultTaxRate.UpdateAsync(_ => Money.FormatRate(profile.DefaultTaxRate), ct);
        await DefaultMarkup.UpdateAsync(_ => Money.FormatRate(profile.DefaultMarkup), ct);
        await CurrencyCode.UpdateAsync(_ => profile.CurrencyCode, ct);
        await QuoteValidDays.UpdateAsync(_ => profile.QuoteValidDays.ToString(), ct);
        await QuoteNumberPrefix.UpdateAsync(_ => profile.QuoteNumberPrefix, ct);
        await CustomFooter.UpdateAsync(_ => profile.CustomFooter ?? string.Empty, ct);
        await LogoPath.UpdateAsync(_ => profile.LogoPath ?? string.Empty, ct);
    }

    public async ValueTask Save(CancellationToken ct)
    {
        await SavedMessage.UpdateAsync(_ => string.Empty, ct);
        var error = SettingsValidation.Validate(await DefaultTaxRate, await DefaultMarkup, await QuoteValidDays);
        await ValidationError.UpdateAsync(_ => error ?? string.Empty, ct);
        if (error is not null)
        {
            return;
        }

        await IsSaving.UpdateAsync(_ => true, ct);
        try
        {
            await SaveProfileAsync();
            await SavedMessage.UpdateAsync(_ => "Settings saved.", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            await ValidationError.UpdateAsync(_ => "Your settings could not be saved. Try again.", ct);
        }
        finally
        {
            await IsSaving.UpdateAsync(_ => false, ct);
        }
    }

    private async Task SaveProfileAsync()
    {
        var profile = await _profileRepo.GetAsync();

        profile.BusinessName = await BusinessName;
        profile.Phone = await Phone;
        profile.Email = await Email;
        profile.Address = await Address;
        profile.Website = await Website;
        profile.BusinessNumber = await BusinessNumber;
        profile.CustomFooter = await CustomFooter;
        profile.LogoPath = await LogoPath;
        profile.QuoteNumberPrefix = await QuoteNumberPrefix ?? "QC-";
        profile.CurrencyCode = await CurrencyCode ?? "USD";
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        if (Money.TryParseRate(await DefaultTaxRate, out var tax))
            profile.DefaultTaxRate = tax;

        var markupText = await DefaultMarkup;
        profile.DefaultMarkup = string.IsNullOrWhiteSpace(markupText) ? 0m
            : Money.TryParseRate(markupText, out var markup) ? markup : profile.DefaultMarkup;

        var daysStr = await QuoteValidDays ?? "14";
        if (int.TryParse(daysStr, out var days))
            profile.QuoteValidDays = days;

        await _profileRepo.SaveAsync(profile);

        // Update the currency converter for the whole app
        Helpers.CurrencyFormatConverter.CurrencyCode = profile.CurrencyCode;
    }

    /// <summary>
    /// Copies a picked image into the app's data folder as the logo (Save keeps it). Read through the StorageFile: a
    /// picked file's Path is not readable on every head (WebAssembly, Android, iOS).
    /// </summary>
    public async ValueTask UploadLogo(CancellationToken ct)
    {
        await LogoError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            if (await ImagePicker.PickAsync(_dispatcher, ct) is { } file)
            {
                await SetLogoAsync(file);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upload logo");
            await LogoError.UpdateAsync(_ => ErrorReporter.Describe("The logo could not be added.", ex), ct);
        }
    }

    private async Task SetLogoAsync(Windows.Storage.StorageFile file)
    {
        var logoDir = AppStorage.Combine("logo");
        Directory.CreateDirectory(logoDir);

        var destPath = Path.Combine(logoDir, $"logo{Path.GetExtension(file.Name)}");
        await using (var source = await file.OpenStreamForReadAsync())
        await using (var destination = File.Create(destPath))
        {
            await source.CopyToAsync(destination);
        }

        await LogoPath.UpdateAsync(_ => destPath, CancellationToken.None);
    }

    /// <summary>Remove: the logo's copy goes, and Save stores the profile without one.</summary>
    public async ValueTask RemoveLogo(CancellationToken ct)
    {
        await LogoError.UpdateAsync(_ => string.Empty, ct);
        try
        {
            var currentPath = await LogoPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                File.Delete(currentPath);
            }
            await LogoPath.UpdateAsync(_ => string.Empty, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Remove logo");
            await LogoError.UpdateAsync(_ => ErrorReporter.Describe("The logo could not be removed.", ex), ct);
        }
    }

    /// <summary>Pro, billed monthly: Stripe Checkout in the browser.</summary>
    public ValueTask UpgradeToPro(CancellationToken ct) => OpenCheckoutAsync("monthly", ct);

    /// <summary>Pro, billed yearly at the annual discount (PRD F13.6): Stripe Checkout in the browser.</summary>
    public ValueTask UpgradeToProAnnual(CancellationToken ct) => OpenCheckoutAsync("annual", ct);

    private ValueTask OpenCheckoutAsync(string interval, CancellationToken ct) => _navigator.RunAsync(this, "Checkout could not open.", async () =>
    {
        var url = await _subscriptionService.GetCheckoutUrlAsync("pro", interval);
        await LaunchAsync(url, "Checkout is not available right now. Try again later.");
    }, ct);

    public ValueTask ManageSubscription(CancellationToken ct) => _navigator.RunAsync(this, "The billing portal could not open.", async () =>
    {
        var url = await _subscriptionService.GetCustomerPortalUrlAsync();
        await LaunchAsync(url, "The billing portal is not available right now. Try again later.");
    }, ct);

    /// <summary>Opens a billing page in the browser; says so when there is none or nothing opened it.</summary>
    private static async Task LaunchAsync(string? url, string unavailable)
    {
        if (string.IsNullOrEmpty(url) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
        {
            throw new InvalidOperationException(unavailable);
        }
    }

    /// <summary>Signed in to the configured Supabase project (only then is Sign Out offered; Sign In otherwise).</summary>
    public IFeed<bool> IsSignedIn => Feed.AsyncEnumerable(WatchSignedInAsync);

    private async IAsyncEnumerable<bool> WatchSignedInAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var changed = new ChangeSignal();
        Action<Services.AuthState> handler = _ => changed.Raise();
        _authService.AuthStateChanged += handler;
        try
        {
            while (true)
            {
                yield return _authService.CurrentState.IsAuthenticated;
                await changed.WaitAsync(ct);
            }
        }
        finally
        {
            _authService.AuthStateChanged -= handler;
        }
    }

    /// <summary>Sign In (a Supabase project is configured and nobody is signed in): the sign-in page.</summary>
    public ValueTask SignIn(CancellationToken ct) => _navigator.RunAsync(this, "The sign-in page could not open.",
        async () => await _navigator.NavigateRouteAsync(this, "Auth", cancellation: ct), ct);

    public ValueTask SignOut(CancellationToken ct) => _navigator.RunAsync(this, "You could not be signed out.", async () =>
    {
        await _authService.SignOutAsync();
        await _navigator.NavigateRouteAsync(this, "Auth", qualifier: Qualifiers.ClearBackStack, cancellation: ct);
    }, ct);
}
