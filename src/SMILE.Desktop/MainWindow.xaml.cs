using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SMILE.Desktop;

public partial class MainWindow : Window
{
    private const double MinimumOutputFontSize = 8.0;
    private const double MaximumOutputFontSize = 48.0;
    private const double OutputZoomStep = 1.0;

    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        ContentRendered += MainWindow_ContentRendered;
    }

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;

        // ContentRendered means WPF completed the first paint. Yield once at
        // background priority so pending render and input work stays ahead of
        // language-reference I/O, toolchain detection, and background transpilation.
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _viewModel.HandleInitializationException(ex);
        }
    }

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // This is strictly visual wiring: whenever diagnostics/build/run text
        // grows, keep the output pane focused on the newest line.
        OutputTextBox.ScrollToEnd();
    }

    private void OutputTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool isControlPressed =
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (!isControlPressed || e.Delta == 0)
        {
            return;
        }

        double zoomAdjustment = e.Delta > 0
            ? OutputZoomStep
            : -OutputZoomStep;
        double newFontSize = Math.Clamp(
            OutputTextBox.FontSize + zoomAdjustment,
            MinimumOutputFontSize,
            MaximumOutputFontSize);

        OutputTextBox.SetCurrentValue(Control.FontSizeProperty, newFontSize);

        // The output log is often projected while teaching. Treat Ctrl + wheel
        // as a presentation zoom gesture and never let it scroll at a limit.
        e.Handled = true;
    }
}
