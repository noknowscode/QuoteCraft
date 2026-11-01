using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public partial record MainModel
{
    private readonly INotificationService _notificationService;
    private readonly IOnboardingService _onboarding;
    private readonly INavigator _navigator;
    private readonly ILogger<MainModel> _logger;

    public MainModel(
        INotificationService notificationService,
        IOnboardingService onboarding,
        INavigator navigator,
        ILogger<MainModel> logger)
    {
        _notificationService = notificationService;
        _onboarding = onboarding;
        _navigator = navigator;
        _logger = logger;
    }

    /// <summary>
    /// On first run, replaces Main with onboarding, which navigates back to Main when it is done. MainPage calls this
    /// once it is loaded: the check used to sit in ShellModel's constructor, where it raced the default route and the
    /// onboarding page never showed.
    /// </summary>
    public async ValueTask ShowOnboardingOnFirstRun(CancellationToken ct)
    {
        try
        {
            if (!await _onboarding.IsOnboardingCompleteAsync())
            {
                await _navigator.NavigateRouteAsync(this, "Onboarding", qualifier: Qualifiers.ClearBackStack, cancellation: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open onboarding");
        }
    }

    public IState<int> NotificationVersion => State<int>.Value(this, () => 0);

    public IFeed<int> UnreadNotificationCount => NotificationVersion
        .SelectAsync(async (_, ct) => await _notificationService.GetUnreadCountAsync());
}
