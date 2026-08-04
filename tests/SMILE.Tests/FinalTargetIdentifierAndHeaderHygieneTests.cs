using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class FinalTargetIdentifierAndHeaderHygieneTests
{
    private static readonly string[] FixedWidthIntegerMacroNames =
    {
        "INT8_MIN", "INT8_MAX", "UINT8_MAX",
        "INT16_MIN", "INT16_MAX", "UINT16_MAX",
        "INT32_MIN", "INT32_MAX", "UINT32_MAX",
        "INT64_MIN", "INT64_MAX", "UINT64_MAX",
        "INT_LEAST8_MIN", "INT_LEAST8_MAX", "UINT_LEAST8_MAX",
        "INT_LEAST16_MIN", "INT_LEAST16_MAX", "UINT_LEAST16_MAX",
        "INT_LEAST32_MIN", "INT_LEAST32_MAX", "UINT_LEAST32_MAX",
        "INT_LEAST64_MIN", "INT_LEAST64_MAX", "UINT_LEAST64_MAX",
        "INT_FAST8_MIN", "INT_FAST8_MAX", "UINT_FAST8_MAX",
        "INT_FAST16_MIN", "INT_FAST16_MAX", "UINT_FAST16_MAX",
        "INT_FAST32_MIN", "INT_FAST32_MAX", "UINT_FAST32_MAX",
        "INT_FAST64_MIN", "INT_FAST64_MAX", "UINT_FAST64_MAX",
        "INTPTR_MIN", "INTPTR_MAX", "UINTPTR_MAX",
        "INTMAX_MIN", "INTMAX_MAX", "UINTMAX_MAX",
        "PTRDIFF_MIN", "PTRDIFF_MAX", "SIG_ATOMIC_MIN", "SIG_ATOMIC_MAX",
        "SIZE_MAX", "WCHAR_MIN", "WCHAR_MAX", "WINT_MIN", "WINT_MAX",
        "INT8_C", "UINT8_C", "INT16_C", "UINT16_C", "INT32_C", "UINT32_C",
        "INT64_C", "UINT64_C", "INTMAX_C", "UINTMAX_C"
    };

    private const string MacroCollisionSource = """
LET INT64_MAX = 49
LET INT64_C = 50
LET UINT64_MAX = 51
LET SIZE_MAX = 52
LET Wide = 5000000000

PRINT {INT64_MAX}
PRINT {INT64_C}
PRINT {UINT64_MAX}
PRINT {SIZE_MAX}
PRINT {Wide}
""";

    private const string ReservedUnderscoreSource = """
LET __internal = 1
LET _Upper = 2
LET user__value = 3
LET A__B = 4
LET value__ = 5
LET _user = 6
LET user_value = 7

PRINT {__internal}
PRINT {_Upper}
PRINT {user__value}
PRINT {A__B}
PRINT {value__}
PRINT {_user}
PRINT {user_value}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Cpp)]
    public void C_family_fixed_width_macro_names_are_mapped_consistently_and_deterministically(
        TargetLanguage language)
    {
        string source = BuildCompleteMacroFamilySource();
        string first = Generate(source, language).PrimaryFile.Content;
        string second = Generate(source, language).PrimaryFile.Content;
        string wideType = language is TargetLanguage.Cpp ? "std::int64_t" : "int64_t";
        string wideHeader = language is TargetLanguage.Cpp ? "#include <cstdint>" : "#include <stdint.h>";

        Assert.AreEqual(first, second);
        StringAssert.Contains(first, wideHeader);
        foreach (string name in FixedWidthIntegerMacroNames)
        {
            StringAssert.Contains(first, $"{wideType} _smile_{name} =");
            Assert.IsFalse(first.Contains($"{wideType} {name} =", StringComparison.Ordinal));
            Assert.IsGreaterThanOrEqualTo(2, CountOccurrences(first, $"_smile_{name}"), name);
        }
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Installed_c_family_targets_run_macro_collision_program_and_match_evaluator(
        TargetLanguage language)
    {
        await AssertInstalledTargetMatchesEvaluator(MacroCollisionSource, language);
    }

    [TestMethod]
    public void Cpp_maps_every_implementation_reserved_double_underscore_without_broadening_C_rules()
    {
        string cpp = Generate(ReservedUnderscoreSource, TargetLanguage.Cpp).PrimaryFile.Content;

        StringAssert.Contains(cpp, "int _smile_underscore_underscore_internal = 1;");
        StringAssert.Contains(cpp, "int _smile_underscore_Upper = 2;");
        StringAssert.Contains(cpp, "int _smile_user_underscore_underscore_value = 3;");
        StringAssert.Contains(cpp, "int _smile_A_underscore_underscore_B = 4;");
        StringAssert.Contains(cpp, "int _smile_value_underscore_underscore = 5;");
        StringAssert.Contains(cpp, "int _user = 6;");
        StringAssert.Contains(cpp, "int user_value = 7;");
        Assert.IsFalse(cpp.Contains(" __", StringComparison.Ordinal));
        Assert.IsFalse(cpp.Contains("user__value", StringComparison.Ordinal));
        Assert.IsFalse(cpp.Contains("A__B", StringComparison.Ordinal));
        Assert.IsFalse(cpp.Contains("value__", StringComparison.Ordinal));

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(ReservedUnderscoreSource, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "int _smile___internal = 1;");
            StringAssert.Contains(generated, "int _smile__Upper = 2;");
            StringAssert.Contains(generated, "int user__value = 3;");
            StringAssert.Contains(generated, "int A__B = 4;");
            StringAssert.Contains(generated, "int value__ = 5;");
            StringAssert.Contains(generated, "int _user = 6;");
            StringAssert.Contains(generated, "int user_value = 7;");
        }
    }

    [TestMethod]
    public async Task Installed_cpp_runs_double_underscore_program_and_matches_evaluator()
    {
        await AssertInstalledTargetMatchesEvaluator(ReservedUnderscoreSource, TargetLanguage.Cpp);
    }

    [TestMethod]
    public void Cpp_direct_streaming_uses_only_the_facilities_its_output_requires()
    {
        string template = Generate(
            "PRINT Age={49}, Adult={TRUE}",
            TargetLanguage.Cpp).PrimaryFile.Content;
        string literal = Generate("PRINT \"Hello\"", TargetLanguage.Cpp).PrimaryFile.Content;
        string wide = Generate("LET Wide = 5000000000", TargetLanguage.Cpp).PrimaryFile.Content;

        AssertHeaderSet(template, "#include <iostream>");
        StringAssert.Contains(template, "std::cout << \"Age=\" << 49 << \", Adult=\" << (true ? \"TRUE\" : \"FALSE\") << '\\n';");
        AssertHeaderSet(literal, "#include <iostream>");
        StringAssert.Contains(literal, "std::cout << \"Hello\" << '\\n';");
        AssertHeaderSet(wide, "#include <cstdint>");
    }

    [TestMethod]
    public void Cpp_string_facilities_still_emit_the_string_header()
    {
        string variable = Generate("LET Name = \"Sin\"\nPRINT {Name}", TargetLanguage.Cpp).PrimaryFile.Content;
        string interpolation = Generate(
            "LET Message = $\"Age={49}\"\nPRINT {Message}",
            TargetLanguage.Cpp).PrimaryFile.Content;
        string concatenation = Generate("LET Text = \"A\" + \"B\"", TargetLanguage.Cpp).PrimaryFile.Content;
        string embeddedNul = Generate("LET Text = \"A\\0B\"", TargetLanguage.Cpp).PrimaryFile.Content;
        string equality = Generate("LET Same = \"A\" = \"A\"", TargetLanguage.Cpp).PrimaryFile.Content;

        AssertHeaderSet(variable, "#include <iostream>", "#include <string>");
        AssertHeaderSet(interpolation, "#include <iostream>", "#include <string>");
        AssertHeaderSet(concatenation, "#include <string>");
        AssertHeaderSet(embeddedNul, "#include <string>");
        AssertHeaderSet(equality, "#include <string>");
    }

    private static string BuildCompleteMacroFamilySource()
    {
        var lines = FixedWidthIntegerMacroNames
            .Select((name, index) => $"LET {name} = {index + 1}")
            .Concat(new[] { "LET Wide = 5000000000", string.Empty })
            .Concat(FixedWidthIntegerMacroNames.Select(name => $"PRINT {{{name}}}"))
            .Concat(new[] { "PRINT {Wide}" });
        return string.Join(Environment.NewLine, lines);
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private async Task AssertInstalledTargetMatchesEvaluator(string source, TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        EvaluationResult expected = _evaluator.Evaluate(source);
        Assert.IsTrue(expected.Success, string.Join(Environment.NewLine, expected.Diagnostics));
        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(Normalize(expected.Output), Normalize(result.StandardOutput));
    }

    private static void AssertHeaderSet(string generated, params string[] expectedHeaders)
    {
        string[] actual = generated
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => line.StartsWith("#include ", StringComparison.Ordinal))
            .ToArray();
        CollectionAssert.AreEqual(expectedHeaders, actual);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
