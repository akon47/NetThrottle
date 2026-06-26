using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetThrottle.App.Converters;

/// <summary>
/// <see cref="bool"/> → <see cref="Visibility"/>: <c>true</c> shows, <c>false</c>
/// collapses. Pass <c>Invert</c> as the converter parameter to flip the mapping.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : ValueConverterMarkupExtension<BoolToVisibilityConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is true ^ IsInverted(parameter);
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public override object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ IsInverted(parameter);

    private static bool IsInverted(object? parameter) =>
        parameter is string flag && flag.Equals("Invert", StringComparison.OrdinalIgnoreCase);
}
