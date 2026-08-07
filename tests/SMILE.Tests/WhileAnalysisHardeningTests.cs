using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class WhileAnalysisHardeningTests
{
    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void Zero_or_more_merge_keeps_only_values_shared_by_every_loop_exit_path()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Same = 1
LET Different = 1
WHILE Continue = TRUE
    SET Same = 1
    SET Different = 2
    INPUT Continue
END WHILE
""");
        VariableSymbol same = program.Variables.Single(variable => variable.Name == "Same");
        VariableSymbol different = program.Variables.Single(variable => variable.Name == "Different");
        var loop = (BoundWhileStatement)program.Statements[3];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundWhileStatementAnalysis facts = analysis.GetWhileFacts(loop);

        Assert.IsTrue(facts.ValuesAtHead[same].IsKnown);
        Assert.AreEqual(1L, facts.ValuesAtHead[same].Value.IntegerValue);
        Assert.IsTrue(facts.ValuesAfter[same].IsKnown);
        Assert.AreEqual(1L, facts.ValuesAfter[same].Value.IntegerValue);
        Assert.IsFalse(facts.ValuesAtHead[different].IsKnown);
        Assert.IsFalse(facts.ValuesAfter[different].IsKnown);
        Assert.IsFalse(analysis.FinalConcreteValues.ContainsKey(different));
    }

    [TestMethod]
    public void Loop_carried_Integer_growth_widens_quickly_to_the_signed_64_bit_range()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Count = 0
WHILE Continue = TRUE
    SET Count = Count + 1
    INPUT Continue
END WHILE
""");
        VariableSymbol count = program.Variables.Single(variable => variable.Name == "Count");
        var loop = (BoundWhileStatement)program.Statements[2];
        var increment = (BoundSetStatement)loop.Statements[0];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsFalse(analysis.GetWhileFacts(loop).ValuesAtHead[count].IsKnown);
        Assert.AreEqual(
            new AnalyzedIntegerRange(1, long.MaxValue),
            analysis.GetPossibleIntegerRange(increment.Value));
        Assert.Contains(count, analysis.VariablesWithInexactAssignedValues);
        Assert.HasCount(1, analysis.AssignedValues[count]);
        Assert.AreEqual(0L, analysis.AssignedValues[count][0].IntegerValue);
    }

    [TestMethod]
    public void Loop_carried_Integer_decrement_widens_through_the_signed_minimum()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Count = 0
WHILE Continue = TRUE
    SET Count = Count - 1
    INPUT Continue
END WHILE
""");
        var loop = (BoundWhileStatement)program.Statements[2];
        var decrement = (BoundSetStatement)loop.Statements[0];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.AreEqual(
            new AnalyzedIntegerRange(long.MinValue, -1),
            analysis.GetPossibleIntegerRange(decrement.Value));
    }

    [TestMethod]
    public void Boolean_loop_input_and_IF_mutation_use_stable_unknown_head_facts()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Choose = TRUE
LET Result = 0
WHILE Continue = TRUE
    INPUT Choose
    IF Choose = TRUE THEN
        SET Result = 1
    ELSE
        SET Result = 2
    END IF
    INPUT Continue
END WHILE
""");
        VariableSymbol choose = program.Variables.Single(variable => variable.Name == "Choose");
        VariableSymbol result = program.Variables.Single(variable => variable.Name == "Result");
        var loop = (BoundWhileStatement)program.Statements[3];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundWhileStatementAnalysis facts = analysis.GetWhileFacts(loop);

        Assert.IsFalse(facts.ValuesAtHead[choose].IsKnown);
        Assert.IsFalse(facts.ValuesAtHead[result].IsKnown);
        Assert.IsFalse(facts.ValuesAfter[choose].IsKnown);
        Assert.IsFalse(facts.ValuesAfter[result].IsKnown);
        Assert.Contains(choose, analysis.MutatedVariables);
        Assert.Contains(result, analysis.MutatedVariables);
        CollectionAssert.AreEquivalent(
            new[] { false, true },
            analysis.AssignedValues[choose]
                .Select(value => value.BooleanValue)
                .Distinct()
                .ToArray());
    }

    [TestMethod]
    public void Complete_body_reset_keeps_intermediate_String_growth_finitely_bounded()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Text = ""
