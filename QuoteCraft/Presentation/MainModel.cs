using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public partial record MainModel
{
    private readonly INotificationService _notificationService;

    public MainModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public IState<int> NotificationVersion => State<int>.Value(this, () => 0);

    public IFeed<int> UnreadNotificationCount => NotificationVersion
        .SelectAsync(async (_, ct) => await _notificationService.GetUnreadCountAsync());
}
