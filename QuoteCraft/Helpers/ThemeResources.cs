using Microsoft.UI.Xaml.Media;

namespace QuoteCraft.Helpers;

/// <summary>
/// Looks up an app or Material theme resource for the theme an element shows. Code that sets a brush must not read
/// <c>Application.Current.Resources[key]</c>: on WinUI that returns the application theme's value, not the one the
/// window shows after a Light/Dark switch, and a brush set from code keeps its colour until it is set again.
/// </summary>
public static class ThemeResources
{
    /// <summary>The brush for the element's theme; transparent when the key is missing (a bug the UI shows plainly).</summary>
    public static Brush GetBrush(FrameworkElement element, string key) =>
        Get(element, key) as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static object? Get(FrameworkElement element, string key)
    {
        var theme = element.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        return Find(Application.Current.Resources, key, theme)
            ?? (Application.Current.Resources.TryGetValue(key, out var value) ? value : null);
    }

    private static object? Find(ResourceDictionary dictionary, string key, string theme)
    {
        // Later merged dictionaries win, as in XAML lookup; a dictionary's own entries win over its merged ones.
        // "Default" is the dark dictionary's other name in WinUI.
        foreach (var name in theme == "Dark" ? new[] { "Dark", "Default" } : new[] { "Light" })
        {
            if (dictionary.ThemeDictionaries.TryGetValue(name, out var themed) && themed is ResourceDictionary themedDictionary
                && Find(themedDictionary, key, theme) is { } fromTheme)
            {
                return fromTheme;
            }
        }

        if (dictionary.ThemeDictionaries.Count == 0 && dictionary.MergedDictionaries.Count == 0
            && dictionary.TryGetValue(key, out var own))
        {
            return own;
        }

        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (Find(dictionary.MergedDictionaries[i], key, theme) is { } merged)
            {
                return merged;
            }
        }

        return null;
    }
}
