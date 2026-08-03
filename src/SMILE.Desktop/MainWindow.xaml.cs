using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        Loaded += async (_, _) =>
        {
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                _viewModel.HandleInitializationException(ex);
            }
        };
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
