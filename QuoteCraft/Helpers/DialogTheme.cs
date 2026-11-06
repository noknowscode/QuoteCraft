namespace QuoteCraft.Helpers;

/// <summary>
/// A dialog takes the window's theme. The theme service sets Light or Dark on the window's content, and a dialog opens
/// in a popup outside it, so without this a dialog kept the system's theme while the app showed the other. The theme
/// is set as the dialog is built, before its template resolves its brushes; its surface follows through
/// AppDialogStyle (App.xaml).
/// </summary>
public static class DialogTheme
{
    /// <summary>Call from a dialog's constructor (a dialog route builds a new dialog each time it opens).</summary>
    public static void FollowWindow(ContentDialog dialog)
    {
        if (App.MainAppWindow.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }
    }
}
