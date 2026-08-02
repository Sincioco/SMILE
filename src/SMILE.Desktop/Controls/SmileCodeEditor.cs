using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using SMILE.Desktop.Highlighting;

namespace SMILE.Desktop.Controls;

public sealed class SmileCodeEditor : TextEditor
{
    public static readonly DependencyProperty DocumentTextProperty =
        DependencyProperty.Register(
            nameof(DocumentText),
            typeof(string),
            typeof(SmileCodeEditor),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnDocumentTextChanged));

    public static readonly DependencyProperty LanguageIdProperty =
        DependencyProperty.Register(
            nameof(LanguageId),
            typeof(string),
            typeof(SmileCodeEditor),
            new PropertyMetadata(string.Empty, OnLanguageIdChanged));

    private bool _isApplyingDocumentText;
    private bool _isPublishingEditorText;

    public SmileCodeEditor()
    {
        FontFamily = new FontFamily("Consolas");
        FontSize = 14;
        ShowLineNumbers = true;
        WordWrap = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.ConvertTabsToSpaces = false;
        Options.IndentationSize = 4;

        TextChanged += OnEditorTextChanged;
    }

    public string DocumentText
    {
        get => (string)GetValue(DocumentTextProperty);
        set => SetValue(DocumentTextProperty, value ?? string.Empty);
    }

    public string LanguageId
    {
        get => (string)GetValue(LanguageIdProperty);
        set => SetValue(LanguageIdProperty, value ?? string.Empty);
    }

    private static void OnDocumentTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var editor = (SmileCodeEditor)dependencyObject;
        if (editor._isPublishingEditorText)
        {
            return;
        }

        string incomingText = e.NewValue as string ?? string.Empty;
        if (string.Equals(editor.Text, incomingText, StringComparison.Ordinal))
        {
            return;
        }

        int caretOffset = Math.Min(editor.CaretOffset, incomingText.Length);
        editor._isApplyingDocumentText = true;
        try
        {
            editor.Text = incomingText;
            editor.CaretOffset = Math.Min(caretOffset, editor.Document.TextLength);
        }
        finally
        {
            editor._isApplyingDocumentText = false;
        }
    }

    private static void OnLanguageIdChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var editor = (SmileCodeEditor)dependencyObject;
        editor.SyntaxHighlighting = SyntaxHighlightingCatalog.GetDefinition(e.NewValue as string);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isApplyingDocumentText)
        {
            return;
        }

        string currentText = Text ?? string.Empty;
        if (string.Equals(DocumentText, currentText, StringComparison.Ordinal))
        {
            return;
        }

        // These guards prevent the normal WPF binding echo from assigning the
        // same whole document back into AvalonEdit after every keystroke, which
        // would reset caret position and damage the undo stack.
        _isPublishingEditorText = true;
        try
        {
            SetCurrentValue(DocumentTextProperty, currentText);
        }
        finally
        {
            _isPublishingEditorText = false;
        }
    }
}
