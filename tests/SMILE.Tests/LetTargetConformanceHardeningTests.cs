using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class LetTargetConformanceHardeningTests
{
    private const string EmptyStringMatrixSource = """
LET Empty = ""
LET Copy = Empty
LET Combined = Empty + Empty
LET Prefix = "A" + Empty
LET Suffix = Empty + "B"
LET Middle = $"A{Empty}B"

PRINT {Empty}
PRINT {Copy}
PRINT {Combined}
PRINT {Prefix}
PRINT {Suffix}
PRINT {Middle}
""";

    private const string AdversarialIdentifierSource = """
LET _ = "_"
LET class = "class"
LET namespace = "namespace"
LET record = "record"
LET required = "required"
LET file = "file"
LET global = "global"
LET Console = "Console"
LET System = "System"
LET String = "String"
LET printf = "printf"
LET main = "main"
LET Program = "Program"
LET args = "args"
LET var = "var"
LET yield = "yield"
LET function = "function"
LET await = "await"
LET arguments = "arguments"
LET eval = "eval"
LET auto = "auto"
LET struct = "struct"
LET protocol = "protocol"
LET extension = "extension"
LET func = "func"
LET self = "self"
LET super = "super"
LET Type = "Type"
LET Any = "Any"
LET NSString = "NSString"
LET __internal = "__internal"
LET _Upper = "_Upper"
LET _smile_class = "_smile_class"
LET PrintText = "print"

PRINT {_}
PRINT {class}
PRINT {namespace}
PRINT {record}
PRINT {required}
PRINT {file}
PRINT {global}
PRINT {Console}
PRINT {System}
PRINT {String}
PRINT {printf}
PRINT {main}
PRINT {Program}
PRINT {args}
PRINT {var}
PRINT {yield}
PRINT {function}
PRINT {await}
PRINT {arguments}
PRINT {eval}
PRINT {auto}
PRINT {struct}
PRINT {protocol}
PRINT {extension}
PRINT {func}
PRINT {self}
PRINT {super}
PRINT {Type}
PRINT {Any}
PRINT {NSString}
PRINT {__internal}
PRINT {_Upper}
PRINT {_smile_class}
PRINT {PrintText}
""";

    private const string CollisionSource = """
LET class = "A"
LET _smile_class = "B"
LET _smile_class_2 = "C"

PRINT {class}
PRINT {_smile_class}
PRINT {_smile_class_2}
""";

    private static readonly TargetLanguage[] RunnableTargets =
    {
        TargetLanguage.CSharp,
        TargetLanguage.C,
        TargetLanguage.MasmX64,
        TargetLanguage.JavaScript,
        TargetLanguage.Java
    };

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    public void Evaluator_preserves_empty_let_strings_exactly()
    {
        (string Source, string ExpectedOutput)[] cases =
        {
            ("LET Empty = \"\"\nPRINT {Empty}", "\n"),
            ("LET Empty = \"\"\nLET Copy = Empty\nPRINT {Copy}", "\n"),
            ("LET Empty = \"\"\nLET Combined = Empty + Empty\nPRINT {Combined}", "\n"),
            (
                """
LET Empty = ""
LET Prefix = "A" + Empty
LET Suffix = Empty + "B"
LET Middle = $"A{Empty}B"

PRINT {Prefix}
PRINT {Suffix}
PRINT {Middle}
""",
                "A\nB\nAB\n"),
            (EmptyStringMatrixSource, "\n\n\nA\nB\nAB\n")
        };

        foreach ((string source, string expectedOutput) in cases)
        {
            EvaluationResult result = _evaluator.Evaluate(source);

            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.AreEqual(expectedOutput, result.Output);
            AssertNoEmbeddedNul(result.Output);
        }
    }

    [TestMethod]
    public void Masm_empty_let_string_uses_placeholder_storage_with_zero_logical_length()
    {
        string masm = Generate("LET Empty = \"\"\nLET Name = \"Sin\"\nPRINT {Empty}", TargetLanguage.MasmX64)
            .PrimaryFile
            .Content;

        StringAssert.Contains(masm, "variable0Value BYTE 0");
        StringAssert.Contains(masm, "variable0ValueLength EQU 0");
        StringAssert.Contains(masm, "variable1Value BYTE \"Sin\"");
        StringAssert.Contains(masm, "variable1ValueLength EQU $ - variable1Value");
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    public void Hardening_corpora_generate_deterministically_for_all_targets(TargetLanguage language)
    {
        foreach (string source in new[] { EmptyStringMatrixSource, AdversarialIdentifierSource, CollisionSource })
        {
            GeneratedProgram first = Generate(source, language);
            GeneratedProgram second = Generate(source, language);

            CollectionAssert.AreEqual(
                first.Files.Select(file => file.Content).ToArray(),
                second.Files.Select(file => file.Content).ToArray());
            AssertNoEmbeddedNul(first.PrimaryFile.Content);
        }
    }

    [TestMethod]
    public void Java_and_swift_map_single_underscore_to_usable_variables()
    {
        const string source = "LET _ = \"Sin\"\nPRINT {_}";

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "String _smile_ = \"Sin\";");
        StringAssert.Contains(java, "System.out.println(_smile_);");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let _smile_ = \"Sin\"");
        StringAssert.Contains(swift, "print(\"\\(_smile_)\")");
    }

    [TestMethod]
    public void Target_identifier_map_covers_keywords_runtime_names_and_reserved_patterns()
    {
        string csharp = Generate(AdversarialIdentifierSource, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "string _smile_class = \"class\";");
        StringAssert.Contains(csharp, "string _smile_namespace = \"namespace\";");
        StringAssert.Contains(csharp, "string _smile_record = \"record\";");
        StringAssert.Contains(csharp, "string _smile_required = \"required\";");
        StringAssert.Contains(csharp, "string _smile_file = \"file\";");
        StringAssert.Contains(csharp, "string _smile_global = \"global\";");
        StringAssert.Contains(csharp, "string _smile_Console = \"Console\";");
        StringAssert.Contains(csharp, "string _smile_Program = \"Program\";");
        StringAssert.Contains(csharp, "string _smile_String = \"String\";");
        StringAssert.Contains(csharp, "Console.WriteLine($\"{_smile_Console}\");");

        string c = Generate(AdversarialIdentifierSource, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "const char *class = \"class\";");
        StringAssert.Contains(c, "const char *_smile_auto = \"auto\";");
        StringAssert.Contains(c, "const char *_smile_printf = \"printf\";");
        StringAssert.Contains(c, "const char *_smile_main = \"main\";");
        StringAssert.Contains(c, "const char *_smile___internal = \"__internal\";");
        StringAssert.Contains(c, "const char *_smile__Upper = \"_Upper\";");
        StringAssert.Contains(c, "printf(\"%s\\n\", _smile___internal);");

        string javascript = Generate(AdversarialIdentifierSource, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let _smile_class = \"class\";");
        StringAssert.Contains(javascript, "let _smile_function = \"function\";");
        StringAssert.Contains(javascript, "let _smile_await = \"await\";");
        StringAssert.Contains(javascript, "let _smile_arguments = \"arguments\";");
        StringAssert.Contains(javascript, "let _smile_eval = \"eval\";");

        string javascriptConsole = Generate("LET console = \"console\"\nPRINT {console}", TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascriptConsole, "let _smile_console = \"console\";");
        StringAssert.Contains(javascriptConsole, "console.log(`${_smile_console}`);");

        string java = Generate(AdversarialIdentifierSource, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "String _smile_ = \"_\";");
        StringAssert.Contains(java, "String _smile_class = \"class\";");
        StringAssert.Contains(java, "String _smile_record = \"record\";");
        StringAssert.Contains(java, "String _smile_var = \"var\";");
        StringAssert.Contains(java, "String _smile_yield = \"yield\";");
        StringAssert.Contains(java, "String _smile_System = \"System\";");
        StringAssert.Contains(java, "String _smile_String = \"String\";");
        StringAssert.Contains(java, "String _smile_args = \"args\";");

        string objectiveC = Generate(AdversarialIdentifierSource, TargetLanguage.ObjectiveC).PrimaryFile.Content;
        StringAssert.Contains(objectiveC, "NSString *_smile_printf = @\"printf\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_main = @\"main\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_self = @\"self\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_super = @\"super\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_NSString = @\"NSString\";");
        StringAssert.Contains(objectiveC, "NSString *_smile___internal = @\"__internal\";");
        StringAssert.Contains(objectiveC, "NSString *_smile__Upper = @\"_Upper\";");

        string swift = Generate(AdversarialIdentifierSource, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let _smile_ = \"_\"");
        StringAssert.Contains(swift, "let _smile_class = \"class\"");
        StringAssert.Contains(swift, "let _smile_struct = \"struct\"");
        StringAssert.Contains(swift, "let _smile_protocol = \"protocol\"");
        StringAssert.Contains(swift, "let _smile_extension = \"extension\"");
        StringAssert.Contains(swift, "let _smile_var = \"var\"");
        StringAssert.Contains(swift, "let _smile_func = \"func\"");
        StringAssert.Contains(swift, "let _smile_self = \"self\"");
        StringAssert.Contains(swift, "let _smile_super = \"super\"");
        StringAssert.Contains(swift, "let _smile_Type = \"Type\"");
        StringAssert.Contains(swift, "let _smile_Any = \"Any\"");
        StringAssert.Contains(swift, "let _smile_String = \"String\"");
    }

    [TestMethod]
    public void Objective_c_and_swift_map_case_sensitive_platform_names()
    {
        string objectiveC = Generate("""
LET Class = "Class"
LET Nil = "Nil"
LET YES = "YES"
LET NO = "NO"

PRINT {Class}
PRINT {Nil}
PRINT {YES}
PRINT {NO}
""", TargetLanguage.ObjectiveC).PrimaryFile.Content;

        StringAssert.Contains(objectiveC, "NSString *_smile_Class = @\"Class\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_Nil = @\"Nil\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_YES = @\"YES\";");
        StringAssert.Contains(objectiveC, "NSString *_smile_NO = @\"NO\";");

        string swift = Generate("LET Self = \"Self\"\nPRINT {Self}", TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let _smile_Self = \"Self\"");
        StringAssert.Contains(swift, "print(\"\\(_smile_Self)\")");
    }

    [TestMethod]
    public void Collision_mapping_keeps_every_generated_identifier_distinct()
    {
        string csharp = Generate(CollisionSource, TargetLanguage.CSharp).PrimaryFile.Content;

        StringAssert.Contains(csharp, "string _smile_class = \"A\";");
        StringAssert.Contains(csharp, "string _smile_class_2 = \"B\";");
        StringAssert.Contains(csharp, "string _smile_class_2_2 = \"C\";");
        StringAssert.Contains(csharp, "Console.WriteLine($\"{_smile_class}\");");
        StringAssert.Contains(csharp, "Console.WriteLine($\"{_smile_class_2}\");");
        StringAssert.Contains(csharp, "Console.WriteLine($\"{_smile_class_2_2}\");");
    }

    [TestMethod]
    public void Missing_let_initializer_reports_dedicated_diagnostic()
    {
        BindResult result = _transpiler.Bind("LET Name =");

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.Code == "SMILE1116");
        Assert.AreEqual("LET requires an initializer expression.", diagnostic.Message);
        Assert.AreEqual(1, diagnostic.Span.Line);
        Assert.AreEqual(11, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Missing_let_initializer_does_not_replace_malformed_present_initializers()
    {
        AssertDiagnostic("LET Name", "SMILE1113");
        AssertDiagnostic("LET Name = \"Sin\" +", "SMILE1108");
    }

    [TestMethod]
    public void Print_keyword_remains_reserved_as_a_smile_identifier()
    {
        BindResult result = _transpiler.Bind("LET print = \"print\"");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1115"));
    }

    [TestMethod]
    public async Task Runnable_targets_match_reference_evaluator_for_hardening_corpora()
    {
        string[] sources =
        {
            EmptyStringMatrixSource,
            AdversarialIdentifierSource,
            CollisionSource
        };

        int executed = 0;
        foreach (string source in sources)
        {
            EvaluationResult evaluation = _evaluator.Evaluate(source);
            Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));
            string expected = NormalizeNewlines(evaluation.Output);

            foreach (TargetLanguage language in RunnableTargets)
            {
                IToolchain toolchain = _toolchains.Get(language);
                ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
                if (!status.IsAvailable)
                {
                    continue;
                }

                GeneratedProgram program = Generate(source, language);
                BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

                Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
                Assert.AreEqual(expected, NormalizeNewlines(result.StandardOutput));
                AssertNoEmbeddedNul(result.StandardOutput);
                executed++;
            }
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No runnable target toolchains are installed.");
        }
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private void AssertDiagnostic(string source, string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    private static void AssertNoEmbeddedNul(string text) =>
        Assert.IsFalse(text.Contains('\0', StringComparison.Ordinal), "Output contained an embedded NUL byte.");
}
