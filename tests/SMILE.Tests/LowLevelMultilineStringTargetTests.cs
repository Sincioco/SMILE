using System.Text.RegularExpressions;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class LowLevelMultilineStringTargetTests
{
    private const string RecursivePlacementSource = """
LET Message = ""
LET Route = 1
LET Outer = 0
LET Inner = 0
LET Nested = 0

IF Route = 0 THEN
    PRINT First branch
ELSE IF Route = 1 THEN
    SET Message = "
ElseIf
Value
"
ELSE
    SET Message = "
Else
Value
"
END IF

WHILE Outer < 1
    WHILE Inner < 1
        SET Message = "
Nested
While
"
        SET Inner = Inner + 1
    END WHILE
    SET Outer = Outer + 1
END WHILE

IF Route = 1 THEN
    WHILE Nested < 1
        SET Message = "
While
InsideIf
"
        SET Nested = Nested + 1
    END WHILE
END IF
""";

    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void Canonical_LET_Block_uses_each_low_level_targets_exact_native_data_form()
    {
        const string source = """
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
""";

        string c = Generate(source, TargetLanguage.C);
        StringAssert.Contains(
            c,
            "    const char *MultilineText =\n" +
            "        \"    Hello World!\\n\"\n" +
            "        \"    This is SMILE!\\n\"\n" +
            "        \"        How are you?\";");

        string objectiveC = Generate(source, TargetLanguage.ObjectiveC);
        StringAssert.Contains(
            objectiveC,
            "    const char *MultilineText =\n" +
            "        \"    Hello World!\\n\"\n" +
            "        \"    This is SMILE!\\n\"\n" +
            "        \"        How are you?\";");

        const string exactHex =
            "2020202048656C6C6F20576F726C64210A" +
            "202020205468697320697320534D494C45210A" +
            "2020202020202020486F772061726520796F753F";
        string cobol = Generate(source, TargetLanguage.Cobol);
        StringAssert.Contains(
            cobol,
            $"01 MultilineText PIC X(56) VALUE X\"{exactHex}\".");
        StringAssert.Contains(
            cobol,
            "01 SMILE-SET-LENGTH-0 PIC 9(9) COMP-5 VALUE 56.");

        string masm = Generate(source, TargetLanguage.MasmX64);
        StringAssert.Contains(
            masm,
            "variable0Value BYTE \"    Hello World!\", 10, " +
            "\"    This is SMILE!\", 10, \"        How are you?\"");
        StringAssert.Contains(masm, "variable0ValueLength EQU $ - variable0Value");
    }

    [TestMethod]
    public void Multiline_SET_inside_IF_inside_WHILE_keeps_structure_and_exact_data()
    {
        const string source = """
LET Message = ""
LET Count = 0
LET Ready = TRUE

WHILE Count < 1
    IF Ready = TRUE THEN
        SET Message = "
Hello
World
"
    END IF

    SET Count = Count + 1
END WHILE

PRINT {Message}
""";

        foreach (TargetLanguage language in new[]
                 {
                     TargetLanguage.C,
                     TargetLanguage.ObjectiveC
                 })
        {
            string generated = Generate(source, language);
            StringAssert.Contains(generated, "while (");
            StringAssert.Contains(generated, "if (");
            StringAssert.Contains(
                generated,
                "            Message =\n" +
                "                \"Hello\\n\"\n" +
                "                \"World\";");
        }

        string cobol = Generate(source, TargetLanguage.Cobol);
        StringAssert.Contains(cobol, "PERFORM UNTIL SMILE-WHILE-EXIT-0 = 1");
        StringAssert.Contains(cobol, "IF SMILE-IF-CONDITION-0 = 1");
        StringAssert.Contains(cobol, "MOVE X\"48656C6C6F0A576F726C64\" TO SMILE-Message");
        StringAssert.Contains(cobol, "MOVE 11 TO SMILE-SET-LENGTH-0");

        string masm = Generate(source, TargetLanguage.MasmX64);
        StringAssert.Contains(masm, "while0Condition:");
        StringAssert.Contains(masm, "; IF #1");
        StringAssert.Contains(masm, "if0End:");
        Assert.IsTrue(
            Regex.IsMatch(
                masm,
                @"(?m)^set\d+Value BYTE \""Hello\"", 10, \""World\"""),
            masm);
    }

    [TestMethod]
    public void Multiline_SET_reaches_every_low_level_recursive_structured_placement()
    {
        foreach (TargetLanguage language in new[]
                 {
                     TargetLanguage.C,
                     TargetLanguage.ObjectiveC
                 })
        {
            string generated = Generate(RecursivePlacementSource, language);
            Assert.AreEqual(
                4,
                CountOccurrences(generated, "Message =\n"),
                $"{language} missed an ELSE IF, ELSE, nested WHILE, or WHILE-inside-IF SET.");
            StringAssert.Contains(generated, "\"ElseIf\\n\"");
            StringAssert.Contains(generated, "\"Else\\n\"");
            StringAssert.Contains(generated, "\"Nested\\n\"");
            StringAssert.Contains(generated, "\"While\\n\"");
        }

        string cobol = Generate(RecursivePlacementSource, TargetLanguage.Cobol);
        Assert.AreEqual(4, CountOccurrences(cobol, " TO SMILE-Message"));
        StringAssert.Contains(cobol, "X\"456C736549660A56616C7565\"");
        StringAssert.Contains(cobol, "X\"456C73650A56616C7565\"");
        StringAssert.Contains(cobol, "X\"4E65737465640A5768696C65\"");
        StringAssert.Contains(cobol, "X\"5768696C650A496E736964654966\"");

        string masm = Generate(RecursivePlacementSource, TargetLanguage.MasmX64);
        Assert.HasCount(
            4,
            Regex.Matches(masm, @"(?m)^set\d+Value BYTE").Cast<Match>(),
            "MASM missed an ELSE IF, ELSE, nested WHILE, or WHILE-inside-IF SET.");
        StringAssert.Contains(masm, "BYTE \"ElseIf\", 10, \"Value\"");
        StringAssert.Contains(masm, "BYTE \"Else\", 10, \"Value\"");
        StringAssert.Contains(masm, "BYTE \"Nested\", 10, \"While\"");
        StringAssert.Contains(masm, "BYTE \"While\", 10, \"InsideIf\"");
    }

    [TestMethod]
    public void Low_level_multiline_data_keeps_NUL_tab_LF_and_text_after_NUL_exact()
    {
        const string source = """
LET Exact = "
A\0B
C\tD
"

PRINT {Exact}
""";

        foreach (TargetLanguage language in new[]
                 {
                     TargetLanguage.C,
                     TargetLanguage.ObjectiveC
                 })
        {
            string generated = Generate(source, language);
            StringAssert.Contains(
                generated,
                "    const char *Exact =\n" +
                "        \"A\\000B\\n\"\n" +
                "        \"C\\tD\";");
            StringAssert.Contains(generated, "size_t smileString0Length = 7;");
            StringAssert.Contains(
                generated,
                "fwrite(Exact, 1, smileString0Length, stdout);");
        }

        string cobol = Generate(source, TargetLanguage.Cobol);
        StringAssert.Contains(cobol, "VALUE X\"4100420A430944\".");
        StringAssert.Contains(cobol, "PIC 9(9) COMP-5 VALUE 7.");

        string masm = Generate(source, TargetLanguage.MasmX64);
        StringAssert.Contains(
            masm,
            "variable0Value BYTE \"A\", 0, \"B\", 10, \"C\", 9, \"D\"");
        StringAssert.Contains(masm, "variable0ValueLength EQU $ - variable0Value");
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void Known_multiline_PRINT_format_uses_adjacent_C_literals(TargetLanguage language)
    {
        string generated = Generate("PRINT {\"Alpha\\nBeta\"}", language);

        StringAssert.Contains(
            generated,
            "    printf(\n" +
            "        \"Alpha\\n\"\n" +
            "        \"Beta\\n\");");
    }

    private string Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        return NormalizePhysicalNewlines(result.GeneratedProgram!.PrimaryFile.Content);
    }

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }
}
