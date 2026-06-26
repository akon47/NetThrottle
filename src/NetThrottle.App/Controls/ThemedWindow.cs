using System.Windows;
using System.Windows.Input;

namespace NetThrottle.App.Controls;

/// <summary>
/// Base window that wears the custom dark chrome (<c>ThemedWindowStyle</c> from
/// the theme) and wires the caption buttons to the standard system commands.
/// The style is applied via a deferred resource reference so it resolves from
/// the window's merged theme dictionary regardless of XAML attribute order.
/// </summary>
public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "ThemedWindowStyle");

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(this)));

        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(this),
            (_, e) => e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip));

        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand,
            (_, _) => SystemCommands.RestoreWindow(this),
            (_, e) => e.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip));

        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(this)));
    }
}
