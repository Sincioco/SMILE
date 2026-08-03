using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class LetSpecificationConformanceTests
{
    private const string CombinedAcceptanceSource = """
LET FirstName = "Sin"
LET LastName = "Cioco"
LET CopyOfFirstName = FirstName
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"

PRINT {CopyOfFirstName}
PRINT {FullName}
PRINT {Greeting}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Let_v1_valid_initializers_evaluate_to_expected_strings()
    {
        EvaluationResult result = _evaluator.Evaluate(CombinedAcceptanceSource);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("Sin\nSin Cioco\nHello Sin Cioco!\n", result.Output);
    }

    [TestMethod]
    [DataRow("LET Name = \"Sin\"\nPRINT {Name}", "Sin\n")]
    [DataRow("LET FirstName = \"Sin\"\nLET Copy = FirstName\nPRINT {Copy}", "Sin\n")]
    [DataRow("LET FirstName = \"Sin\"\nLET LastName = \"Cioco\"\nLET FullName = FirstName + \" \" + LastName\nPRINT {FullName}", "Sin Cioco\n")]
    [DataRow("LET Name = \"Sin\"\nLET Greeting = $\"Hello {Name}!\"\nPRINT {Greeting}", "Hello Sin!\n")]
    [DataRow("LET Placeholder = $\"Use {{Name}} as a placeholder.\"\nPRINT {Placeholder}", "Use {Name} as a placeholder.\n")]
    [DataRow("LET Steps = \"First; second; third\"\nPRINT {Steps}", "First; second; third\n")]
    public void Normative_valid_let_examples_evaluate_successfully(string source, string expectedOutput)
    {
        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(expectedOutput, result.Output);
    }

    [TestMethod]
    [DataRow("LETName = \"Sin\"", "SMILE1001")]
    [DataRow("LET = \"Sin\"", "SMILE1112")]
    [DataRow("LET 2Name = \"Sin\"", "SMILE1112")]
    [DataRow("LET First Name = \"Sin\"", "SMILE1113")]
    [DataRow("LET LET = \"Sin\"", "SMILE1115")]
    [DataRow("LET let = \"Sin\"", "SMILE1115")]
    [DataRow("LET PRINT = \"Sin\"", "SMILE1115")]
    [DataRow("LET pRiNt = \"Sin\"", "SMILE1115")]
    [DataRow("LET Name", "SMILE1113")]
    [DataRow("LET Name =", "SMILE1116")]
    [DataRow("LET Name =    ", "SMILE1116")]
    [DataRow("LET Name = Hello World!", "SMILE1111")]
    [DataRow("LET Name = \"Sin\" +", "SMILE1201")]
    [DataRow("LET Name = MissingName", "SMILE1106")]
    [DataRow("LET Name = $\"Hello {", "SMILE1103")]
    [DataRow("LET Name = $\"Hello {}", "SMILE1105")]
    [DataRow("LET Name = $\"Hello {MissingName}!\"", "SMILE1106")]
    [DataRow("LET Name = \"Sin\"; PRINT {Name}", "SMILE1109")]
    [DataRow("LET Name = \"Sin\" LET Other = \"Joy\"", "SMILE1111")]
    public void Normative_invalid_let_examples_report_diagnostics(string source, string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    [DataRow("LET Name = \"Sin\"\nLET Name = \"Joy\"")]
    [DataRow("LET Name = \"Sin\"\nLET NAME = \"Joy\"")]
    public void Duplicate_let_declarations_are_case_insensitive_errors(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Diagnostic duplicate = result.Diagnostics.Single(diagnostic => diagnostic.Code == "SMILE1107");
        Assert.AreEqual(2, duplicate.Span.Line);
    }

    [TestMethod]
    [DataRow("LET Name = \"Sin\"")]
    [DataRow("LET first_name = \"Sin\"")]
    [DataRow("LET Name2 = \"Sin\"")]
    [DataRow("LET _ = \"Sin\"")]
    [DataRow("LET _temporary = \"Sin\"")]
    [DataRow("LET Letter = \"A\"")]
    [DataRow("LET Reprint = \"Again\"")]
    [DataRow("LET Printable = \"Yes\"")]
    [DataRow("LET LetValue = \"Value\"")]
    public void Official_ascii_identifier_examples_are_valid(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    [DataRow("LET 2Name = \"Sin\"")]
    [DataRow("LET First-Name = \"Sin\"")]
    [DataRow("LET Näme = \"Sin\"")]
    public void Invalid_identifier_examples_are_rejected(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1112"),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    [DataRow("LET Greeting = FirstName + \"!\"\nLET FirstName = \"Sin\"")]
    [DataRow("LET Name = Name + \"!\"")]
    public void Forward_reference_and_self_reference_are_undefined_at_initializer_time(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1106"));
    }

    [TestMethod]
    public void Failed_let_initializer_does_not_create_a_later_symbol()
    {
        BindResult result = _transpiler.Bind("""
LET Broken = MissingName
PRINT {Broken}
""");

        Assert.IsFalse(result.Success);
        string[] undefinedNames = result.Diagnostics
            .Where(diagnostic => diagnostic.Code == "SMILE1106")
            .Select(diagnostic => diagnostic.Message)
            .ToArray();
        Assert.IsTrue(undefinedNames.Any(message => message.Contains("MissingName", StringComparison.Ordinal)));
        Assert.IsTrue(undefinedNames.Any(message => message.Contains("Broken", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Bound_let_initializers_preserve_expression_shape_and_constant_values()
    {
        BindResult result = _transpiler.Bind("""
LET Name = "Sin"
LET Copy = Name
LET FullName = Name + " Cioco"
LET Greeting = $"Hello {FullName}!"
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        BoundLetStatement[] lets = result.Program!.Statements.OfType<BoundLetStatement>().ToArray();

        Assert.IsInstanceOfType(lets[1].Initializer, typeof(BoundVariableExpression));
        var fullName = (BoundBinaryExpression)lets[2].Initializer;
        Assert.AreEqual(BoundBinaryOperatorKind.StringConcatenation, fullName.Operator.Kind);
        Assert.IsInstanceOfType(lets[3].Initializer, typeof(BoundInterpolatedStringExpression));
        CollectionAssert.AreEqual(
            new[] { "Sin", "Sin", "Sin Cioco", "Hello Sin Cioco!" },
            lets.Select(let => let.ConstantValue.ToDisplayText()).ToArray());
    }

    [TestMethod]
    public void Evaluator_keeps_literal_print_distinct_from_evaluated_print()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Name = "Sin"

