using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SMILE.Desktop;

public partial class GoToLineDialog : Window
{
    private readonly int _lineCount;

    public GoToLineDialog(int lineCount, int currentLine)
    {
        InitializeComponent();

        _lineCount = Math.Max(1, lineCount);
        PromptTextBlock.Text = $"Enter a line number from 1 to {_lineCount}:";
        LineNumberTextBox.Text = Math.Clamp(currentLine, 1, _lineCount)
            .ToString(CultureInfo.InvariantCulture);

        ContentRendered += (_, _) =>
        {
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
        };
    }

    public int LineNumber { get; private set; }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (!GoToLineRequest.TryParse(
                LineNumberTextBox.Text,
                _lineCount,
                out int lineNumber,
                out string validationMessage))
        {
            ValidationTextBlock.Text = validationMessage;
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
            return;
        }

        LineNumber = lineNumber;
        DialogResult = true;
    }

    private void LineNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ValidationTextBlock is not null)
        {
            ValidationTextBlock.Text = string.Empty;
        }
    }
}

internal static class GoToLineRequest
{
    public static bool TryParse(
        string? text,
        int lineCount,
        out int lineNumber,
        out string validationMessage)
    {
        int maximumLine = Math.Max(1, lineCount);
        if (!int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lineNumber) ||
            lineNumber < 1 ||
            lineNumber > maximumLine)
        {
            lineNumber = 0;
            validationMessage = $"Enter a whole number from 1 to {maximumLine}.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }
}
