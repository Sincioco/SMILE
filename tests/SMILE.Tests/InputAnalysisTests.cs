using System.Reflection;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class InputAnalysisTests
{
    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void INPUT_replaces_a_known_Integer_with_full_range_Unknown_facts()
    {
        BoundProgram program = Bind("LET Age = 0\nINPUT Age\nPRINT {Age}");
        VariableSymbol age = program.Variables.Single(variable => variable.Name == "Age");
        var input = (BoundInputStatement)program.Statements[1];
        var print = (BoundPrintStatement)program.Statements[2];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundStatementAnalysis inputFacts = analysis.GetStatementFacts(input);

        Assert.IsTrue(inputFacts.ValuesBefore[age].IsKnown);
        Assert.AreEqual(0L, inputFacts.ValuesBefore[age].Value.IntegerValue);
        Assert.IsFalse(inputFacts.ValuesAfter[age].IsKnown);
        Assert.IsFalse(inputFacts.Value.IsKnown);
        Assert.IsFalse(inputFacts.HasConcreteValue);
        Assert.IsFalse(inputFacts.ConcreteValuesAfter.ContainsKey(age));
        Assert.IsFalse(analysis.FinalValues[age].IsKnown);
        Assert.IsFalse(analysis.FinalConcreteValues.ContainsKey(age));
        Assert.Contains(age, analysis.MutatedVariables);
        Assert.Contains(age, analysis.VariablesWithInexactAssignedValues);
        Assert.AreEqual(
            new AnalyzedIntegerRange(long.MinValue, long.MaxValue),
            analysis.GetPossibleIntegerRange(print.Value));
        Assert.AreEqual(20, analysis.MaximumExpressionDisplayUtf8ByteLength(print.Value));
    }

    [TestMethod]
    public void INPUT_seeds_complete_String_and_Boolean_possible_value_facts()
    {
        BoundProgram program = Bind("""
LET Name = ""
LET Ready = FALSE
LET Copy = ""
INPUT Name
INPUT Ready
SET Copy = Name + "!"
PRINT {Ready}
""");
        VariableSymbol name = program.Variables.Single(variable => variable.Name == "Name");
        VariableSymbol ready = program.Variables.Single(variable => variable.Name == "Ready");
        VariableSymbol copy = program.Variables.Single(variable => variable.Name == "Copy");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.AreEqual(SmileLanguage.MaximumInputLineUtf8Bytes, analysis.MaximumAssignedUtf8ByteLength(name));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(name));
        Assert.Contains(name, analysis.VariablesWithInexactAssignedValues);
        Assert.AreEqual(SmileLanguage.MaximumInputLineUtf8Bytes + 1, analysis.MaximumAssignedUtf8ByteLength(copy));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(copy));
        CollectionAssert.AreEquivalent(
            new[] { false, true },
            analysis.AssignedValues[ready].Select(value => value.BooleanValue).Distinct().ToArray());
        Assert.AreEqual(5, analysis.MaximumAssignedUtf8ByteLength(ready));
        Assert.IsFalse(analysis.AssignedValuesMayContainNul(ready));
        Assert.IsFalse(analysis.FinalValues[ready].IsKnown);
    }

    [TestMethod]
    public void Runtime_dependent_LET_remains_bound_instead_of_being_silently_dropped()
    {
        BoundProgram program = Bind("""
LET Divisor = 1
INPUT Divisor
LET Result = 10 / Divisor
PRINT {Result}
""");

        Assert.HasCount(4, program.Statements);
        var result = (BoundLetStatement)program.Statements[2];
        Assert.AreEqual("Result", result.Variable.Name);

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        Assert.IsFalse(analysis.FinalValues[result.Variable].IsKnown);
    }

    [TestMethod]
    public void Static_evaluation_distinguishes_Known_Unknown_and_Invalid()
    {
        BoundProgram program = Bind("""
LET Check = FALSE
LET Divisor = 1
LET Result = FALSE
INPUT Check
INPUT Divisor
SET Result = Check = TRUE AND (1 / Divisor = 0)
""");
        var runtimeSet = (BoundSetStatement)program.Statements[^1];

        StaticEvaluationResult unknown = BoundExpressionEvaluator.Evaluate(
            runtimeSet.Value,
            new Dictionary<VariableSymbol, SmileValue>());
        Assert.AreEqual(StaticEvaluationKind.Unknown, unknown.Kind);
        Assert.IsTrue(unknown.MayFailAtRuntime);

        BoundProgram unselectedError = Bind("""
LET Choose = FALSE
LET Result = 0
IF Choose = TRUE THEN
    SET Result = 1 / 0
END IF
""");
        var conditional = (BoundIfStatement)unselectedError.Statements[^1];
        BoundExpression invalidExpression = ((BoundSetStatement)conditional.Clauses[0].Statements.Single()).Value;
        StaticEvaluationResult invalid = BoundExpressionEvaluator.Evaluate(
            invalidExpression,
            new Dictionary<VariableSymbol, SmileValue>());
        Assert.AreEqual(StaticEvaluationKind.Invalid, invalid.Kind);
        Assert.AreEqual(SmileArithmeticErrorKind.DivisionByZero, invalid.Error!.Value.Kind);

        StaticEvaluationResult known = BoundExpressionEvaluator.Evaluate(
            new BoundIntegerLiteralExpression(49),
            new Dictionary<VariableSymbol, SmileValue>());
        Assert.AreEqual(StaticEvaluationKind.Known, known.Kind);
        Assert.AreEqual(49L, known.Value.IntegerValue);
    }

    [TestMethod]
    public void Concrete_source_only_trace_directs_INPUT_programs_to_the_evaluator()
    {
        BoundProgram program = Bind("LET Value = 0\nINPUT Value");

        try
        {
            BoundProgramExecutionTrace.Create(program);
            Assert.Fail("INPUT unexpectedly produced a concrete source-only trace.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual(
                "A concrete source-only execution trace cannot evaluate INPUT. " +
                "Use SmileEvaluator with an injected input source for runtime programs.",
                exception.Message);
        }
    }

    [TestMethod]
    public void Source_known_reachable_errors_remain_diagnostics_but_runtime_errors_bind()
    {
        BindResult sourceKnown = _transpiler.Bind("LET Bad = 1 / 0");
        Assert.IsFalse(sourceKnown.Success);
        Assert.IsTrue(sourceKnown.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1207"));

        BindResult runtime = _transpiler.Bind("LET Divisor = 1\nINPUT Divisor\nLET Result = 1 / Divisor");
        Assert.IsTrue(runtime.Success, Join(runtime.Diagnostics));

        BindResult conditionallyReached = _transpiler.Bind("""
LET Check = FALSE
LET Result = 0
INPUT Check
IF Check = TRUE THEN
    SET Result = 1 / 0
END IF
""");
        Assert.IsTrue(conditionallyReached.Success, Join(conditionallyReached.Diagnostics));
    }

    [TestMethod]
    public void Source_known_error_after_every_runtime_path_has_terminated_is_not_reported()
    {
        BindResult result = _transpiler.Bind("""
LET Choose = FALSE
LET Value = 0
INPUT Choose
IF Choose = TRUE THEN
    SET Value = 1 / 0
ELSE
    SET Value = 2 / 0
END IF
SET Value = 3 / 0
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.HasCount(0, result.Diagnostics);
        Assert.HasCount(5, result.Program!.Statements);
    }

    [TestMethod]
    public void Source_known_error_after_only_some_runtime_paths_terminate_is_not_unconditional()
    {
        const string source = """
LET Choose = FALSE
LET Value = 0
INPUT Choose
PRINT Before
IF Choose = TRUE THEN
    SET Value = 1
    PRINT Continued
ELSE
    SET Value = 1 / 0
    PRINT Unreachable
END IF
SET Value = 2 / 0
PRINT Also unreachable
""";

        BindResult binding = _transpiler.Bind(source);
        Assert.IsTrue(binding.Success, Join(binding.Diagnostics));
        Assert.HasCount(0, binding.Diagnostics);

        EvaluationResult truePath = new SmileEvaluator().Evaluate(source, "TRUE\n");
        Assert.AreEqual("Before\nContinued\n", truePath.Output);
        Assert.AreEqual("SMILER1207", truePath.RuntimeError?.Code);

        EvaluationResult falsePath = new SmileEvaluator().Evaluate(source, "FALSE\n");
        Assert.AreEqual("Before\n", falsePath.Output);
        Assert.AreEqual("SMILER1207", falsePath.RuntimeError?.Code);
    }

    [TestMethod]
    public void Source_known_error_after_runtime_dependent_arithmetic_is_not_unconditional()
    {
        const string source = """
LET X = 1
INPUT X
SET X = 1 / X
SET X = 1 / 0
""";

        BindResult binding = _transpiler.Bind(source);
        Assert.IsTrue(binding.Success, Join(binding.Diagnostics));
        Assert.HasCount(0, binding.Diagnostics);

        EvaluationResult zero = new SmileEvaluator().Evaluate(source, "0\n");
        Assert.AreEqual("SMILER1207", zero.RuntimeError?.Code);

        EvaluationResult one = new SmileEvaluator().Evaluate(source, "1\n");
        Assert.AreEqual("SMILER1207", one.RuntimeError?.Code);
    }

    [TestMethod]
    [DataRow("X + 0")]
    [DataRow("0 + X")]
    [DataRow("X - 0")]
    [DataRow("X * 0")]
    [DataRow("0 * X")]
    [DataRow("X * 1")]
    [DataRow("1 * X")]
    [DataRow("X / 1")]
    [DataRow("X / 2")]
    public void Guaranteed_safe_input_arithmetic_does_not_hide_a_later_compile_error(
        string safeExpression)
    {
        BindResult binding = _transpiler.Bind($"""
LET X = 0
INPUT X
SET X = {safeExpression}
SET X = 1 / 0
""");

        Assert.IsFalse(binding.Success);
        Assert.IsTrue(
            binding.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1207"),
            Join(binding.Diagnostics));
    }

    [TestMethod]
    [DataRow("X * 0")]
    [DataRow("0 * X")]
    public void Multiplication_by_zero_proves_the_runtime_value_for_later_diagnostics(
        string zeroExpression)
    {
        BindResult binding = _transpiler.Bind($"""
LET X = 1
INPUT X
SET X = {zeroExpression}
SET X = 1 / X
""");

        Assert.IsFalse(binding.Success);
        Assert.IsTrue(
            binding.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1207"),
            Join(binding.Diagnostics));
    }

    [TestMethod]
    public void Source_known_branch_error_after_a_fallible_known_condition_is_not_unconditional()
    {
        const string source = """
LET X = 1
LET Y = 0
INPUT X
IF (1 / X = 0) AND FALSE = TRUE THEN
    SET Y = 1
ELSE
    SET Y = 1 / 0
END IF
""";

        BindResult binding = _transpiler.Bind(source);
        Assert.IsTrue(binding.Success, Join(binding.Diagnostics));
        Assert.HasCount(0, binding.Diagnostics);

        EvaluationResult zero = new SmileEvaluator().Evaluate(source, "0\n");
        Assert.AreEqual("SMILER1207", zero.RuntimeError?.Code);

        EvaluationResult one = new SmileEvaluator().Evaluate(source, "1\n");
        Assert.AreEqual("SMILER1207", one.RuntimeError?.Code);
    }

    [TestMethod]
    public void IF_merge_is_Unknown_when_runtime_paths_differ_and_Known_when_they_agree()
    {
        BoundProgram different = Bind("""
LET Choose = FALSE
LET Result = 0
INPUT Choose
IF Choose = TRUE THEN
    INPUT Result
ELSE
    SET Result = 5
END IF
""");
        VariableSymbol differentResult = different.Variables.Single(variable => variable.Name == "Result");
        BoundProgramAnalysis differentAnalysis = BoundProgramAnalysis.Create(different);
        Assert.IsFalse(differentAnalysis.FinalValues[differentResult].IsKnown);

        BoundProgram same = Bind("""
LET Choose = FALSE
LET Result = 0
INPUT Choose
IF Choose = TRUE THEN
    SET Result = 7
ELSE
    SET Result = 7
END IF
""");
        VariableSymbol sameResult = same.Variables.Single(variable => variable.Name == "Result");
        BoundProgramAnalysis sameAnalysis = BoundProgramAnalysis.Create(same);
        Assert.IsTrue(sameAnalysis.FinalValues[sameResult].IsKnown);
        Assert.AreEqual(7L, sameAnalysis.FinalValues[sameResult].Value.IntegerValue);
    }

    [TestMethod]
    public void INPUT_in_every_IF_branch_remains_Unknown_after_the_merge()
    {
        BoundProgram program = Bind("""
LET Choose = FALSE
LET Value = "Before"
INPUT Choose
IF Choose = TRUE THEN
    INPUT Value
ELSE
    INPUT Value
END IF
PRINT {Value}
""");
        VariableSymbol value = program.Variables.Single(variable => variable.Name == "Value");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsFalse(analysis.FinalValues[value].IsKnown);
        Assert.IsFalse(analysis.FinalConcreteValues.ContainsKey(value));
        Assert.AreEqual(SmileLanguage.MaximumInputLineUtf8Bytes, analysis.MaximumAssignedUtf8ByteLength(value));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(value));
    }

    [TestMethod]
    public void Runtime_unknown_first_clause_never_selects_a_later_concrete_clause()
    {
        BoundProgram program = Bind("""
LET Choose = FALSE
LET Result = "start"
INPUT Choose
IF Choose = TRUE THEN
    SET Result = "first"
ELSE IF TRUE = TRUE THEN
    SET Result = "second"
ELSE
    SET Result = "else"
END IF
""");
        VariableSymbol result = program.Variables.Single(variable => variable.Name == "Result");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        Assert.IsFalse(analysis.FinalValues[result].IsKnown);
        Assert.IsFalse(analysis.FinalConcreteValues.ContainsKey(result));
    }

    [TestMethod]
    public void INPUT_and_nested_statements_receive_deterministic_source_ordinals()
    {
        BoundProgram program = Bind("""
LET Choose = TRUE
LET Value = ""

// first input
INPUT Choose
IF Choose = TRUE THEN
    INPUT Value
ELSE
    SET Value = "fallback"
END IF
PRINT {Value}
""");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        IReadOnlyList<BoundStatement> statements = analysis.EnumerateStatements();

        CollectionAssert.AreEqual(
            Enumerable.Range(0, statements.Count).ToArray(),
            statements.Select(statement => analysis.GetStatementFacts(statement).Ordinal).ToArray());
        Assert.HasCount(7, statements);
        Assert.HasCount(2, statements.OfType<BoundInputStatement>().ToArray());
    }

    [TestMethod]
    public void Simplifier_preserves_INPUT_and_does_not_erase_a_fallible_left_operand()
    {
        BoundProgram program = Bind("""
LET Divisor = 1
LET Result = FALSE
INPUT Divisor
SET Result = (1 / Divisor = 0) AND FALSE
PRINT {Result}
""");

        BoundProgram simplified = Simplify(program);

        Assert.HasCount(1, simplified.Statements.OfType<BoundInputStatement>().ToArray());
        var set = simplified.Statements.OfType<BoundSetStatement>().Single();
        var logical = (BoundBinaryExpression)set.Value;
        Assert.AreEqual(BoundBinaryOperatorKind.LogicalAnd, logical.Operator.Kind);
        Assert.IsInstanceOfType<BoundBinaryExpression>(logical.Left);
    }

    [TestMethod]
    public void SET_after_INPUT_can_restore_a_known_value()
    {
        BoundProgram program = Bind("""
LET Value = 1
SET Value = 2
INPUT Value
SET Value = 9
""");
        VariableSymbol value = program.Variables.Single();

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);

        BoundInputStatement input = program.Statements.OfType<BoundInputStatement>().Single();
        Assert.IsTrue(analysis.GetStatementFacts(input).ValuesBefore[value].IsKnown);
        Assert.AreEqual(2L, analysis.GetStatementFacts(input).ValuesBefore[value].Value.IntegerValue);

        Assert.IsTrue(analysis.FinalValues[value].IsKnown);
        Assert.AreEqual(9L, analysis.FinalValues[value].Value.IntegerValue);
        Assert.Contains(value, analysis.MutatedVariables);
    }

    private BoundProgram Bind(string source)
    {
        BindResult result = _transpiler.Bind(source);
        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        return result.Program!;
    }

    private static BoundProgram Simplify(BoundProgram program)
    {
        Type type = typeof(SmileTranspiler).Assembly.GetType(
            "SMILE.Engine.BoundProgramSimplifier",
            throwOnError: true)!;
        MethodInfo method = type.GetMethod(
            "Simplify",
            BindingFlags.Public | BindingFlags.Static)!;
        return (BoundProgram)method.Invoke(null, new object[] { program })!;
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
