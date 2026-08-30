using SMILE.Engine;
using System.IO;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
public sealed class CoreBasicGenerationTests
{
    private const string Source = """
Const Greeting = "Hello"
Dim Total As Number
Total = 0
For I = 1 To 3
    Total = Total + I
End For
Do
    Total = Total - 1
Loop Until Total = 0
If Total = 0 Then
    Print Greeting; "!"
End If
""";

    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Every_active_target_generates_a_deterministic_primary_file()
    {
        IReadOnlyList<TranspileResult> first = _transpiler.TranspileMany(Source, ActiveTargetLanguages.All);
        IReadOnlyList<TranspileResult> second = _transpiler.TranspileMany(Source, ActiveTargetLanguages.All);

        Assert.HasCount(10, first);
        foreach ((TranspileResult left, TranspileResult right) in first.Zip(second))
        {
            Assert.IsTrue(left.Success, Join(left.Diagnostics));
            Assert.AreEqual(TargetLanguageInfo.GetPrimaryFileName(left.Language), left.GeneratedProgram!.PrimaryFile.RelativePath);
            Assert.AreEqual(left.GeneratedProgram.PrimaryFile.Content, right.GeneratedProgram!.PrimaryFile.Content);
            Assert.IsGreaterThan(0, left.GeneratedProgram.PrimaryFile.Content.Length);
        }
    }

    [TestMethod]
    public async Task Every_checked_in_example_generates_all_ten_active_targets()
    {
        string examplesDirectory = FindExamplesDirectory();
        string[] examples = Directory.GetFiles(examplesDirectory, "*.smile").Order().ToArray();
        Assert.IsGreaterThanOrEqualTo(12, examples.Length);

        foreach (string example in examples)
        {
            IReadOnlyList<TranspileResult> results = _transpiler.TranspileMany(
                await File.ReadAllTextAsync(example),
                ActiveTargetLanguages.All);
            Assert.HasCount(10, results, Path.GetFileName(example));
            Assert.IsTrue(
                results.All(result => result.Success),
                Path.GetFileName(example) + Environment.NewLine +
                Join(results.SelectMany(result => result.Diagnostics)));
        }
    }

