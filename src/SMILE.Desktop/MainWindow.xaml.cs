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
    private TargetPaneViewModel? _maximizedTargetPane;

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

    private void TargetPaneMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TargetPaneViewModel pane })
        {
            return;
        }

        if (ReferenceEquals(_maximizedTargetPane, pane))
        {
            RestoreTargetQuadrants();
            return;
        }

        if (_maximizedTargetPane is not null)
        {
            RestoreTargetQuadrants();
        }

        ContentControl? selectedHost = GetTargetPaneHost(pane);
        if (selectedHost is null)
        {
            return;
        }

        SourcePaneHost.Visibility = Visibility.Collapsed;
        Pane1Host.Visibility = ReferenceEquals(selectedHost, Pane1Host)
            ? Visibility.Visible
            : Visibility.Collapsed;
        Pane2Host.Visibility = ReferenceEquals(selectedHost, Pane2Host)
            ? Visibility.Visible
            : Visibility.Collapsed;
        Pane3Host.Visibility = ReferenceEquals(selectedHost, Pane3Host)
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalEditorSplitter.Visibility = Visibility.Collapsed;
        HorizontalEditorSplitter.Visibility = Visibility.Collapsed;

        // Keep the existing ContentControl and AvalonEdit instance in place.
        // Changing only its Grid coordinates preserves the learner's caret,
        // selection, undo history, scroll position, and zoom while the pane
        // temporarily occupies the complete four-quadrant content area.
        Grid.SetRow(selectedHost, 0);
        Grid.SetColumn(selectedHost, 0);
        Grid.SetRowSpan(selectedHost, 3);
        Grid.SetColumnSpan(selectedHost, 3);
        Panel.SetZIndex(selectedHost, 1);

        pane.IsMaximized = true;
        _maximizedTargetPane = pane;
    }

    private void RestoreTargetQuadrants()
    {
        if (_maximizedTargetPane is null)
        {
            return;
        }

        ContentControl? selectedHost = GetTargetPaneHost(_maximizedTargetPane);
        if (selectedHost is not null)
        {
            (int row, int column) = GetTargetPanePosition(_maximizedTargetPane);
            Grid.SetRow(selectedHost, row);
            Grid.SetColumn(selectedHost, column);
            Grid.SetRowSpan(selectedHost, 1);
            Grid.SetColumnSpan(selectedHost, 1);
            Panel.SetZIndex(selectedHost, 0);
        }

        SourcePaneHost.Visibility = Visibility.Visible;
        Pane1Host.Visibility = Visibility.Visible;
        Pane2Host.Visibility = Visibility.Visible;
        Pane3Host.Visibility = Visibility.Visible;
        VerticalEditorSplitter.Visibility = Visibility.Visible;
        HorizontalEditorSplitter.Visibility = Visibility.Visible;

        _maximizedTargetPane.IsMaximized = false;
        _maximizedTargetPane = null;
    }

    private ContentControl? GetTargetPaneHost(TargetPaneViewModel pane)
    {
        if (ReferenceEquals(pane, _viewModel.Pane1))
        {
            return Pane1Host;
        }

        if (ReferenceEquals(pane, _viewModel.Pane2))
        {
            return Pane2Host;
        }

        return ReferenceEquals(pane, _viewModel.Pane3) ? Pane3Host : null;
    }

    private (int Row, int Column) GetTargetPanePosition(TargetPaneViewModel pane) =>
        ReferenceEquals(pane, _viewModel.Pane1)
            ? (0, 2)
            : ReferenceEquals(pane, _viewModel.Pane2)
                ? (2, 0)
                : (2, 2);
}
