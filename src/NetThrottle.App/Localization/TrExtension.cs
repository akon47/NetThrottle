using System.Windows.Data;
using System.Windows.Markup;

namespace NetThrottle.App.Localization;

/// <summary>
/// XAML markup extension for localized text: <c>{loc:Tr Toolbar.Start}</c>.
/// Produces a one-way binding to the <see cref="LocalizationService"/> indexer so
/// every translated element updates live when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
