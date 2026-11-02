using Microsoft.UI.Xaml.Data;

namespace QuoteCraft.Helpers;

/// <summary>Shows an amount in the business's currency (<see cref="Money.Format"/>), the way the PDF shows it.</summary>
public class CurrencyFormatConverter : IValueConverter
{
    /// <summary>
    /// The business profile's currency: set at startup and when Settings saves.
    /// Supported: USD, CAD, EUR, GBP, AUD, MXN.
    /// </summary>
    public static string CurrencyCode { get; set; } = "USD";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var amount = value switch
        {
            decimal m => m,
            int i => i,
            long l => l,
            double d when double.IsFinite(d) => (decimal)d,
            _ => 0m,
        };
        return Money.Format(amount, CurrencyCode);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