WHILE Continue = TRUE
    SET Text = Text + "x"
    SET Text = ""
    INPUT Continue
END WHILE
""");
        VariableSymbol text = program.Variables.Single(variable => variable.Name == "Text");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(1, analysis.MaximumAssignedUtf8ByteLength(text));
    }

    [TestMethod]
    public void INPUT_inside_nested_WHILE_retains_its_finite_String_bound_and_NUL_fact()
    {
        BoundProgram program = Bind("""
LET Outer = TRUE
LET Inner = TRUE
LET Text = ""
WHILE Outer = TRUE
    WHILE Inner = TRUE
        INPUT Text
        INPUT Inner
    END WHILE
    INPUT Outer
END WHILE
""");
        VariableSymbol text = program.Variables.Single(variable => variable.Name == "Text");
        var outer = (BoundWhileStatement)program.Statements[3];
        var inner = (BoundWhileStatement)outer.Statements[0];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(SmileLanguage.MaximumInputLineUtf8Bytes, analysis.MaximumAssignedUtf8ByteLength(text));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(text));
        Assert.AreEqual(0, analysis.GetWhileOrdinal(outer));
        Assert.AreEqual(1, analysis.GetWhileOrdinal(inner));
    }

    [TestMethod]
    public void Unbounded_String_recurrence_nested_in_IF_is_reported_on_the_containing_WHILE()
    {
        const string source = """
LET Continue = TRUE
LET Choose = TRUE
LET Text = ""
WHILE Continue = TRUE
    IF Choose = TRUE THEN
        SET Text = Text + "x"
    ELSE
        SET Text = Text
    END IF
    INPUT Continue
END WHILE
""";

        BindResult result = _transpiler.Bind(source);
        Diagnostic diagnostic = result.Diagnostics.Single(item => item.Code == "SMILE1612");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(4, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
        Assert.AreEqual(5, diagnostic.Span.Length);
    }

    [TestMethod]
    public void Unbounded_String_recurrence_in_nested_WHILE_is_reported_on_the_nested_opener()
    {
        const string source = """
LET Outer = TRUE
LET Inner = TRUE
LET Text = ""
WHILE Outer = TRUE
    WHILE Inner = TRUE
        SET Text = Text + "x"
        INPUT Inner
    END WHILE
    INPUT Outer
END WHILE
""";

        BindResult result = _transpiler.Bind(source);
        Diagnostic[] diagnostics = result.Diagnostics
            .Where(item => item.Code == "SMILE1612")
            .ToArray();

        Assert.IsFalse(result.Success);
        Assert.HasCount(2, diagnostics);
        Assert.AreEqual(4, diagnostics[0].Span.Line);
        Assert.AreEqual(1, diagnostics[0].Span.Column);
        Assert.AreEqual(5, diagnostics[0].Span.Length);
        Assert.AreEqual(5, diagnostics[1].Span.Line);
        Assert.AreEqual(5, diagnostics[1].Span.Column);
        Assert.AreEqual(5, diagnostics[1].Span.Length);
    }

    [TestMethod]
    public void Maximum_supported_nested_WHILE_analysis_converges_and_records_each_loop_once()
    {
        var source = new StringBuilder("LET Sentinel = 0\n");
        for (int depth = 0; depth < 128; depth++)
        {
            source.AppendLine("WHILE FALSE = TRUE");
        }

        for (int depth = 0; depth < 128; depth++)
        {
            source.AppendLine("END WHILE");
        }

        BoundProgram program = Bind(source.ToString());
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundWhileStatement[] loops = analysis.EnumerateStatements()
            .OfType<BoundWhileStatement>()
            .ToArray();

        Assert.HasCount(128, loops);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 128).ToArray(),
            loops.Select(analysis.GetWhileOrdinal).ToArray());
        Assert.HasCount(
            analysis.EnumerateStatements().Count,
            analysis.EnumerateStatements().Distinct(ReferenceEqualityComparer.Instance).ToArray());
    }

    [TestMethod]
    public void Finite_multi_hop_String_copy_propagation_is_not_mistaken_for_recurrence()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET A = ""
LET B = "b"
LET C = "cc"
LET D = "ddd"
WHILE Continue = TRUE
    SET A = B
    SET B = C
    SET C = D
    INPUT Continue
END WHILE
""");
        VariableSymbol a = program.Variables.Single(variable => variable.Name == "A");
        VariableSymbol b = program.Variables.Single(variable => variable.Name == "B");
        VariableSymbol c = program.Variables.Single(variable => variable.Name == "C");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(3, analysis.MaximumAssignedUtf8ByteLength(a));
        Assert.AreEqual(3, analysis.MaximumAssignedUtf8ByteLength(b));
        Assert.AreEqual(3, analysis.MaximumAssignedUtf8ByteLength(c));
    }

    [TestMethod]
    public void Finite_String_display_growth_from_Integer_widening_remains_bounded()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET Count = 0
