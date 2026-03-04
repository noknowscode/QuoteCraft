using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace QuoteCraft.Helpers;

public class StatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString() ?? string.Empty;
        var key = $"Status{status}BackgroundBrush";

        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
            return brush;

        // Fallback to Draft style
        if (Application.Current.Resources.TryGetValue("StatusDraftBackgroundBrush", out var fallback) && fallback is SolidColorBrush fb)
            return fb;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 244, 246));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public class StatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString() ?? string.Empty;
        var key = $"Status{status}ForegroundBrush";

        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
            return brush;

        // Fallback to Draft style
        if (Application.Current.Resources.TryGetValue("StatusDraftForegroundBrush", out var fallback) && fallback is SolidColorBrush fb)
            return fb;

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
