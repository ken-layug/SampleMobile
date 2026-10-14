using System.Globalization;

namespace SampleMobile.Mobile.Converters;

/// <summary>Negates a boolean binding (for example to show a placeholder while content is hidden).</summary>
public sealed class InvertedBoolConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
