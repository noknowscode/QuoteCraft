using Microsoft.UI.Xaml.Data;

namespace QuoteCraft.Helpers;

/// <summary>Visible when the bound number is zero (an empty-list hint), otherwise Collapsed.</summary>
public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
