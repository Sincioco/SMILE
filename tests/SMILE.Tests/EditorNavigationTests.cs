using System.Windows.Input;
using SMILE.Desktop;
using SMILE.Desktop.Controls;

namespace SMILE.Tests;

[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
public sealed class EditorNavigationTests
{
    [STATestMethod]
    public void Every_editor_installs_find_and_go_to_line_shortcuts()
    {
        var editor = new SmileCodeEditor();

        Assert.IsTrue(
            editor.TextArea.CommandBindings
                .OfType<CommandBinding>()
                .Any(binding => ReferenceEquals(binding.Command, ApplicationCommands.Find)),
            "AvalonEdit's Find command was not installed on the editor.");
        Assert.IsTrue(HasGesture(ApplicationCommands.Find, Key.F, ModifierKeys.Control));
        Assert.IsTrue(HasGesture(SmileCodeEditor.GoToLineCommand, Key.G, ModifierKeys.Control));
        Assert.IsTrue(
            editor.CommandBindings
                .OfType<CommandBinding>()
                .Any(binding => ReferenceEquals(binding.Command, SmileCodeEditor.GoToLineCommand)));
    }

    [STATestMethod]
    public void Go_to_line_moves_the_caret_to_valid_lines_and_rejects_invalid_lines()
    {
        var editor = new SmileCodeEditor
        {
            DocumentText = "first\nsecond\nthird"
        };

        Assert.IsTrue(editor.TryGoToLine(2));
        Assert.AreEqual(2, editor.TextArea.Caret.Line);
        Assert.AreEqual(editor.Document.GetLineByNumber(2).Offset, editor.CaretOffset);

        int caretOffset = editor.CaretOffset;
        Assert.IsFalse(editor.TryGoToLine(0));
        Assert.IsFalse(editor.TryGoToLine(4));
        Assert.AreEqual(caretOffset, editor.CaretOffset);
    }

    [TestMethod]
    [DataRow("1", 3, true, 1)]
    [DataRow("3", 3, true, 3)]
    [DataRow("0", 3, false, 0)]
    [DataRow("4", 3, false, 0)]
    [DataRow("2.5", 3, false, 0)]
    [DataRow("line 2", 3, false, 0)]
    [DataRow("", 3, false, 0)]
    public void Go_to_line_input_requires_a_line_inside_the_current_document(
        string text,
        int lineCount,
        bool expectedSuccess,
        int expectedLine)
    {
        bool success = GoToLineRequest.TryParse(
            text,
            lineCount,
            out int lineNumber,
            out string validationMessage);

        Assert.AreEqual(expectedSuccess, success);
        Assert.AreEqual(expectedLine, lineNumber);
        Assert.AreEqual(expectedSuccess, validationMessage.Length == 0);
    }

    private static bool HasGesture(RoutedCommand command, Key key, ModifierKeys modifiers) =>
        command.InputGestures
            .OfType<KeyGesture>()
            .Any(gesture => gesture.Key == key && gesture.Modifiers == modifiers);
}
