using SMILE.Engine;
using System.IO;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("TextGameFoundation")]
public sealed class TextGameFoundationTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Key_constants_have_one_stable_cross_target_number_table()
    {
        EvaluationResult result = _evaluator.Evaluate(
            "Print KEY_NONE; KEY_W; KEY_A; KEY_S; KEY_D; KEY_UP; KEY_DOWN; KEY_LEFT; KEY_RIGHT; KEY_ENTER; KEY_ESCAPE; KEY_SPACE; KEY_1; KEY_2; KEY_OTHER; KEY_3; KEY_TAB; KEY_4");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("0123410111213141516171819202122\n", result.Output);
    }

    [TestMethod]
    public void Injected_host_makes_keys_frames_wait_clock_and_random_deterministic()
    {
        var host = new ScriptedSmileEvaluationHost(
            keys: new[] { SmileKeyCodes.Left },
            randomValues: new long[] { 6 },
            initialMilliseconds: 100);
        const string source = """
Option Explicit
Dim KeyCode As Number
Dim Roll As Number
Print "before"
Clear Screen
Get Key KeyCode
Print KeyCode
Print Timer()
Wait -10 Milliseconds
Wait 50 Milliseconds
Print Timer()
Random Roll From 1 To 6
Print Roll
""";

        EvaluationResult result = _evaluator.Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 100));

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("before\n12\n100\n150\n6\n", result.Output);
        CollectionAssert.AreEqual(new long[] { 0, 50 }, host.Waits.ToArray());
        CollectionAssert.AreEqual(new[] { "before\n" }, host.ScreenFrames.ToArray());
    }

    [TestMethod]
    public void Get_Key_consumes_at_most_one_event_and_Timer_never_moves_backward()
    {
        var host = new ScriptedSmileEvaluationHost(
            keys: new[] { SmileKeyCodes.W, SmileKeyCodes.D },
            initialMilliseconds: 75);
        const string source = """
Option Explicit
Dim First As Number
Dim Second As Number
Dim Third As Number
Get Key First
Get Key Second
Get Key Third
Print First; Second; Third
Print Timer()
Wait 25 Milliseconds
Print Timer()
""";

        EvaluationResult result = _evaluator.Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 100));

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("140\n75\n100\n", result.Output);
        Assert.AreEqual(0, host.RemainingKeyCount);
    }

    [TestMethod]
    public void Evaluator_budget_stops_an_unbounded_game_loop_deterministically()
    {
        EvaluationResult result = _evaluator.Evaluate(
            "Do\nLoop",
            new SmileEvaluationOptions(StatementBudget: 12));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("SMILER1222", result.RuntimeError?.Code);
    }

    [TestMethod]
    public void Rank_two_arrays_default_and_reset_local_Number_Boolean_and_Text_cells()
    {
        const string source = """
Option Explicit
Dim Numbers[2, 3] As Number
Dim Flags[2, 3] As Boolean
Dim Words[2, 3] As Text
Print Numbers[1, 2]; Flags[1, 2]; "["; Words[1, 2]; "]"
Numbers[1, 2] = 9
Flags[1, 2] = True
Words[1, 2] = "ready"
Print Numbers[1, 2]; Flags[1, 2]; Words[1, 2]
Print Fresh(); Fresh()
Function Fresh() As Text
    Dim LocalWords[2, 2] As Text
    LocalWords[1, 1] = LocalWords[1, 1] + "X"
    Return LocalWords[1, 1]
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("0False[]\n9Trueready\nXX\n", result.Output);
    }

    [TestMethod]
    public void Rank_two_assignment_evaluates_both_indexes_before_checks_and_rhs_after_checks()
    {
        const string source = """
Option Explicit
Dim Grid[2, 2] As Number
Grid[Mark(2, "X"), Mark(0, "Y")] = Mark(7, "V")
Function Mark(Value As Number, Label As Text) As Number
    Print Label;
    Return Value
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("XY", result.Output);
        Assert.AreEqual("SMILER1210", result.RuntimeError?.Code);
    }

    [TestMethod]
    [DataRow("Dim Grid[] As Number", "cannot be empty")]
    [DataRow("Dim Grid[2, 2, 2] As Number", "at most two")]
    [DataRow("Dim Grid[2, 2] As Number\nPrint Grid[0]", "requires 2 index")]
    [DataRow("Dim Grid[2] As Number\nPrint Grid[0, 0]", "requires 1 index")]
    [DataRow("Dim Grid[2, 2] As Number\nPrint Grid[0, True]", "must have type Number")]
    [DataRow("Dim Grid[2, 2] As Number\nPrint Grid[0, 2]", "dimension 2")]
    [DataRow("Dim Width As Number\nDim Grid[Width, 2] As Number", "Constant expression")]
    public void Invalid_rank_two_forms_receive_focused_diagnostics(string source, string expected)
    {
        BindResult result = _transpiler.Bind(source);
        Assert.IsFalse(result.Success, source);
        StringAssert.Contains(Join(result.Diagnostics), expected, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("Dim Grid[2, 2] As Number\nPrint Grid[0 1]")]
    [DataRow("Dim Grid[2, 2] As Number\nPrint Grid[0, 1")]
    [DataRow("Get Key")]
    [DataRow("Get Key KEY_W")]
    [DataRow("Dim Flag As Boolean\nGet Key Flag")]
    [DataRow("Wait 5 Seconds")]
    [DataRow("Random Value 1 To 2")]
    [DataRow("Print KEY_ESC")]
    [DataRow("Game Window 80 By 25")]
    public void Malformed_or_excluded_text_game_forms_are_rejected(string source)
    {
        ParseResult parse = _transpiler.Parse(source);
        BindResult bind = _transpiler.Bind(source);
        Assert.IsTrue(
            parse.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
            bind.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            source);
    }

    [TestMethod]
    public void Parser_recovers_after_a_malformed_two_index_access()
    {
        const string source = "Dim Grid[2, 2] As Number\nPrint Grid[0 1]\nPrint 42";
        ParseResult result = _transpiler.Parse(source);

        Assert.IsNotNull(result.Program);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(result.Program.Statements.OfType<CorePrintStatementSyntax>().Any(
            print => print.Span.Start >= source.LastIndexOf("Print 42", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Abs_evaluator_overflow_and_generated_native_intrinsics_follow_the_documented_edge_policy()
    {
        const string source = """
Option Explicit
Dim Value As Number
Value = -9223372036854775807
Value = Value - 1
Print Abs(Value)
""";

        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsFalse(evaluation.Success);
        Assert.AreEqual("SMILER1206", evaluation.RuntimeError?.Code);

        string java = _transpiler.Transpile(source, TargetLanguage.Java).GeneratedProgram!.PrimaryFile.Content;
        string python = _transpiler.Transpile(source, TargetLanguage.Python).GeneratedProgram!.PrimaryFile.Content;
        string swift = _transpiler.Transpile("Print Abs(-2)", TargetLanguage.Swift).GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(java, "Math.abs(Value)");
        StringAssert.Contains(python, "abs(Value)");
        Assert.IsFalse(java.Contains("smileAbs", StringComparison.Ordinal));
        Assert.IsFalse(python.Contains("smile_abs", StringComparison.Ordinal));
        StringAssert.Contains(swift, "Swift.abs((-2))");
        Assert.IsFalse(swift.Contains("import Foundation", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("import WinSDK", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Random_reversed_bounds_fail_after_left_to_right_bound_evaluation()
    {
        const string source = """
Option Explicit
Dim Result As Number
Random Result From Mark(5, "L") To Mark(2, "U")
Function Mark(Value As Number, Label As Text) As Number
    Print Label;
    Return Value
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("LU", result.Output);
        Assert.AreEqual("SMILER1221", result.RuntimeError?.Code);
    }

    [TestMethod]
    public void Every_target_emits_native_rank_two_storage_and_only_used_runtime_support()
    {
        const string source = """
Option Explicit
Dim Grid[2, 3] As Number
Dim KeyCode As Number
Grid[1, 2] = 7
Get Key KeyCode
Print Grid[1, 2]; KeyCode
""";
        Dictionary<TargetLanguage, string[]> markers = new()
        {
            [TargetLanguage.CSharp] = ["long[,] Grid", "SmileGetKey"],
            [TargetLanguage.C] = ["Grid[2][3]", "_kbhit"],
            [TargetLanguage.MasmX64] = ["QWORD 6 DUP", "_kbhit PROTO"],
            [TargetLanguage.JavaScript] = ["Array.from({ length: 2 }", "setRawMode(true)"],
            [TargetLanguage.Java] = ["long[][] Grid", "java.lang.foreign"],
            [TargetLanguage.Cobol] = ["OCCURS 2 TIMES", "OCCURS 3 TIMES"],
            [TargetLanguage.ObjectiveC] = ["Grid[2][3]", "_kbhit"],
            [TargetLanguage.Swift] = ["[[Int64]]", "_kbhit"],
            [TargetLanguage.Python] = ["for _ in range(3)", "msvcrt.kbhit"],
            [TargetLanguage.Cpp] = ["std::array<std::array<std::int64_t, 3>, 2>", "_kbhit"]
        };

        foreach (TranspileResult result in _transpiler.TranspileMany(source, ActiveTargetLanguages.All))
        {
            Assert.IsTrue(result.Success, result.Language + ": " + Join(result.Diagnostics));
            string generated = result.GeneratedProgram!.PrimaryFile.Content;
            foreach (string marker in markers[result.Language])
            {
                StringAssert.Contains(generated, marker, result.Language.ToString());
            }
            Assert.IsFalse(generated.Contains("smile_wait", StringComparison.OrdinalIgnoreCase), result.Language.ToString());
            Assert.IsFalse(generated.Contains("smile_random", StringComparison.OrdinalIgnoreCase), result.Language.ToString());
        }
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Cobol_preserves_the_logical_length_of_dynamic_Text_without_trimming_game_cells()
    {
        const string source = """
Option Explicit
Dim Cell As Text
Dim Cells[2] As Text
Cell = " "
Cells[0] = " A "
Print "["; Cell; Cells[0]; Echo(" x "); "a" + " " + "b"; "]"
Function Echo(Value As Text) As Text
    Return Value
End Function
""";

        TranspileResult result = _transpiler.Transpile(source, TargetLanguage.Cobol);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        string generated = result.GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(generated, "STUDENT-Cell-LENGTH PIC S9(18) COMP-5");
        StringAssert.Contains(generated, "STUDENT-Cells-LENGTH-ITEM");
        StringAssert.Contains(generated, "SMILE-RETURN-LENGTH");
        StringAssert.Contains(generated, "DISPLAY STUDENT-Cell(1:STUDENT-Cell-LENGTH) WITH NO ADVANCING");
        Assert.IsFalse(
            generated.Contains("FUNCTION TRIM(STUDENT-Cell, TRAILING)", StringComparison.Ordinal),
            "Dynamic Text must use its logical length; trimming destroys meaningful game spaces.");
    }

    [TestMethod]
    public void Generated_console_support_uses_normal_dependency_free_target_facilities()
    {
        const string source = "Dim KeyCode As Number\nGet Key KeyCode\nWait 1 Milliseconds\nPrint KeyCode";
        Dictionary<TargetLanguage, string[]> forbidden = new()
        {
            [TargetLanguage.C] = ["system(\"cls\")"],
            [TargetLanguage.JavaScript] = ["Atomics.wait", "node_modules", "npm"],
            [TargetLanguage.Java] = ["com.sun.jna", "readLine("],
            [TargetLanguage.Python] = ["if __name__", "def main("],
            [TargetLanguage.Cpp] = ["system(\"cls\")"]
        };

        foreach ((TargetLanguage language, string[] markers) in forbidden)
        {
            string generated = _transpiler.Transpile(source, language).GeneratedProgram!.PrimaryFile.Content;
            foreach (string marker in markers)
            {
                Assert.IsFalse(generated.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    $"{language} unexpectedly emitted {marker}.");
            }
        }
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Python_routines_declare_globals_written_by_Get_Key_and_Random()
    {
        const string source = """
Option Explicit
Dim KeyCode As Number
Dim Roll As Number
Call Poll()
Sub Poll()
    Get Key KeyCode
    Random Roll From 1 To 6
End Sub
""";

        string generated = _transpiler.Transpile(source, TargetLanguage.Python)
            .GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(generated, "global KeyCode, Roll");
    }

    [TestMethod]
    public void Swift_parameters_targeted_by_Get_Key_or_Random_are_mutable_local_copies()
    {
        const string source = """
Sub Poll(KeyCode As Number, Roll As Number)
    Get Key KeyCode
    Random Roll From 1 To 6
End Sub
""";

        string generated = _transpiler.Transpile(source, TargetLanguage.Swift)
            .GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(generated, "var KeyCode: Int64");
        StringAssert.Contains(generated, "var Roll: Int64");
    }

    [TestMethod]
    public void Conventional_targets_put_main_before_user_routines_and_helpers()
    {
        const string source = """
Option Explicit
Dim Values[2] As Number
Call Teach()
Sub Teach()
    Print Values[0]
End Sub
""";
        Dictionary<TargetLanguage, (string Main, string Routine, string Helper)> markers = new()
        {
            [TargetLanguage.CSharp] = ("private static void Main()", "private static void Teach()", "private static int SmileIndex"),
            [TargetLanguage.C] = ("int main(void)\n{", "static void Teach", "static size_t smile_index"),
            [TargetLanguage.MasmX64] = ("main PROC", "_smile_Teach PROC", "smile_bounds_fail PROC"),
            [TargetLanguage.Java] = ("public static void main", "private static void Teach", "private static int smileIndex"),
            [TargetLanguage.ObjectiveC] = ("int main(void)\n{", "static void Teach", "static size_t smile_index"),
            [TargetLanguage.Cpp] = ("int main()\n{", "static void Teach", "static std::size_t smile_index")
        };

        foreach ((TargetLanguage language, (string main, string routine, string helper)) in markers)
        {
            string generated = _transpiler.Transpile(source, language).GeneratedProgram!.PrimaryFile.Content;
            int mainBody = generated.IndexOf(main, StringComparison.Ordinal);
            int routineBody = generated.LastIndexOf(routine, StringComparison.Ordinal);
            int helperBody = generated.LastIndexOf(helper, StringComparison.Ordinal);
            Assert.IsLessThan(helperBody, routineBody, language.ToString());
            Assert.IsLessThan(routineBody, mainBody, language.ToString());
        }
    }

    [TestMethod]
    public void Node_async_main_is_first_and_wait_propagates_through_called_routines()
    {
        const string source = """
Call Tick()
Sub Tick()
    Wait 0 Milliseconds
End Sub
""";
        string generated = _transpiler.Transpile(source, TargetLanguage.JavaScript)
            .GeneratedProgram!.PrimaryFile.Content;

        int main = generated.IndexOf("async function main()", StringComparison.Ordinal);
        int routine = generated.IndexOf("async function Tick()", StringComparison.Ordinal);
        int helper = generated.IndexOf("function smileWait", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, main);
        Assert.IsLessThan(routine, main);
        Assert.IsLessThan(helper, routine);
        StringAssert.Contains(generated, "await Tick()");
        StringAssert.Contains(generated, "await smileWait(0n)");
    }

    [TestMethod]
    public async Task Snake_script_starts_turns_eats_grows_collides_and_exits()
    {
        string source = await ReadExample("text-snake.smile");
        var host = new ScriptedSmileEvaluationHost(
            randomValues: new long[] { 6, 6, 10, 4 },
            timedKeys:
            [
                new(0, SmileKeyCodes.Enter),
                new(20, SmileKeyCodes.Up),
                new(900, SmileKeyCodes.Escape)
            ]);

        EvaluationResult result = _evaluator.Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 600_000));

        Assert.IsTrue(result.Success, result.RuntimeError?.ToString());
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("Score: 10", StringComparison.Ordinal)));
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("Trail ended", StringComparison.Ordinal)));
        Assert.AreEqual(0, host.RemainingKeyCount);
    }

    [TestMethod]
    public async Task Maze_script_collects_pellets_updates_enemy_loses_lives_and_exits()
    {
        string source = await ReadExample("text-maze-muncher.smile");
        var host = new ScriptedSmileEvaluationHost(
            randomValues: new long[] { 4, 1, 1, 1 },
            timedKeys:
            [
                new(0, SmileKeyCodes.Enter),
                new(20, SmileKeyCodes.D),
                new(40, SmileKeyCodes.D),
                new(240, SmileKeyCodes.D),
                new(260, SmileKeyCodes.D),
                new(280, SmileKeyCodes.D),
                new(340, SmileKeyCodes.Escape)
            ]);

        EvaluationResult result = _evaluator.Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 800_000));

        Assert.IsTrue(result.Success, result.RuntimeError?.ToString());
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("Lanterns: 2", StringComparison.Ordinal)));
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("last lantern", StringComparison.Ordinal)));
        Assert.AreEqual(0, host.RemainingRandomCount, "The bounded enemy update should consume its scripted choices.");
    }

    [TestMethod]
    public async Task Falling_blocks_script_moves_rotates_locks_clears_a_row_spawns_and_exits()
    {
        string source = await ReadExample("text-falling-blocks.smile");
        var events = new List<SmileTimedKeyEvent> { new(0, SmileKeyCodes.Enter) };
        for (int time = 20; time <= 400; time += 20)
        {
            events.Add(new SmileTimedKeyEvent(time, SmileKeyCodes.S));
        }
        events.Add(new SmileTimedKeyEvent(420, SmileKeyCodes.Up));
        events.Add(new SmileTimedKeyEvent(440, SmileKeyCodes.Left));
        events.Add(new SmileTimedKeyEvent(500, SmileKeyCodes.Escape));
        var host = new ScriptedSmileEvaluationHost(
            randomValues: new long[] { 2, 3 },
            timedKeys: events);

        EvaluationResult result = _evaluator.Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 1_200_000));

        Assert.IsTrue(result.Success, result.RuntimeError?.ToString());
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("Rows: 1", StringComparison.Ordinal)));
        Assert.IsTrue(host.ScreenFrames.Any(frame => frame.Contains("Score: 119", StringComparison.Ordinal)));
        Assert.AreEqual(0, host.RemainingRandomCount, "Locking should spawn the scripted next family.");
    }

    private static async Task<string> ReadExample(string fileName) =>
        await File.ReadAllTextAsync(Path.Combine(FindExamplesDirectory(), fileName));

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

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
