using SMILE.Engine;
using SMILE.Desktop.Controls;
using System.Diagnostics;
using System.IO;

namespace SMILE.Tests;

[TestClass]
[TestCategory("Formatting")]
[TestCategory("SourceFormatting")]
[DoNotParallelize]
public sealed class SourceFormattingTests
{
    [TestMethod]
    public void Formatter_is_idempotent_and_normalizes_global_whitespace()
    {
        const string source = "\r\n\r\nOption Explicit   \r\n\r\n\r\nDim Value As Number   \r\n\r\nValue = 1   \r\n\r\n\r\n";

        SmileFormatResult first = SmileSourceFormatter.Format(source);
        SmileFormatResult second = SmileSourceFormatter.Format(first.FormattedSource);

        Assert.IsTrue(first.Success, Join(first.Diagnostics));
        Assert.AreEqual("Option Explicit\n\nDim Value As Number\n\nValue = 1\n", first.FormattedSource);
        Assert.AreEqual(first.FormattedSource, second.FormattedSource);
        Assert.IsFalse(second.NeedsFormatting);
    }

    [TestMethod]
    public void Formatter_preserves_text_comments_and_behavior()
    {
        const string source = """
            ' Keep this learner comment exactly.
            Option Explicit
            Dim Message As Text
            Message = "  spacing, punctuation!  "  ' Keep this inline comment exactly.
            Print Message
            """;

        EvaluationResult before = new SmileEvaluator().Evaluate(source);
        SmileFormatResult format = SmileSourceFormatter.Format(source);
        EvaluationResult after = new SmileEvaluator().Evaluate(format.FormattedSource);

        Assert.IsTrue(format.Success, Join(format.Diagnostics));
        StringAssert.Contains(format.FormattedSource, "' Keep this learner comment exactly.");
        StringAssert.Contains(format.FormattedSource, "' Keep this inline comment exactly.");
        StringAssert.Contains(format.FormattedSource, "\"  spacing, punctuation!  \"");
        Assert.AreEqual(before.Output, after.Output);
        Assert.AreEqual("  spacing, punctuation!  \n", after.Output);
    }

