using QuoteCraft.Services;

namespace QuoteCraft.Presentation;

public partial record NotificationsModel
{
    private readonly INavigator _navigator;
    private readonly INotificationService _notificationService;

    public NotificationsModel(INavigator navigator, INotificationService notificationService)
    {
        _navigator = navigator;
        _notificationService = notificationService;
    }

    public IState<int> Version => State<int>.Value(this, () => 0);

    public IListFeed<NotificationEntity> Notifications => Version
        .SelectAsync(async (_, ct) =>
            (IImmutableList<NotificationEntity>)(await _notificationService.GetAllAsync()).ToImmutableList())
        .AsListFeed();

    public IFeed<int> UnreadCount => Version
        .SelectAsync(async (_, ct) => await _notificationService.GetUnreadCountAsync());

    public async ValueTask MarkAsRead(NotificationEntity notification, CancellationToken ct)
    {
        await _notificationService.MarkAsReadAsync(notification.Id);
        await Version.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask MarkAllRead(CancellationToken ct)
    {
        await _notificationService.MarkAllAsReadAsync();
        await Version.UpdateAsync(v => v + 1, ct);
    }

    public async ValueTask GoBack(CancellationToken ct)
    {
        // Navigate to Dashboard within the Main region (Notifications is a peer route)
        await _navigator.NavigateRouteAsync(this, "Dashboard");
    }
}
