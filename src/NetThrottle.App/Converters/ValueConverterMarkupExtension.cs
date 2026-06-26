using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace NetThrottle.App.Converters;

/// <summary>
/// Base for value converters that double as XAML markup extensions, so they can
/// be used inline — <c>{conv:BoolToVisibilityConverter}</c> — with no
/// <c>ResourceDictionary</c> entry. Converters are stateless, so a single shared
/// instance per type is handed out by <see cref="ProvideValue"/>.
/// </summary>
/// <typeparam name="T">The concrete converter (curiously recurring pattern).</typeparam>
public abstract class ValueConverterMarkupExtension<T> : MarkupExtension, IValueConverter
    where T : ValueConverterMarkupExtension<T>, new()
{
    private static readonly T Shared = new();

    public sealed override object ProvideValue(IServiceProvider serviceProvider) => Shared;

    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    /// <summary>One-way by default; override for two-way bindings.</summary>
    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{typeof(T).Name} is a one-way converter.");
}