    [TestMethod]
    public void Formatter_uses_semantic_routine_and_control_flow_boundaries()
    {
        const string source = """
            Option Explicit
            Dim Total As Number
            Total = 0
            For Total = 1 To 2

            Print Total

            End For
            Call PresentPair("A", "B")
            Sub PresentPair(ByVal LeftText As Text, ByVal RightText As Text)


            Dim Joined As Text
            Joined = LeftText + RightText
            If Joined = "AB" Then

            Print Joined

            End If

            End Sub
            """;

        SmileFormatResult result = SmileSourceFormatter.Format(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(
            """
            Option Explicit

            Dim Total As Number

            Total = 0

            For Total = 1 To 2
                Print Total
            End For

            Call PresentPair("A", "B")

            Sub PresentPair(ByVal LeftText As Text, ByVal RightText As Text)

                Dim Joined As Text

                Joined = LeftText + RightText

                If Joined = "AB" Then
                    Print Joined
                End If

            End Sub
            """ + "\n",
            result.FormattedSource);
    }

    [TestMethod]
    public void Formatter_keeps_select_legal_and_separates_cases()
    {
        const string source = """
            Option Explicit
            Dim Choice As Number
            Choice = 2
            Select Case Choice
            Case 1
            Print "one"
            Case 2
            Print "two"
            Case Else
            Print "other"
            End Select
            """;

        SmileFormatResult result = SmileSourceFormatter.Format(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        StringAssert.Contains(result.FormattedSource, "Select Case Choice\n    Case 1");
        StringAssert.Contains(result.FormattedSource, "Print \"one\"\n\n    Case 2");
        StringAssert.Contains(result.FormattedSource, "Print \"other\"\nEnd Select");
        Assert.IsTrue(new SmileTranspiler().Bind(result.FormattedSource).Success);
    }

    [TestMethod]
    public void Formatter_wraps_legal_long_call_arguments_without_splitting_text()
    {
        const string left = "This is a deliberately long first argument whose exact Text must remain unchanged.";
        const string right = "This is a deliberately long second argument whose exact Text must remain unchanged.";
        string source = $"""
            Option Explicit
            Call PresentPair("{left}", "{right}")
            Sub PresentPair(ByVal LeftText As Text, ByVal RightText As Text)
            Print LeftText; RightText
            End Sub
            """;

        SmileFormatResult result = SmileSourceFormatter.Format(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        StringAssert.Contains(
            result.FormattedSource,
            $"Call PresentPair(\n    \"{left}\",\n    \"{right}\"\n)");
        Assert.IsTrue(result.FormattedSource.Split('\n')
            .Where(line => !line.Contains(left, StringComparison.Ordinal) && !line.Contains(right, StringComparison.Ordinal))
            .All(line => line.Length <= SmileSourceFormatter.MaximumLineLength));
    }

    [TestMethod]
    public void Formatter_refuses_invalid_source_without_partial_rewrites()
    {
        const string source = "  Option Explicit\r\nIf Then\r\n  Print \"unchanged\"\r\n";

        SmileFormatResult result = SmileSourceFormatter.Format(source);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(source, result.FormattedSource);
    }

    [STATestMethod]
    public void Desktop_whole_document_format_is_one_undoable_edit()
    {
        var editor = new SmileCodeEditor
        {
            DocumentText = "Option Explicit\nDim Value As Number\nValue = 1\n"
        };
        editor.Document.UndoStack.ClearAll();
        string before = editor.Text;
        string after = SmileSourceFormatter.Format(before).FormattedSource;

        editor.ReplaceAllTextAsSingleEdit(after);

        Assert.AreEqual(after, editor.Text);
        Assert.IsTrue(editor.Document.UndoStack.CanUndo);
        editor.Document.UndoStack.Undo();
        Assert.AreEqual(before, editor.Text);
    }

    [TestMethod]
    public void Every_living_example_is_formatted()
    {
        string repository = FindRepositoryRoot();
        string[] files = Directory.GetFiles(
                Path.Combine(repository, "examples"),
                "*.smile",
                SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(repository, "tests", "CoreBasicParity"), "*.smile"))
            .Concat(Directory.GetFiles(Path.Combine(repository, "tests", "CoreBasic2Parity"), "*.smile"))
            .ToArray();

        Assert.IsNotEmpty(files);
        foreach (string file in files)
        {
            SmileFormatResult result = SmileSourceFormatter.Check(File.ReadAllText(file));
            Assert.IsTrue(result.Success, $"{file}{Environment.NewLine}{Join(result.Diagnostics)}");
            Assert.IsFalse(result.NeedsFormatting, $"Formatting required: {file}");
        }
    }

    [TestMethod]
    public async Task Cli_format_and_check_modes_share_the_production_formatter()
    {
        string repository = FindRepositoryRoot();
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"smile-format-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        string sourcePath = Path.Combine(temporaryDirectory, "sample.smile");
        await File.WriteAllTextAsync(
            sourcePath,
            "Option Explicit\r\nDim Value As Number\r\nValue = 1\r\n");

        try
        {
            Assert.AreEqual(1, await RunCliAsync(repository, sourcePath, "--check"));
            Assert.AreEqual(0, await RunCliAsync(repository, sourcePath, "--format"));
            Assert.AreEqual(0, await RunCliAsync(repository, sourcePath, "--check"));
            Assert.AreEqual(
                "Option Explicit\n\nDim Value As Number\n\nValue = 1\n",
                await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<int> RunCliAsync(string repository, string sourcePath, string mode)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(repository, "src", "SMILE.Cli", "SMILE.Cli.csproj"));
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add("Debug");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(sourcePath);
        start.ArgumentList.Add(mode);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The SMILE CLI did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string details = (await output) + (await error);
        Assert.IsTrue(process.ExitCode is 0 or 1, details);
        return process.ExitCode;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SMILE.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SMILE repository root.");
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