PRINT Name
PRINT {Name}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("Name\nSin\n", result.Output);
    }

    [TestMethod]
    public void Evaluator_preserves_blank_and_empty_print_newlines()
    {
        EvaluationResult result = _evaluator.Evaluate("""
PRINT
PRINT ""
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("\n\n", result.Output);
    }

    [TestMethod]
    public void Evaluator_returns_diagnostics_for_invalid_source()
    {
        EvaluationResult result = _evaluator.Evaluate("LET Name = MissingName");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1106"));
    }

    [TestMethod]
    public void High_level_generators_preserve_let_expression_intent()
    {
        const string source = """
LET FirstName = "Sin"
LET Copy = FirstName
LET FullName = FirstName + " Cioco"
LET Greeting = $"Hello {FullName}!"
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "string Copy = FirstName;");
        StringAssert.Contains(csharp, "string FullName = FirstName + \" Cioco\";");
        StringAssert.Contains(csharp, "string Greeting = $\"Hello {FullName}!\";");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Copy = FirstName;");
        StringAssert.Contains(javascript, "let FullName = FirstName + \" Cioco\";");
        StringAssert.Contains(javascript, "let Greeting = `Hello ${FullName}!`;");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "String Copy = FirstName;");
        StringAssert.Contains(java, "String FullName = FirstName + \" Cioco\";");
        StringAssert.Contains(java, "String Greeting = \"Hello \" + FullName + \"!\";");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let Copy: String = FirstName");
        StringAssert.Contains(swift, "let FullName: String = FirstName + \" Cioco\"");
        StringAssert.Contains(swift, "let Greeting: String = \"Hello \\(FullName)!\"");
    }

    [TestMethod]
    public void Low_level_generators_use_evaluated_let_constant_values()
    {
        const string source = """
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"
""";

        string c = Generate(source, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "const char *FullName = \"Sin Cioco\";");
        StringAssert.Contains(c, "const char *Greeting = \"Hello Sin Cioco!\";");

        string objectiveC = Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content;
        StringAssert.Contains(objectiveC, "const char *FullName = \"Sin Cioco\";");
        StringAssert.Contains(objectiveC, "const char *Greeting = \"Hello Sin Cioco!\";");

        string cobol = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;
        StringAssert.Contains(cobol, "01 FullName PIC X(9) VALUE \"Sin Cioco\".");
        StringAssert.Contains(cobol, "01 Greeting PIC X(16) VALUE \"Hello Sin Cioco!\".");

        string masm = Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "variable2Value BYTE \"Sin Cioco\"");
        StringAssert.Contains(masm, "variable3Value BYTE \"Hello Sin Cioco!\"");
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    public void Generators_are_deterministic_for_complete_let_program(TargetLanguage language)
    {
        GeneratedProgram first = Generate(CombinedAcceptanceSource, language);
        GeneratedProgram second = Generate(CombinedAcceptanceSource, language);

        CollectionAssert.AreEqual(
            first.Files.Select(file => file.Content).ToArray(),
            second.Files.Select(file => file.Content).ToArray());
    }

    [TestMethod]
    public void Target_identifier_mapping_uses_safe_consistent_names()
    {
        const string source = """
LET class = "A"
LET Console = "B"
LET printf = "C"
LET System = "D"

PRINT {class}
PRINT {Console}
PRINT {printf}
PRINT {System}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "string _smile_class = \"A\";");
        StringAssert.Contains(csharp, "string _smile_Console = \"B\";");
        StringAssert.Contains(csharp, "Console.WriteLine(_smile_class);");
        StringAssert.Contains(csharp, "Console.WriteLine(_smile_Console);");

        string c = Generate(source, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "const char *class = \"A\";");
        StringAssert.Contains(c, "const char *_smile_printf = \"C\";");
        StringAssert.Contains(c, "printf(\"%s\\n\", class);");
        StringAssert.Contains(c, "printf(\"%s\\n\", _smile_printf);");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "String _smile_class = \"A\";");
        StringAssert.Contains(java, "String _smile_System = \"D\";");
        StringAssert.Contains(java, "System.out.println(_smile_System);");

        string javascript = Generate("LET console = \"A\"\nPRINT {console}", TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let _smile_console = \"A\";");
        StringAssert.Contains(javascript, "console.log(_smile_console);");
    }

    [TestMethod]
    public void Target_identifier_mapping_adds_suffixes_for_collisions()
    {
        string csharp = Generate("""
LET class = "A"
LET _smile_class = "B"

PRINT {class}
PRINT {_smile_class}
""", TargetLanguage.CSharp).PrimaryFile.Content;

        StringAssert.Contains(csharp, "string _smile_class = \"A\";");
        StringAssert.Contains(csharp, "string _smile_class_2 = \"B\";");
        StringAssert.Contains(csharp, "Console.WriteLine(_smile_class);");
        StringAssert.Contains(csharp, "Console.WriteLine(_smile_class_2);");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }
}
