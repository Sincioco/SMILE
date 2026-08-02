using System.Windows;
using System.Windows.Controls;

namespace SMILE.Desktop;

public partial class MainWindow : Window
{
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
}
