using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class CSharpSelfAssignmentGenerationTests
{
    private const string AllTypesSource = """
LET Name = "Sin"
LET Count = 49
LET Ready = TRUE

SET Name = Name
SET Count = Count
SET Ready = Ready

PRINT {Name}
PRINT {Count}
PRINT {Ready}
""";

    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void Csharp_and_swift_lower_direct_self_assignment_for_every_SMILE_type()
    {
        string csharp = Generate(AllTypesSource, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "Name = Name + \"\";");
        StringAssert.Contains(csharp, "Count = Count + 0;");
        StringAssert.Contains(csharp, "Ready = Ready || false;");
        Assert.IsFalse(csharp.Contains("Name = Name;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Count = Count;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Ready = Ready;", StringComparison.Ordinal));

        string swift = Generate(AllTypesSource, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "Name = Name + \"\"");
        StringAssert.Contains(swift, "Count = Count + 0");
        StringAssert.Contains(swift, "Ready = Ready || false");
        Assert.IsFalse(swift.Contains("Name = Name\n", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("Count = Count\n", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("Ready = Ready\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_wide_Integer_self_assignment_preserves_the_long_profile()
    {
        const string source = """
LET Count = 5000000000
SET Count = Count
PRINT {Count}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;

        StringAssert.Contains(csharp, "long Count = 5000000000L;");
        StringAssert.Contains(csharp, "Count = Count + 0;");
        Assert.IsFalse(csharp.Contains("Count = Count;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_detects_case_insensitive_self_assignment_by_bound_symbol()
    {
        const string source = """
LET Name = "Sin"
SET name = NAME
PRINT {NaMe}
""";

        BindResult binding = _transpiler.Bind(source);
        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        BoundLetStatement let = binding.Program!.Statements.OfType<BoundLetStatement>().Single();
        BoundSetStatement set = binding.Program.Statements.OfType<BoundSetStatement>().Single();
        BoundVariableExpression value = (BoundVariableExpression)set.Value;
        Assert.AreSame(let.Variable, set.Variable);
        Assert.AreSame(set.Variable, value.Variable);

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "Name = Name + \"\";");
        Assert.IsFalse(csharp.Contains("Name = Name;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_uses_the_mapped_identifier_in_self_assignment_identity_lowering()
    {
        const string source = """
LET class = "Sin"
SET CLASS = class
PRINT {Class}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;

        StringAssert.Contains(csharp, "string _smile_class = \"Sin\";");
        StringAssert.Contains(csharp, "_smile_class = _smile_class + \"\";");
        StringAssert.Contains(csharp, "Console.WriteLine(_smile_class);");
        Assert.IsFalse(csharp.Contains("_smile_class = _smile_class;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_keeps_different_variable_assignments_natural_for_every_SMILE_type()
    {
        const string source = """
LET NumberA = 1
LET NumberB = 1
LET TextA = "A"
LET TextB = "A"
LET FlagA = TRUE
LET FlagB = TRUE

SET NumberA = NumberB
SET TextA = TextB
SET FlagA = FlagB
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;

        StringAssert.Contains(csharp, "NumberA = NumberB;");
        StringAssert.Contains(csharp, "TextA = TextB;");
        StringAssert.Contains(csharp, "FlagA = FlagB;");
        Assert.IsFalse(csharp.Contains("NumberA = NumberA + 0;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("TextA = TextA + \"\";", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("FlagA = FlagA || false;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_does_not_duplicate_explicit_identity_expressions()
    {
        const string source = """
LET Count = 49
LET Name = "Sin"
LET Ready = TRUE

SET Count = Count + 0
SET Name = Name + ""
SET Ready = Ready OR FALSE
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;

        Assert.AreEqual(1, CountOccurrences(csharp, "Count = Count + 0;"));
        Assert.AreEqual(1, CountOccurrences(csharp, "Name = Name + \"\";"));
        Assert.AreEqual(1, CountOccurrences(csharp, "Ready = Ready || false;"));
        Assert.IsFalse(csharp.Contains("Count = Count + 0 + 0;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Name = Name + \"\" + \"\";", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Ready = Ready || false || false;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_self_assignment_generation_is_byte_deterministic()
    {
        GeneratedProgram first = Generate(AllTypesSource, TargetLanguage.CSharp);
        GeneratedProgram second = Generate(AllTypesSource, TargetLanguage.CSharp);

        CollectionAssert.AreEqual(
            first.Files.Select(file => file.RelativePath).ToArray(),
            second.Files.Select(file => file.RelativePath).ToArray());
        CollectionAssert.AreEqual(
            first.Files.Select(file => file.Content).ToArray(),
            second.Files.Select(file => file.Content).ToArray());
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
