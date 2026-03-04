namespace QuoteCraft.Services;

public enum SubscriptionTier
{
    Free,
    Pro,
    Business
}

public interface IFeatureGateService
{
    SubscriptionTier CurrentTier { get; }
    Task<bool> CanCreateQuoteAsync();
    Task<bool> CanAddClientAsync();
    Task<int> GetQuotesUsedThisMonthAsync();
    Task<int> GetClientCountAsync();
    int MaxQuotesPerMonth { get; }
    int MaxClients { get; }
    bool HasPdfWatermark { get; }
    bool HasCloudSync { get; }
    bool HasAllCatalogs { get; }
    bool HasBranding { get; }
    bool HasNotifications { get; }
    bool HasAdvancedStatuses { get; }
    string GetUpgradeMessage(string feature);
}

public class FeatureGateService : IFeatureGateService
{
    private readonly IQuoteRepository _quoteRepo;
    private readonly IClientRepository _clientRepo;

    // In the future, this will come from Stripe/Supabase user profile
    private SubscriptionTier _currentTier = SubscriptionTier.Free;

    public FeatureGateService(IQuoteRepository quoteRepo, IClientRepository clientRepo)
    {
        _quoteRepo = quoteRepo;
        _clientRepo = clientRepo;
    }

    public SubscriptionTier CurrentTier => _currentTier;

    public int MaxQuotesPerMonth => _currentTier switch
    {
        SubscriptionTier.Free => 5,
        _ => int.MaxValue // Unlimited
    };

    public int MaxClients => _currentTier switch
    {
        SubscriptionTier.Free => 10,
        _ => int.MaxValue
    };

    public bool HasPdfWatermark => _currentTier == SubscriptionTier.Free;

    public bool HasCloudSync => _currentTier >= SubscriptionTier.Pro;

    public bool HasAllCatalogs => _currentTier >= SubscriptionTier.Pro;

    public bool HasBranding => _currentTier >= SubscriptionTier.Pro;

    public bool HasNotifications => _currentTier >= SubscriptionTier.Pro;

    public bool HasAdvancedStatuses => _currentTier >= SubscriptionTier.Pro;

    public async Task<bool> CanCreateQuoteAsync()
    {
        if (_currentTier >= SubscriptionTier.Pro)
            return true;

        var usedThisMonth = await GetQuotesUsedThisMonthAsync();
        return usedThisMonth < MaxQuotesPerMonth;
    }

    public async Task<bool> CanAddClientAsync()
    {
        if (_currentTier >= SubscriptionTier.Pro)
            return true;

        var count = await GetClientCountAsync();
        return count < MaxClients;
    }

    public async Task<int> GetQuotesUsedThisMonthAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return await _quoteRepo.CountCreatedSinceAsync(startOfMonth);
    }

    public async Task<int> GetClientCountAsync()
    {
        return await _clientRepo.CountActiveAsync();
    }

    public string GetUpgradeMessage(string feature) => feature switch
    {
        "quotes" => $"You've used all {MaxQuotesPerMonth} free quotes this month. Upgrade to Pro for unlimited quotes at $15/mo.",
        "clients" => $"You've reached the {MaxClients} client limit. Upgrade to Pro for unlimited clients at $15/mo.",
        "branding" => "Remove the QuoteCraft watermark and add your logo. Upgrade to Pro at $15/mo.",
        "catalogs" => "Unlock all trade catalogs (Plumbing, Electrical, HVAC, Painting). Upgrade to Pro at $15/mo.",
        "sync" => "Sync your quotes across all devices. Upgrade to Pro at $15/mo.",
        _ => "Upgrade to QuoteCraft Pro for the full experience at $15/mo."
    };
}
