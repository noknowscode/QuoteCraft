namespace QuoteCraft.Helpers;

/// <summary>
/// What happens when something fails. Page event handlers are <c>async void</c>, so an exception that escapes one
/// reaches the app's unhandled-exception handler with nothing shown; these helpers log the error and tell the user
/// what did not happen.
/// </summary>
public static class ErrorReporter
{
    /// <summary>A user action (a click, a tap): on failure, log it and show <paramref name="failure"/> in a dialog.</summary>
    /// <param name="failure">What did not happen, as a sentence: "The quote could not be deleted."</param>
    public static async Task RunAsync(FrameworkElement page, string failure, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(ex, failure);
            await ShowAsync(page, failure, ex);
        }
    }

    /// <summary>Work the user did not ask for (a page loading, search suggestions): on failure, log it only.</summary>
    public static async Task RunQuietlyAsync(string context, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(ex, context);
        }
    }

    /// <summary>
    /// The sentence to show: <paramref name="failure"/>, then the reason when the app wrote it for people (an
    /// <see cref="InvalidOperationException"/>, such as "close the PDF in the other app"), otherwise "Try again."
    /// </summary>
    public static string Describe(string failure, Exception ex) =>
        ex is InvalidOperationException { Message.Length: > 0 } ? $"{failure} {ex.Message}" : $"{failure} Try again.";

    public static void Log(Exception? ex, string context)
    {
        if (App.ServicesOrNull?.GetService<ILoggerFactory>() is { } loggers)
        {
            loggers.CreateLogger("QuoteCraft").LogError(ex, "{Context}", context);
        }
        else
        {
            // Before the host exists (startup), or after it is gone.
            Console.Error.WriteLine($"{context}: {ex}");
        }
    }

    private static async Task ShowAsync(FrameworkElement page, string failure, Exception ex)
    {
        if (page.XamlRoot is null)
        {
            return;
        }

        try
        {
            var reason = ex is InvalidOperationException { Message.Length: > 0 }
                ? ex.Message
                : "Try again. If it keeps happening, restart QuoteCraft.";
            await DialogHelper.BuildBannerDialog(page.XamlRoot, "", failure, reason, closeButtonText: "OK").ShowAsync();
        }
        catch (Exception dialogError)
        {
            // Only one dialog can be open at a time; the log has the original error.
            Log(dialogError, "The error dialog could not open");
        }
    }
}
