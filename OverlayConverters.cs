using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Valtrans;

public static class OverlayConverters
{
    public static readonly IValueConverter HasSkipReason = new HasSkipReasonConverter();
}

internal sealed class HasSkipReasonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
