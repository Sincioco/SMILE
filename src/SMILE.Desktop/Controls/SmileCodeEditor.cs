using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using SMILE.Desktop.Highlighting;

namespace SMILE.Desktop.Controls;

public sealed class SmileCodeEditor : TextEditor
{
    private const double DefaultEditorFontSize = 14.0;
    private const double MinimumEditorFontSize = 8.0;
    private const double MaximumEditorFontSize = 48.0;
    private const double EditorZoomStep = 1.0;

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
        FontSize = DefaultEditorFontSize;
        Foreground = Brushes.Black;
        ShowLineNumbers = true;
        WordWrap = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.ConvertTabsToSpaces = false;
        Options.IndentationSize = 4;

        TextChanged += OnEditorTextChanged;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        bool isControlPressed =
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (!isControlPressed || e.Delta == 0)
        {
            base.OnPreviewMouseWheel(e);
            return;
        }

        double zoomAdjustment = e.Delta > 0
            ? EditorZoomStep
            : -EditorZoomStep;
        double newFontSize = Math.Clamp(
            FontSize + zoomAdjustment,
            MinimumEditorFontSize,
            MaximumEditorFontSize);

        SetCurrentValue(FontSizeProperty, newFontSize);

        // Ctrl + mouse wheel is an editor zoom gesture, so AvalonEdit's
        // internal scroll viewer must not also move at either zoom limit.
        e.Handled = true;
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
