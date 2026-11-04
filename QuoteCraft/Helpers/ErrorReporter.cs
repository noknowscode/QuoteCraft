namespace QuoteCraft.Helpers;

/// <summary>
/// What happens when something fails: the log, and the sentence the user sees. Model commands show it in a banner, a
/// message line or a dialog (<see cref="DialogNavigation.RunAsync"/>).
/// </summary>
public static class ErrorReporter
{
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
}
