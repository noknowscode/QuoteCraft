namespace QuoteCraft.Helpers;

/// <summary>The app's dialogs, opened from models through navigation ("!" routes registered in App.xaml.cs).</summary>
public static class DialogNavigation
{
    /// <summary>Shows a <see cref="ConfirmRequest"/>; true when the user pressed its primary button.</summary>
    public static async Task<bool> ConfirmAsync(this INavigator navigator, object sender, ConfirmRequest request, CancellationToken ct) =>
        await navigator.GetDataAsync<ContentDialogResult>(sender, "Confirm", Qualifiers.Dialog, request, ct) == ContentDialogResult.Primary;

    /// <summary>Opens a dialog route and waits for the result its model sends back; null when it was cancelled.</summary>
    public static Task<TResult?> ShowDialogAsync<TResult>(this INavigator navigator, object sender, string route, object? data, CancellationToken ct) =>
        navigator.GetDataAsync<TResult>(sender, route, Qualifiers.Dialog, data, ct);
}
