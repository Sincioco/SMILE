using System.IO;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class CppTargetTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Cpp_metadata_is_appended_as_the_tenth_target()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                TargetLanguage.CSharp,
                TargetLanguage.C,
                TargetLanguage.MasmX64,
                TargetLanguage.JavaScript,
                TargetLanguage.Java,
                TargetLanguage.Cobol,
                TargetLanguage.ObjectiveC,
                TargetLanguage.Swift,
                TargetLanguage.Python,
                TargetLanguage.Cpp
            },
            TargetLanguageInfo.All.ToArray());

        Assert.AreEqual("cpp", TargetLanguageInfo.GetStableId(TargetLanguage.Cpp));
        Assert.AreEqual("C++", TargetLanguageInfo.GetDisplayName(TargetLanguage.Cpp));
        Assert.AreEqual("Program.cpp", TargetLanguageInfo.GetPrimaryFileName(TargetLanguage.Cpp));
        Assert.IsTrue(TargetLanguageInfo.TryParse("CPP", out TargetLanguage parsed));
        Assert.AreEqual(TargetLanguage.Cpp, parsed);
    }

    [TestMethod]
    public void Cpp_generator_emits_idiomatic_small_program()
    {
        string generated = Generate("""
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "int Age = 49;");
        StringAssert.Contains(generated, "bool Adult = Age >= 18;");
        StringAssert.Contains(generated, "bool WorkingAge = Adult;");
        Assert.IsFalse(generated.Contains("#include", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("INT64_C", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("LL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Cpp_generator_uses_owned_strings_valid_concatenation_and_native_equality()
    {
        string generated = Generate("""
LET FirstName = "Sin"
LET Copy = FirstName
LET FullName = FirstName + " " + "Cioco"
LET LiteralPair = "A" + "B"
LET Same = FullName = "Sin Cioco"
LET Message = $"{FullName}: {Same}"

PRINT {FullName}
PRINT Name={FirstName}, Same={Same}
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "#include <iostream>");
        StringAssert.Contains(generated, "#include <string>");
        StringAssert.Contains(generated, "std::string FirstName = \"Sin\";");
        StringAssert.Contains(generated, "std::string Copy = FirstName;");
        StringAssert.Contains(generated, "std::string FullName = FirstName + \" \" + \"Cioco\";");
        StringAssert.Contains(generated, "std::string LiteralPair = std::string{\"A\"} + \"B\";");
        StringAssert.Contains(generated, "bool Same = FullName == \"Sin Cioco\";");
        StringAssert.Contains(generated, "std::string Message = FullName + \": \" + (Same ? \"TRUE\" : \"FALSE\");");
        StringAssert.Contains(generated, "std::cout << FullName << '\\n';");
        StringAssert.Contains(generated, "std::cout << \"Name=\" << FirstName << \", Same=\" << (Same ? \"TRUE\" : \"FALSE\") << '\\n';");
        Assert.IsFalse(generated.Contains("using namespace std", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("printf", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("strcmp", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("char *", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("std::endl", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Cpp_generator_preserves_embedded_NUL_with_length_aware_strings()
    {
        string generated = Generate("""
LET A = "A\0B"
LET B = "A\0C"
LET Same = A = B

PRINT {A}
PRINT {Same}
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "std::string A = std::string{\"A\\000B\", 3};");
        StringAssert.Contains(generated, "std::string B = std::string{\"A\\000C\", 3};");
        StringAssert.Contains(generated, "bool Same = A == B;");
        StringAssert.Contains(generated, "std::cout << A << '\\n';");
    }

    [TestMethod]
    public void Cpp_generator_uses_wide_profile_and_exact_literals_only_when_required()
    {
        string generated = Generate("""
LET Small = 49
LET Wide = 5000000000
LET Min = -9223372036854775808
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "#include <cstdint>");
        StringAssert.Contains(generated, "std::int64_t Small = INT64_C(49);");
        StringAssert.Contains(generated, "std::int64_t Wide = INT64_C(5000000000);");
        StringAssert.Contains(generated, "std::int64_t Min = INT64_MIN;");
        Assert.IsFalse(generated.Contains("LL", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("#include <limits>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Cpp_precedence_and_boolean_display_preserve_the_bound_tree()
    {
        string generated = Generate("""
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = 100 / (10 / 2)
LET E = TRUE = (FALSE = FALSE)

PRINT {E}
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "int A = 2 + 3 * 4;");
        StringAssert.Contains(generated, "int B = (2 + 3) * 4;");
        StringAssert.Contains(generated, "int C = 10 - (3 - 1);");
        StringAssert.Contains(generated, "int D = 100 / (10 / 2);");
        StringAssert.Contains(generated, "bool E = true == (false == false);");
        StringAssert.Contains(generated, "std::cout << (E ? \"TRUE\" : \"FALSE\") << '\\n';");
    }

    [TestMethod]
    public void Cpp_identifier_mapping_covers_keywords_runtime_names_and_implementation_reservations()
    {
        string generated = Generate("""
LET class = "class"
LET concept = "concept"
LET final = "final"
LET module = "module"
LET std = "std"
LET main = "main"
LET cout = "cout"
LET string = "string"
LET to_string = "to_string"
LET int64_t = "int64_t"
LET INT64_C = "INT64_C"
LET smile_text = "smile_text"
LET __Hidden = "double"
LET _Upper = "upper"

PRINT {class}
PRINT {__Hidden}
""").PrimaryFile.Content;

        StringAssert.Contains(generated, "std::string _smile_class = \"class\";");
        StringAssert.Contains(generated, "std::string _smile_concept = \"concept\";");
        StringAssert.Contains(generated, "std::string _smile_final = \"final\";");
        StringAssert.Contains(generated, "std::string _smile_module = \"module\";");
        StringAssert.Contains(generated, "std::string _smile_std = \"std\";");
        StringAssert.Contains(generated, "std::string _smile_main = \"main\";");
        StringAssert.Contains(generated, "std::string _smile_cout = \"cout\";");
        StringAssert.Contains(generated, "std::string _smile_string = \"string\";");
        StringAssert.Contains(generated, "std::string _smile_to_string = \"to_string\";");
        StringAssert.Contains(generated, "std::string _smile_int64_t = \"int64_t\";");
        StringAssert.Contains(generated, "std::string _smile_INT64_C = \"INT64_C\";");
        StringAssert.Contains(generated, "std::string _smile_smile_text = \"smile_text\";");
        StringAssert.Contains(generated, "std::string _smile___Hidden = \"double\";");
        StringAssert.Contains(generated, "std::string _smile__Upper = \"upper\";");
    }

    [TestMethod]
    public void Cpp_generation_is_byte_for_byte_deterministic_and_uses_minimal_headers()
    {
        const string source = "PRINT {49}";
        string first = Generate(source).PrimaryFile.Content;
        string second = Generate(source).PrimaryFile.Content;

        Assert.AreEqual(first, second);
        StringAssert.Contains(first, "#include <iostream>");
        Assert.IsFalse(first.Contains("#include <string>", StringComparison.Ordinal));
        Assert.IsFalse(first.Contains("#include <cstdint>", StringComparison.Ordinal));
        Assert.IsTrue(first.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.IsFalse(first.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installed_cpp_matches_reference_evaluator_and_writes_pause_launcher()
    {
        const string source = """
LET Literal = "A\0B"
LET Copy = Literal
LET Other = "A\0C"
LET Different = Copy <> Other
LET Wide = 5000000000
LET SafeAnd = FALSE AND (1 / 0 = 0)
LET Message = $"Wide={Wide}, Different={Different}"

PRINT {Literal}
PRINT {Different}
PRINT {SafeAnd}
PRINT {Message}
""";

        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.Cpp);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source),
            CancellationToken.None,
            new BuildRunOptions(CreatePauseLauncher: true));

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            Convert.ToHexString(Encoding.UTF8.GetBytes(Normalize(evaluation.Output))),
            Convert.ToHexString(Encoding.UTF8.GetBytes(Normalize(result.StandardOutput))));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PauseLauncherPath));
        string launcher = await File.ReadAllTextAsync(result.PauseLauncherPath!);
        StringAssert.Contains(launcher, "\"Program.exe\"");
    }

    private GeneratedProgram Generate(string source)
    {
        TranspileResult result = _transpiler.Transpile(source, TargetLanguage.Cpp);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
