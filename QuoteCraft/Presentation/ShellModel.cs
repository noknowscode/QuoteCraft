namespace QuoteCraft.Presentation;

public partial record ShellModel
{
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILogger<ShellModel> _logger;

    public ShellModel(
        IBusinessProfileRepository profileRepo,
        ILogger<ShellModel> logger)
    {
        _profileRepo = profileRepo;
        _logger = logger;

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            // Load currency setting for the converter
            var profile = await _profileRepo.GetAsync();
            Helpers.CurrencyFormatConverter.CurrencyCode = profile.CurrencyCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize shell");
        }
    }
}