    [TestMethod]
    public void Targets_use_their_normal_structured_constructs()
    {
        Dictionary<TargetLanguage, string[]> markers = new()
        {
            [TargetLanguage.CSharp] = ["for (", "do", "if (", "Console.Write"],
            [TargetLanguage.C] = ["for (", "do", "if (", "fputs"],
            [TargetLanguage.MasmX64] = ["smile_for_", "smile_do_", "cmp", "printf"],
            [TargetLanguage.JavaScript] = ["for (", "do {", "if (", "process.stdout.write"],
            [TargetLanguage.Java] = ["for (", "do {", "if (", "System.out.print"],
            [TargetLanguage.Cobol] = ["PERFORM VARYING", "PERFORM WITH TEST AFTER", "IF ", "DISPLAY"],
            [TargetLanguage.ObjectiveC] = ["for (", "do", "if (", "fputs"],
            [TargetLanguage.Swift] = ["for ", "repeat {", "if ", "print("],
            [TargetLanguage.Python] = ["for ", "while True:", "if ", "print("],
            [TargetLanguage.Cpp] = ["for (", "do", "if (", "std::cout"]
        };

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            string generated = _transpiler.Transpile(Source, language).GeneratedProgram!.PrimaryFile.Content;
            foreach (string marker in markers[language])
            {
                StringAssert.Contains(generated, marker, language.ToString());
            }
        }
    }

    [TestMethod]
    public void C_helper_is_emitted_only_when_Text_concatenation_needs_it()
    {
        string simple = _transpiler.Transpile("Print \"Hello\"", TargetLanguage.C)
            .GeneratedProgram!.PrimaryFile.Content;
        string concatenated = _transpiler.Transpile("Print \"Hello\" + \"!\"", TargetLanguage.C)
            .GeneratedProgram!.PrimaryFile.Content;

        Assert.IsFalse(simple.Contains("smile_text_concat", StringComparison.Ordinal));
        StringAssert.Contains(concatenated, "smile_text_concat");
    }

    [TestMethod]
    public void Python_typed_exit_helper_is_emitted_only_for_a_loop_that_needs_it()
    {
        string ordinary = _transpiler.Transpile(
            "For I = 1 To 2\n    Print I\nEnd For",
            TargetLanguage.Python).GeneratedProgram!.PrimaryFile.Content;
        string nearestExit = _transpiler.Transpile(
            "For I = 1 To 2\n    Exit For\nEnd For",
            TargetLanguage.Python).GeneratedProgram!.PrimaryFile.Content;
        string crossKindExit = _transpiler.Transpile(
            "For I = 1 To 2\n    Do\n        Exit For\n    Loop\nEnd For",
            TargetLanguage.Python).GeneratedProgram!.PrimaryFile.Content;

        Assert.IsFalse(ordinary.Contains("_SmileExitLoop", StringComparison.Ordinal));
        Assert.IsFalse(nearestExit.Contains("_SmileExitLoop", StringComparison.Ordinal));
        StringAssert.Contains(nearestExit, "break");
        StringAssert.Contains(crossKindExit, "class _SmileExitLoop1(Exception):");
    }

    [TestMethod]
    public void MASM_runtime_declarations_are_emitted_only_for_features_that_need_them()
    {
        string simple = _transpiler.Transpile("Value = 1", TargetLanguage.MasmX64)
            .GeneratedProgram!.PrimaryFile.Content;
        string printed = _transpiler.Transpile("Print 1", TargetLanguage.MasmX64)
            .GeneratedProgram!.PrimaryFile.Content;
        string indexed = _transpiler.Transpile(
            "Dim Values[2] As Number\nIndex = 0\nPrint Values[Index]",
            TargetLanguage.MasmX64).GeneratedProgram!.PrimaryFile.Content;
        string concatenated = _transpiler.Transpile(
            "Print \"A\" + \"B\"",
            TargetLanguage.MasmX64).GeneratedProgram!.PrimaryFile.Content;
        string compared = _transpiler.Transpile(
            "Print \"A\" = \"B\"",
            TargetLanguage.MasmX64).GeneratedProgram!.PrimaryFile.Content;

        Assert.IsFalse(simple.Contains("printf PROTO", StringComparison.Ordinal));
        Assert.IsFalse(simple.Contains("strcmp PROTO", StringComparison.Ordinal));
        Assert.IsFalse(simple.Contains("sprintf PROTO", StringComparison.Ordinal));
        Assert.IsFalse(simple.Contains("malloc PROTO", StringComparison.Ordinal));
        Assert.IsFalse(simple.Contains("smile_bounds_fail", StringComparison.Ordinal));
        Assert.IsFalse(simple.Contains("includelib msvcrt.lib", StringComparison.Ordinal));

        StringAssert.Contains(printed, "printf PROTO");
        StringAssert.Contains(indexed, "smile_bounds_fail PROC");
        StringAssert.Contains(concatenated, "malloc PROTO");
        StringAssert.Contains(concatenated, "sprintf PROTO");
        StringAssert.Contains(compared, "strcmp PROTO");
    }

    [TestMethod]
    public void End_Program_omits_unreachable_following_source_in_every_target()
    {
        const string source = "Print \"before\"\nEnd Program\nPrint \"unreachable marker\"";

        foreach (TranspileResult result in _transpiler.TranspileMany(source, ActiveTargetLanguages.All))
        {
            Assert.IsTrue(result.Success, result.Language.ToString());
            Assert.IsFalse(
                result.GeneratedProgram!.PrimaryFile.Content.Contains("unreachable marker", StringComparison.Ordinal),
                result.Language.ToString());
        }
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln")))
            {
                return Path.Combine(directory.FullName, "examples");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the examples directory.");
    }
}