LET Text = ""
WHILE Continue = TRUE
    SET Text = $"{Count}"
    SET Count = Count + 1
    INPUT Continue
END WHILE
""");
        VariableSymbol text = program.Variables.Single(variable => variable.Name == "Text");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(20, analysis.MaximumAssignedUtf8ByteLength(text));
    }

    [TestMethod]
    public void Cross_variable_String_growth_cycle_still_reports_SMILE1612()
    {
        BindResult result = _transpiler.Bind("""
LET Continue = TRUE
LET A = ""
LET B = ""
WHILE Continue = TRUE
    SET A = B + "x"
    SET B = A
    INPUT Continue
END WHILE
""");

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.Diagnostics.Where(diagnostic => diagnostic.Code == "SMILE1612").ToArray());
    }

    [TestMethod]
    public void Pure_copy_String_cycle_reaches_a_finite_maximum_without_SMILE1612()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET A = "a"
LET B = "bb"
WHILE Continue = TRUE
    SET A = B
    SET B = A
    INPUT Continue
END WHILE
""");
        VariableSymbol a = program.Variables.Single(variable => variable.Name == "A");
        VariableSymbol b = program.Variables.Single(variable => variable.Name == "B");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(2, analysis.MaximumAssignedUtf8ByteLength(a));
        Assert.AreEqual(2, analysis.MaximumAssignedUtf8ByteLength(b));
    }

    [TestMethod]
    public void Growth_cycle_is_detected_when_the_positive_edge_target_is_reset_later()
    {
        BindResult result = _transpiler.Bind("""
LET Continue = TRUE
LET A = ""
LET B = ""
WHILE Continue = TRUE
    SET B = A + "x"
    SET A = B
    SET B = ""
    INPUT Continue
END WHILE
""");

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.Diagnostics.Where(diagnostic => diagnostic.Code == "SMILE1612").ToArray());
    }

    [TestMethod]
    public void Overwritten_growth_does_not_poison_a_later_finite_copy_chain()
    {
        BoundProgram program = Bind("""
LET Continue = TRUE
LET A = ""
LET B = "b"
LET C = "cc"
LET D = "ddd"
WHILE Continue = TRUE
    SET A = A + "x"
    SET A = B
    SET B = C
    SET C = D
    INPUT Continue
END WHILE
""");
        VariableSymbol a = program.Variables.Single(variable => variable.Name == "A");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsEmpty(analysis.Diagnostics);
        Assert.AreEqual(4, analysis.MaximumAssignedUtf8ByteLength(a));
    }

    [TestMethod]
    public void Generator_receives_stable_loop_head_facts_instead_of_the_LET_initializer()
    {
        TranspileResult result = _transpiler.Transpile("""
LET Continue = TRUE
LET Count = 0
WHILE Continue = TRUE
    PRINT {Count}
    SET Count = Count + 1
    INPUT Continue
END WHILE
""", TargetLanguage.CSharp);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        string generated = result.GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(
            generated,
            "Console.WriteLine(Count.ToString(CultureInfo.InvariantCulture));");
        Assert.IsFalse(generated.Contains("Console.WriteLine(0L", StringComparison.Ordinal));
    }

    private BoundProgram Bind(string source)
    {
        BindResult result = _transpiler.Bind(source);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsNotNull(result.Program);
        return result.Program;
    }
}
