using System.Globalization;
using System.Text;

namespace SMILE.Engine;

public sealed record GeneratedFile(
    string RelativePath,
    string Content,
    bool IsPrimary);

public sealed record GeneratedProgram(
    TargetLanguage Language,
    IReadOnlyList<GeneratedFile> Files)
{
    public GeneratedFile PrimaryFile => Files.Single(file => file.IsPrimary);
}

public interface ICodeGenerator
{
    TargetLanguage Language { get; }

    // Generators consume the bound program, not source text. That keeps target
    // backends honest: they all see the same variables, literals, and
    // interpolation parts resolved by the binder.
    GeneratedProgram Generate(BoundProgram program);
}

public sealed record TranspileResult(
    TargetLanguage Language,
    GeneratedProgram? GeneratedProgram,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success =>
        GeneratedProgram is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class SmileTranspiler
{
    public ParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Parser(source).Parse();
    }

    public BindResult Bind(string source)
    {
        ParseResult parseResult = Parse(source);
        if (!parseResult.Success || parseResult.Program is null)
        {
            return new BindResult(null, parseResult.Diagnostics);
        }

        BindResult bindResult = new Binder().Bind(parseResult.Program);
        return new BindResult(
            bindResult.Program,
            parseResult.Diagnostics.Concat(bindResult.Diagnostics).ToArray());
    }

    public TranspileResult Transpile(string source, TargetLanguage targetLanguage) =>
        TranspileMany(source, new[] { targetLanguage }).Single();

    public IReadOnlyList<TranspileResult> TranspileMany(
        string source,
        IEnumerable<TargetLanguage> targetLanguages)
    {
        ArgumentNullException.ThrowIfNull(targetLanguages);

        TargetLanguage[] languages = targetLanguages.Distinct().ToArray();

        BindResult bindResult = Bind(source);
        if (!bindResult.Success || bindResult.Program is null)
        {
            return languages
                .Select(language => new TranspileResult(language, null, bindResult.Diagnostics))
                .ToArray();
        }

        // Simplification belongs between binding and target generation. The
        // binder remains the source of truth for SMILE's signed 64-bit
        // semantics, while every backend receives the same smaller, pure
        // bound tree and therefore cannot invent target-specific identities.
        BoundProgram simplifiedProgram = BoundProgramSimplifier.Simplify(bindResult.Program);

        return languages
            .Select(language =>
            {
                ICodeGenerator generator = CodeGeneratorRegistry.Get(language);
                return new TranspileResult(language, generator.Generate(simplifiedProgram), bindResult.Diagnostics);
            })
            .ToArray();
    }
}

internal static class BoundProgramSimplifier
{
    public static BoundProgram Simplify(BoundProgram program)
    {
        var values = new Dictionary<VariableSymbol, SmileValue>();
        IReadOnlyList<BoundStatement> statements = SimplifyStatementList(program.Statements, values);
        return new BoundProgram(statements, program.Variables);
    }

    private static IReadOnlyList<BoundStatement> SimplifyStatementList(
        IReadOnlyList<BoundStatement> sourceStatements,
        Dictionary<VariableSymbol, SmileValue> values)
    {
        var statements = new List<BoundStatement>(sourceStatements.Count);

        foreach (BoundStatement statement in sourceStatements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    BoundExpression initializer = SimplifyExpression(let.Initializer, values);
                    statements.Add(let with { Initializer = initializer });
                    UpdateKnownValue(values, let.Variable, initializer);
                    break;

                case BoundSetStatement set:
                    // SET sees the old value throughout its complete right side.
                    // Only after simplification and evaluation succeeds does the
                    // new value become visible to later statements.
                    BoundExpression value = SimplifyExpression(set.Value, values);
                    statements.Add(set with { Value = value });
                    UpdateKnownValue(values, set.Variable, value);
                    break;

                case BoundPrintStatement print:
                    statements.Add(print with
                    {
                        Value = SimplifyExpression(print.Value, values)
                    });
                    break;

                case BoundIfStatement conditional:
                    statements.Add(SimplifyIfStatement(conditional, values));
                    break;

                default:
                    statements.Add(statement);
                    break;
            }
        }

        return statements;
    }

    private static BoundIfStatement SimplifyIfStatement(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values)
    {
        var clauses = new List<BoundConditionalClause>(conditional.Clauses.Count);
        var outgoingEnvironments = new List<Dictionary<VariableSymbol, SmileValue>>(
            conditional.Clauses.Count + 1);

        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            // Keep condition comparisons and their variable reads visible in
            // every target. Using current source-only values here could turn a
            // genuine condition into `if (false)`, which both erases the
            // educational expression and triggers unreachable/unused warnings
            // in strict target compilers. Binding has already validated the
            // complete condition tree; branch bodies still use their incoming
            // facts for safe expression simplification.
            BoundExpression condition = SimplifyExpression(
                clause.Condition,
                new Dictionary<VariableSymbol, SmileValue>());
            var branchValues = new Dictionary<VariableSymbol, SmileValue>(values);
            IReadOnlyList<BoundStatement> branchStatements =
                SimplifyStatementList(clause.Statements, branchValues);
            clauses.Add(clause with
            {
                Condition = condition,
                Statements = branchStatements
            });
            outgoingEnvironments.Add(branchValues);
        }

        var elseValues = new Dictionary<VariableSymbol, SmileValue>(values);
        IReadOnlyList<BoundStatement> elseStatements = conditional.HasElseClause
            ? SimplifyStatementList(conditional.ElseStatements, elseValues)
            : conditional.ElseStatements;

        // An IF without ELSE has an implicit unchanged path. Every explicit
        // branch is retained and contributes to the merge even when its
        // condition is currently known. This prevents a branch-specific value
        // from leaking into later simplification or target planning.
        outgoingEnvironments.Add(
            conditional.HasElseClause
                ? elseValues
                : new Dictionary<VariableSymbol, SmileValue>(values));
        MergeKnownValues(values, outgoingEnvironments);

        return conditional with
        {
            Clauses = clauses,
            ElseStatements = elseStatements
        };
    }

    private static void UpdateKnownValue(
        Dictionary<VariableSymbol, SmileValue> values,
        VariableSymbol variable,
        BoundExpression expression)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            values[variable] = value;
        }
        else
        {
            values.Remove(variable);
        }
    }

    private static void MergeKnownValues(
        Dictionary<VariableSymbol, SmileValue> destination,
        IReadOnlyList<Dictionary<VariableSymbol, SmileValue>> outgoingEnvironments)
    {
        VariableSymbol[] variables = destination.Keys
            .Concat(outgoingEnvironments.SelectMany(environment => environment.Keys))
            .Distinct()
            .ToArray();

        destination.Clear();
        foreach (VariableSymbol variable in variables)
        {
            bool hasValue = outgoingEnvironments[0].TryGetValue(variable, out SmileValue value);
            if (!hasValue)
            {
                continue;
            }

            bool allPathsAgree = outgoingEnvironments.Skip(1).All(environment =>
                environment.TryGetValue(variable, out SmileValue candidate) && candidate == value);
            if (allPathsAgree)
            {
                destination.Add(variable, value);
            }
        }
    }

    private static BoundExpression SimplifyExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression switch
        {
            BoundUnaryExpression unary => SimplifyUnary(unary, values),
            BoundBinaryExpression binary => SimplifyBinary(binary, values),
            BoundInterpolatedStringExpression interpolated => interpolated with
            {
                Parts = interpolated.Parts.Select(part => part switch
                {
                    BoundInterpolationExpressionPart hole =>
                        hole with { Expression = SimplifyExpression(hole.Expression, values) },
                    _ => part
                }).ToArray()
            },
            _ => expression
        };

    private static BoundExpression SimplifyUnary(
        BoundUnaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        BoundExpression operand = SimplifyExpression(expression.Operand, values);
        if (expression.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation &&
            operand is BoundBooleanLiteralExpression literal)
        {
            return new BoundBooleanLiteralExpression(!literal.Value);
        }

        return expression with { Operand = operand };
    }

    private static BoundExpression SimplifyBinary(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        BoundExpression left = SimplifyExpression(expression.Left, values);

        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            // Preserve the two readable right-side identity forms without
            // traversing the right subtree. This keeps examples such as
            // Adult AND TRUE as Adult and still respects evaluation order.
            if ((expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd &&
                 expression.Right is BoundBooleanLiteralExpression { Value: true }) ||
                (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr &&
                 expression.Right is BoundBooleanLiteralExpression { Value: false }))
            {
                return left;
            }

            if (BoundExpressionEvaluator.TryEvaluate(left, values, out SmileValue leftValue) &&
                leftValue.Type is SmileType.Boolean)
            {
                bool rightIsUnreachable =
                    expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd && !leftValue.BooleanValue ||
                    expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr && leftValue.BooleanValue;
                if (rightIsUnreachable)
                {
                    // Binding has already validated both operands. Skipping
                    // simplification here prevents an unreachable division or
                    // overflow from leaking into a strict target compiler.
                    return new BoundBooleanLiteralExpression(leftValue.BooleanValue);
                }

                BoundExpression reachableRight = SimplifyExpression(expression.Right, values);
                if ((expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd &&
                     reachableRight is BoundBooleanLiteralExpression { Value: true }) ||
                    (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr &&
                     reachableRight is BoundBooleanLiteralExpression { Value: false }))
                {
                    return left;
                }

                return reachableRight;
            }
        }

        BoundExpression right = SimplifyExpression(expression.Right, values);

        // All current SMILE expressions are pure. These Boolean identities
        // can therefore remove redundant work without changing observable
        // behavior, including the language's left-to-right short circuiting.
        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd)
        {
            if (left is BoundBooleanLiteralExpression { Value: false } ||
                right is BoundBooleanLiteralExpression { Value: false })
            {
                return new BoundBooleanLiteralExpression(false);
            }

            if (left is BoundBooleanLiteralExpression { Value: true })
            {
                return right;
            }

            if (right is BoundBooleanLiteralExpression { Value: true })
            {
                return left;
            }
        }

        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr)
        {
            if (left is BoundBooleanLiteralExpression { Value: true } ||
                right is BoundBooleanLiteralExpression { Value: true })
            {
                return new BoundBooleanLiteralExpression(true);
            }

            if (left is BoundBooleanLiteralExpression { Value: false })
            {
                return right;
            }

            if (right is BoundBooleanLiteralExpression { Value: false })
            {
                return left;
            }
        }

        // Empty String concatenation is a target-independent identity. In
        // particular, preserving the non-empty operand keeps a post-IF
        // storage read visible instead of forcing low-level targets to invent
        // a temporary value for `Name + ""`.
        if (expression.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
        {
            if (left is BoundStringLiteralExpression { Value.Length: 0 })
            {
                return right;
            }

            if (right is BoundStringLiteralExpression { Value.Length: 0 })
            {
                return left;
            }
        }

        return expression with { Left = left, Right = right };
    }
}

internal abstract record RuntimeTextSegment;

internal sealed record RuntimeLiteralTextSegment(string Text) : RuntimeTextSegment;

internal sealed record RuntimeExpressionTextSegment(BoundExpression Expression) : RuntimeTextSegment;

internal static class RuntimeTextPlan
{
    public static IReadOnlyList<RuntimeTextSegment> Flatten(BoundExpression expression)
    {
        var segments = new List<RuntimeTextSegment>();
        Append(expression, segments);
        return segments;
    }

    public static bool CanFlatten(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanFlatten(binary.Left) && CanFlatten(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart => true,
                _ => false
            }),
            _ when expression.Type is not SmileType.String => true,
            _ => false
        };

    private static void Append(
        BoundExpression expression,
        List<RuntimeTextSegment> segments)
    {
        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                AppendLiteral(segments, literal.Value);
                break;

            case BoundVariableExpression:
                segments.Add(new RuntimeExpressionTextSegment(expression));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                Append(binary.Left, segments);
                Append(binary.Right, segments);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart text:
                            AppendLiteral(segments, text.Text);
                            break;

                        case BoundInterpolationExpressionPart hole:
                            Append(hole.Expression, segments);
                            break;
                    }
                }

                break;

            default:
                segments.Add(new RuntimeExpressionTextSegment(expression));
                break;
        }
    }

    private static void AppendLiteral(
        List<RuntimeTextSegment> segments,
        string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (segments.LastOrDefault() is RuntimeLiteralTextSegment previous)
        {
            segments[^1] = previous with { Text = previous.Text + text };
        }
        else
        {
            segments.Add(new RuntimeLiteralTextSegment(text));
        }
    }
}

internal static class BoundStatementTree
{
    public static IEnumerable<BoundStatement> Enumerate(BoundProgram program) =>
        Enumerate(program.Statements);

    public static IEnumerable<BoundStatement> Enumerate(
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (BoundStatement statement in statements)
        {
            yield return statement;

            if (statement is not BoundIfStatement conditional)
            {
                continue;
            }

            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                foreach (BoundStatement nested in Enumerate(clause.Statements))
                {
                    yield return nested;
                }
            }

            foreach (BoundStatement nested in Enumerate(conditional.ElseStatements))
            {
                yield return nested;
            }
        }
    }

    public static IEnumerable<BoundExpression> EnumerateExpressions(BoundProgram program)
    {
        foreach (BoundStatement statement in Enumerate(program))
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    yield return let.Initializer;
                    break;

                case BoundSetStatement set:
                    yield return set.Value;
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    yield return print.Value;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        yield return clause.Condition;
                    }

                    break;
            }
        }
    }
}

internal static class GeneratorConditionFacts
{
    private static readonly IReadOnlyDictionary<VariableSymbol, SmileValue> NoValues =
        new Dictionary<VariableSymbol, SmileValue>();

    public static bool IsProvenWithoutVariableReads(BoundExpression expression) =>
        TryEvaluateWithoutVariableReads(expression, out SmileValue value) &&
        value.Type is SmileType.Boolean;

    public static bool RequiresWarningSafeWrapper(BoundExpression expression)
    {
        if ((expression is BoundUnaryExpression or BoundBinaryExpression) &&
            IsProvenWithoutVariableReads(expression))
        {
            return true;
        }

        return expression switch
        {
            BoundUnaryExpression unary => RequiresWarningSafeWrapper(unary.Operand),
            BoundBinaryExpression binary => RequiresWarningSafeWrapper(binary.Left) ||
                RequiresWarningSafeWrapper(binary.Right),
            _ => false
        };
    }

    public static bool TryEvaluateWithoutVariableReads(
        BoundExpression expression,
        out SmileValue value) =>
        BoundExpressionEvaluator.TryEvaluate(expression, NoValues, out value);

    public static bool TryEvaluateFromAnalyzedValues(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> analyzedValues,
        out SmileValue value)
    {
        var knownValues = new Dictionary<VariableSymbol, SmileValue>();
        foreach ((VariableSymbol variable, AnalyzedValue analyzed) in analyzedValues)
        {
            if (analyzed.IsKnown)
            {
                knownValues.Add(variable, analyzed.Value);
            }
        }

        return BoundExpressionEvaluator.TryEvaluate(expression, knownValues, out value);
    }

    public static IReadOnlyDictionary<VariableSymbol, SmileValue> KnownValues(
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> analyzedValues) =>
        analyzedValues
            .Where(pair => pair.Value.IsKnown)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value);

}

internal sealed record TargetIntegerProfile(
    bool RequiresSigned64Storage,
    bool RequiresJavaScriptBigInt)
{
    private const long JavaScriptMaxSafeInteger = 9_007_199_254_740_991L;

    public static TargetIntegerProfile Analyze(
        BoundProgram program,
        BoundProgramAnalysis analysis)
    {
        bool requiresSigned64 = false;
        bool requiresBigInt = false;

        void Observe(long value)
        {
            requiresSigned64 |= value is < int.MinValue or > int.MaxValue;
            requiresBigInt |= value is < -JavaScriptMaxSafeInteger or > JavaScriptMaxSafeInteger;
        }

        void Visit(BoundExpression expression)
        {
            // The branch-aware range is compositional: it covers every path
            // without enumerating a Cartesian product, including an unselected
            // branch and a later arithmetic intermediate fed by a merged value.
            if (expression.Type is SmileType.Integer)
            {
                AnalyzedIntegerRange range = analysis.GetPossibleIntegerRange(expression);
                Observe(range.Minimum);
                Observe(range.Maximum);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Visit(unary.Operand);
                    break;

                case BoundBinaryExpression binary:
                    Visit(binary.Left);
                    Visit(binary.Right);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Visit(hole.Expression);
                    }

                    break;
            }
        }

        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    Visit(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Visit(set.Value);
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    Visit(print.Value);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Visit(clause.Condition);
                    }

                    break;
            }
        }

        return new TargetIntegerProfile(requiresSigned64, requiresBigInt);
    }
}

public static class CodeGeneratorRegistry
{
    private static readonly IReadOnlyDictionary<TargetLanguage, ICodeGenerator> Generators =
        new ICodeGenerator[]
        {
            new CSharpCodeGenerator(),
            new CCodeGenerator(),
            new MasmX64CodeGenerator(),
            new JavaScriptCodeGenerator(),
            new JavaCodeGenerator(),
            new CobolCodeGenerator(),
            new ObjectiveCCodeGenerator(),
            new SwiftCodeGenerator(),
            new PythonCodeGenerator(),
            new CppCodeGenerator()
        }.ToDictionary(generator => generator.Language);

    public static ICodeGenerator Get(TargetLanguage language) => Generators[language];
}

internal sealed class CSharpCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.CSharp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        bool needsConditionHelper = BoundStatementTree.Enumerate(program)
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .Any(clause => GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition));
        var source = new StringBuilder();
        source.AppendLine("using System;");
        if (CSharpGenerationFacts.NeedsInvariantCulture(program))
        {
            source.AppendLine("using System.Globalization;");
        }

        source.AppendLine();
        source.AppendLine("internal static class Program");
        source.AppendLine("{");
        source.AppendLine("    private static void Main()");
        source.AppendLine("    {");

        AppendStatements(
            source,
            program.Statements,
            "        ",
            identifiers,
            integers,
            needsConditionHelper);

        source.AppendLine("    }");
        if (needsConditionHelper)
        {
            source.AppendLine();
            source.AppendLine("    // Keep a valid source-constant IF as genuine control flow without CS0162.");
            source.AppendLine("    private static bool _smile_condition(bool value) => value;");
        }

        source.AppendLine("}");

        const string project = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile("Program.cs", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true),
                new GeneratedFile("GeneratedProgram.csproj", TextOutput.EnsureOneTrailingNewLine(project), IsPrimary: false)
            });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.CSharp(let.Initializer, identifiers, integers);
                    source.AppendLine($"{indent}{TargetTypes.CSharp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = TargetExpression.CSharp(set.Value, identifiers, integers);
                    if (set.Value is BoundVariableExpression variable &&
                        ReferenceEquals(variable.Variable, set.Variable))
                    {
                        // Direct self-assignment is valid SMILE, but C# warns
                        // about a plain `value = value` (CS1717). Keep the real
                        // storage update with the smallest type-preserving
                        // identity expression instead of deleting the SET.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{indent}{name} = {value};");
                    break;

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.Append(indent).AppendLine("Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"{indent}Console.WriteLine({TargetExpression.CSharpDisplay(print.Value, identifiers, integers)});");
                    }

                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        hasConditionHelper);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.CSharp(clause.Condition, identifiers, integers);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(condition)
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }
    }
}

internal sealed class CCodeGenerator : ICodeGenerator
{
    internal sealed record RuntimeStringBuffer(string Name, int Capacity);

    public TargetLanguage Language => TargetLanguage.C;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers =
            CreateRuntimeStringBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers =
            CreateRuntimeExpressionBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CreateExactStringLengthNames(
                program,
                identifiers,
                analysis,
                runtimeStringBuffers.Keys.Select(statement => statement switch
                {
                    BoundLetStatement let => let.Variable,
                    BoundSetStatement set => set.Variable,
                    _ => throw new InvalidOperationException("Unexpected C runtime String statement.")
                }));
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (exactStringLengths.Count > 0 ||
            CGenerationFacts.NeedsStringComparison(analysis))
        {
            source.AppendLine("#include <string.h>");
        }

        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        foreach (RuntimeStringBuffer buffer in runtimeExpressionBuffers.Values)
        {
            source.Append("    static char ").Append(buffer.Name).Append('[')
                .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                .AppendLine("] = { 0 };");
            source.Append("    size_t ").Append(buffer.Name).AppendLine("Used = 0;");
        }

        if (runtimeExpressionBuffers.Count > 0)
        {
            source.AppendLine();
        }

        bool emittedDeclaration = runtimeExpressionBuffers.Count > 0;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;
        AppendStatements(
            source,
            program.Statements,
            "    ",
            analysis,
            identifiers,
            integers,
            exactStringLengths,
            runtimeStringBuffers,
            runtimeExpressionBuffers,
            ref emittedDeclaration,
            ref emittedExecutable,
            ref emittedBodyStatement);

        if (emittedBodyStatement)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.c", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        foreach (BoundStatement statement in statements)
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    if (let.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        if (let.Initializer is BoundVariableExpression letDirectSource)
                        {
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {identifiers.Get(letDirectSource.Variable)};");
                            if (exactStringLengths.TryGetValue(let.Variable, out string? directLetLength))
                            {
                                string sourceLength = exactStringLengths.TryGetValue(
                                    letDirectSource.Variable,
                                    out string? exactSourceLength)
                                    ? exactSourceLength
                                    : $"strlen({identifiers.Get(letDirectSource.Variable)})";
                                source.AppendLine($"{indent}size_t {directLetLength} = {sourceLength};");
                            }
                        }
                        else
                        {
                            RuntimeStringBuffer buffer = runtimeStringBuffers[let];
                            source.AppendLine(
                                $"{indent}static char {buffer.Name}[{buffer.Capacity + 1}] = {{ 0 }};");
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {buffer.Name};");
                            source.AppendLine(
                                $"{indent}size_t {exactStringLengths[let.Variable]} = 0;");
                            AppendCRuntimeStringAssignment(
                                source,
                                indent,
                                let.Variable,
                                let.Initializer,
                                buffer,
                                identifiers,
                                integers,
                                exactStringLengths,
                                runtimeExpressionBuffers,
                                declareBuffer: false);
                        }
                    }
                    else
                    {
                        SmileValue letValue = let.Variable.Type is SmileType.String
                            ? facts.Value.Value
                            : default;
                        string initializer = let.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(letValue, integers)
                            : TargetExpression.C(
                                let.Initializer,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                        if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                        {
                            source.AppendLine($"{indent}size_t {letLengthName} = {Utf8ByteLength(letValue)};");
                        }
                    }

                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    if (set.Variable.Type is SmileType.String &&
                        !facts.Value.IsKnown &&
                        set.Value is BoundVariableExpression directSource)
                    {
                        AppendCDirectStringCopy(
                            source,
                            indent,
                            set.Variable,
                            directSource.Variable,
                            identifiers,
                            exactStringLengths);
                    }
                    else if (set.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        AppendCRuntimeStringAssignment(
                            source,
                            indent,
                            set.Variable,
                            set.Value,
                            runtimeStringBuffers[set],
                            identifiers,
                            integers,
                            exactStringLengths,
                            runtimeExpressionBuffers,
                            declareBuffer: true);
                    }
                    else
                    {
                        SmileValue setValue = facts.Value.IsKnown
                            ? facts.Value.Value
                            : default;
                        string value = set.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(setValue, integers)
                            : TargetExpression.C(
                                set.Value,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {value};");
                        if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                        {
                            source.AppendLine($"{indent}{setLengthName} = {Utf8ByteLength(setValue)};");
                        }
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendCPrint(
                        source,
                        indent,
                        print,
                        identifiers,
                        integers,
                        facts.Value.IsKnown,
                        GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundIfStatement conditional:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        analysis,
                        identifiers,
                        integers,
                        exactStringLengths,
                        runtimeStringBuffers,
                        runtimeExpressionBuffers,
                        ref emittedDeclaration,
                        ref emittedExecutable,
                        ref emittedBodyStatement);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.C(
                    clause.Condition,
                    identifiers,
                    integers,
                    GeneratorConditionFacts.KnownValues(clauseFacts.ValuesBefore),
                    exactStringLengths,
                    runtimeExpressionBuffers))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendCPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool valueIsKnown,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        if (TryAppendDirectStringVariablePrint(
            source,
            indent,
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (!valueIsKnown && TryAppendRuntimeStringSegments(
                source,
                indent,
                print,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers,
                TargetLanguage.C))
        {
            return;
        }

        if (valueIsKnown && TryAppendExactNulStringPrint(source, indent, print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.C(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers),
            integers.RequiresSigned64Storage);
        AppendPrintfCall(source, indent, plan);
    }

    internal static bool TryAppendRuntimeStringSegments(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        TargetLanguage language)
    {
        if (print.IsBlankLine ||
            print.Value.Type is not SmileType.String ||
            !RuntimeTextPlan.CanFlatten(print.Value))
        {
            return false;
        }

        IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
        bool needsExactStreaming = segments.Any(segment => segment switch
        {
            RuntimeLiteralTextSegment literal => literal.Text.Contains('\0', StringComparison.Ordinal),
            RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable } =>
                variable.Variable.Type is SmileType.String &&
                exactStringLengths.ContainsKey(variable.Variable),
            _ => false
        });
        if (!needsExactStreaming)
        {
            return false;
        }

        foreach (RuntimeTextSegment segment in segments)
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    int byteLength = Encoding.UTF8.GetByteCount(literal.Text);
                    if (byteLength > 0)
                    {
                        source.Append(indent).Append("fwrite(")
                            .Append(TargetEscapes.CString(literal.Text))
                            .Append(", 1, ")
                            .Append(byteLength.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(", stdout);");
                    }

                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string name = identifiers.Get(variable.Variable);
                    if (exactStringLengths.TryGetValue(variable.Variable, out string? lengthName))
                    {
                        source.Append(indent).Append("fwrite(").Append(name).Append(", 1, ")
                            .Append(lengthName).AppendLine(", stdout);");
                    }
                    else
                    {
                        source.Append(indent).Append("fputs(").Append(name).AppendLine(", stdout);");
                    }

                    break;

                case RuntimeExpressionTextSegment expression:
                    string rendered = language is TargetLanguage.ObjectiveC
                        ? TargetExpression.ObjectiveC(
                            expression.Expression,
                            identifiers,
                            integers,
                            values,
                            exactStringLengths,
                            runtimeExpressionBuffers)
                        : TargetExpression.C(
                            expression.Expression,
                            identifiers,
                            integers,
                            values,
                            exactStringLengths,
                            runtimeExpressionBuffers);
                    CPrintfPlan typedPlan = CPrintfPlan.FromPrint(
                        new BoundPrintStatement(expression.Expression, IsBlankLine: false),
                        _ => rendered,
                        integers.RequiresSigned64Storage);
                    // Remove the newline owned by the complete PRINT; each
                    // live segment is emitted without advancing here.
                    AppendPrintfCall(
                        source,
                        indent,
                        typedPlan with
                        {
                            FormatText = typedPlan.FormatText[..^1]
                        });
                    break;
            }
        }

        source.Append(indent).AppendLine("fputc('\\n', stdout);");
        return true;
    }

    internal static bool TryAppendDirectStringVariablePrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (print.IsBlankLine ||
            print.Value is not BoundVariableExpression variable ||
            variable.Variable.Type is not SmileType.String)
        {
            return false;
        }

        string name = identifiers.Get(variable.Variable);
        if (exactStringLengths.TryGetValue(variable.Variable, out string? lengthName))
        {
            // Exact mutable Strings are pointer-plus-length values in C. Read
            // both pieces of current target storage instead of re-emitting the
            // statement's statically known bytes as an unrelated print literal.
            source.Append(indent).Append("fwrite(").Append(name).Append(", 1, ")
                .Append(lengthName).AppendLine(", stdout);");
            source.Append(indent).AppendLine("fputc('\\n', stdout);");
        }
        else
        {
            source.Append(indent).Append("printf(\"%s\\n\", ").Append(name).AppendLine(");");
        }

        return true;
    }

    internal static void AppendPrintfCall(StringBuilder source, string indent, CPrintfPlan plan)
    {
        source.Append(indent);
        source.Append("printf(");
        source.Append(TargetEscapes.CPrintfFormatString(plan.FormatText));

        foreach (string argument in plan.Arguments)
        {
            source.Append(", ");
            source.Append(argument);
        }

        source.AppendLine(");");
    }

    internal static bool TryAppendExactNulStringPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (print.IsBlankLine ||
            !GeneratorValueFacts.TryGetNulContainingString(print.Value, values, out string value))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);

        // A tiny nested scope lets every exact PRINT reuse the same readable
        // compiler-owned name without colliding with a SMILE variable in the
        // surrounding main function. The byte array avoids C's NUL-terminated
        // String convention and makes the UTF-8 length explicit to fwrite.
        source.Append(indent).AppendLine("{");
        source.Append(indent).Append("    static const unsigned char smilePrintBytes[] = { ");
        source.Append(string.Join(", ", bytes.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        source.AppendLine(" };");
        source.Append(indent).Append("    fwrite(smilePrintBytes, 1, ");
        source.Append(bytes.Length.ToString(CultureInfo.InvariantCulture));
        source.AppendLine(", stdout);");
        source.Append(indent).AppendLine("    fputc('\\n', stdout);");
        source.Append(indent).AppendLine("}");
        return true;
    }

    internal static IReadOnlyDictionary<VariableSymbol, string> CreateExactStringLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramAnalysis analysis,
        IEnumerable<VariableSymbol>? additionalVariables = null)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<VariableSymbol> additional = additionalVariables?.ToHashSet() ??
            new HashSet<VariableSymbol>();

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            if (variable.Type is not SmileType.String ||
                !analysis.AssignedValuesMayContainNul(variable) &&
                !additional.Contains(variable))
            {
                continue;
            }

            string preferred = $"smileString{index}Length";
            string name = preferred;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            names.Add(variable, name);
        }

        return names;
    }

    internal static IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer>
        CreateRuntimeStringBuffers(
            BoundProgram program,
            TargetIdentifierMap identifiers,
            BoundProgramAnalysis analysis)
    {
        var needsBuffer = new List<(BoundStatement Statement, VariableSymbol Variable)>();
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement { Variable.Type: SmileType.String } let when
                    !facts.Value.IsKnown && let.Initializer is not BoundVariableExpression:
                    needsBuffer.Add((let, let.Variable));
                    break;

                case BoundSetStatement { Variable.Type: SmileType.String } set when
                    !facts.Value.IsKnown && set.Value is not BoundVariableExpression:
                    needsBuffer.Add((set, set.Variable));
                    break;
            }
        }

        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);
        var buffers = new Dictionary<BoundStatement, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < needsBuffer.Count; index++)
        {
            (BoundStatement statement, VariableSymbol variable) = needsBuffer[index];

            string preferred = $"smileString{index}Buffer";
            string name = preferred;
            int suffix = 2;
            while (used.Contains(name) || used.Contains(name + "Used"))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            used.Add(name);
            used.Add(name + "Used");

            buffers.Add(
                statement,
                new RuntimeStringBuffer(
                    name,
                    Math.Max(1, analysis.MaximumAssignedUtf8ByteLength(variable))));
        }

        return buffers;
    }

    internal static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateRuntimeExpressionBuffers(
            BoundProgram program,
            TargetIdentifierMap identifiers,
            BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);

        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    Collect(let.Initializer, facts.ValuesBefore);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value, facts.ValuesBefore);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    Collect(print.Value, facts.ValuesBefore);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Collect(
                            clause.Condition,
                            analysis.GetClauseFacts(clause).ValuesBefore);
                    }

                    break;
            }
        }

        return buffers;

        void Collect(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> valuesBefore)
        {
            if (expression is BoundBinaryExpression comparison &&
                comparison.Left.Type is SmileType.String &&
                comparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(comparison.Left, valuesBefore);
                Add(comparison.Right, valuesBefore);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Collect(unary.Operand, valuesBefore);
                    break;

                case BoundBinaryExpression binary:
                    Collect(binary.Left, valuesBefore);
                    Collect(binary.Right, valuesBefore);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Collect(hole.Expression, valuesBefore);
                    }

                    break;
            }
        }

        void Add(
            BoundExpression operand,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> valuesBefore)
        {
            if (operand is BoundVariableExpression or BoundStringLiteralExpression ||
                buffers.ContainsKey(operand) ||
                GeneratorConditionFacts.TryEvaluateFromAnalyzedValues(
                    operand,
                    valuesBefore,
                    out SmileValue knownOperand) &&
                knownOperand.Type is SmileType.String &&
                !knownOperand.StringValue.Contains('\0', StringComparison.Ordinal))
            {
                return;
            }

            string preferred = $"smileExpression{buffers.Count}Buffer";
            string name = preferred;
            int suffix = 2;
            while (used.Contains(name) || used.Contains(name + "Used"))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            used.Add(name);
            used.Add(name + "Used");
            buffers.Add(
                operand,
                new RuntimeStringBuffer(
                    name,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    internal static void AppendCRuntimeStringAssignment(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool declareBuffer)
    {
        string workLength = buffer.Name + "Used";
        source.Append(indent).AppendLine("{");
        if (declareBuffer)
        {
            source.Append(indent).Append("    static char ").Append(buffer.Name).Append('[')
                .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                .AppendLine("] = { 0 };");
        }

        source.Append(indent).Append("    size_t ").Append(workLength).AppendLine(" = 0;");
        AppendCRuntimeTextSegments(
            source,
            indent + "    ",
            expression,
            buffer,
            workLength,
            identifiers,
            integers,
            exactStringLengths,
            runtimeExpressionBuffers);
        source.Append(indent).Append("    ").Append(buffer.Name).Append('[')
            .Append(workLength).AppendLine("] = '\\0';");
        source.Append(indent).Append("    ").Append(identifiers.Get(destination))
            .Append(" = ").Append(buffer.Name).AppendLine(";");
        source.Append(indent).Append("    ").Append(exactStringLengths[destination])
            .Append(" = ").Append(workLength).AppendLine(";");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendCRuntimeTextSegments(
        StringBuilder source,
        string indent,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        string workLength,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    int literalLength = Encoding.UTF8.GetByteCount(literal.Text);
                    if (literalLength == 0)
                    {
                        break;
                    }

                    source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
                        .Append(workLength).Append(", ").Append(TargetEscapes.CString(literal.Text))
                        .Append(", ").Append(literalLength.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(");");
                    source.Append(indent).Append(workLength).Append(" += ")
                        .Append(literalLength.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string variableName = identifiers.Get(variable.Variable);
                    string variableLength = exactStringLengths.TryGetValue(
                        variable.Variable,
                        out string? exactLength)
                        ? exactLength
                        : $"strlen({variableName})";
                    source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
                        .Append(workLength).Append(", ").Append(variableName).Append(", ")
                        .Append(variableLength).AppendLine(");");
                    source.Append(indent).Append(workLength).Append(" += ")
                        .Append(variableLength).AppendLine(";");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Integer:
                    string integer = TargetExpression.C(
                        typed.Expression,
                        identifiers,
                        integers,
                        new Dictionary<VariableSymbol, SmileValue>(),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    string integerFormat = integers.RequiresSigned64Storage ? "%lld" : "%d";
                    string integerArgument = integers.RequiresSigned64Storage
                        ? $"(long long)({integer})"
                        : integer;
                    source.Append(indent).Append(workLength).Append(" += (size_t)snprintf(")
                        .Append(buffer.Name).Append(" + ").Append(workLength).Append(", ")
                        .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                        .Append(" - ").Append(workLength).Append(", \"")
                        .Append(integerFormat).Append("\", ").Append(integerArgument)
                        .AppendLine(");");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Boolean:
                    string boolean = TargetExpression.C(
                        typed.Expression,
                        identifiers,
                        integers,
                        new Dictionary<VariableSymbol, SmileValue>(),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    source.Append(indent).Append("if (").Append(boolean).AppendLine(")");
                    source.Append(indent).AppendLine("{");
                    AppendCFixedRuntimeText(source, indent + "    ", buffer, workLength, "TRUE");
                    source.Append(indent).AppendLine("}");
                    source.Append(indent).AppendLine("else");
                    source.Append(indent).AppendLine("{");
                    AppendCFixedRuntimeText(source, indent + "    ", buffer, workLength, "FALSE");
                    source.Append(indent).AppendLine("}");
                    break;
            }
        }
    }

    private static void AppendCFixedRuntimeText(
        StringBuilder source,
        string indent,
        RuntimeStringBuffer buffer,
        string workLength,
        string text)
    {
        source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
            .Append(workLength).Append(", \"").Append(text).Append("\", ")
            .Append(text.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        source.Append(indent).Append(workLength).Append(" += ")
            .Append(text.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
    }

    internal static void AppendCDirectStringCopy(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        VariableSymbol sourceVariable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        string destinationName = identifiers.Get(destination);
        string sourceName = identifiers.Get(sourceVariable);
        source.AppendLine($"{indent}{destinationName} = {sourceName};");
        if (!exactStringLengths.TryGetValue(destination, out string? destinationLength))
        {
            return;
        }

        string sourceLength = exactStringLengths.TryGetValue(sourceVariable, out string? exactSourceLength)
            ? exactSourceLength
            : $"strlen({sourceName})";
        source.AppendLine($"{indent}{destinationLength} = {sourceLength};");
    }

    internal static int Utf8ByteLength(SmileValue value) =>
        Encoding.UTF8.GetByteCount(value.StringValue);

}

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    private const string IntegerFormatBufferLabel = "smileIntegerFormatBuffer";
    private const string IntegerFormatProcedure = "smileFormatInteger";

    private sealed record RuntimeStringBuffer(
        BoundExpression Expression,
        string Label,
        int Capacity);

    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(BoundProgram program)
    {
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundPrintStatement[] prints = analysis.EnumerateStatements()
            .OfType<BoundPrintStatement>()
            .ToArray();
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes = lets
            .Select((let, index) => (let.Variable, index))
            .ToDictionary(item => item.Variable, item => item.index);
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers =
            CreateMasmStatementBuffers(analysis);
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>
            conditionBuffers = CreateMasmConditionBuffers(analysis);
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers =
            CreateMasmBooleanStringBuffers(analysis);
        bool needsIntegerFormatter = NeedsMasmIntegerFormatter(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        bool needsBooleanText = NeedsMasmBooleanText(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        var source = new StringBuilder();

        AppendMasmLine(source, "option casemap:none", "Keep symbol names case-sensitive.");
        source.AppendLine();

        if (prints.Length > 0)
        {
            AppendMasmLine(source, "EXTERN GetStdHandle:PROC", "Windows API: get standard console handles.");
            AppendMasmLine(source, "EXTERN WriteFile:PROC", "Windows API: write bytes to the console.");
        }

        AppendMasmLine(source, "EXTERN ExitProcess:PROC", "Windows API: terminate the process.");
        source.AppendLine();

        AppendMasmData(
            source,
            analysis,
            variableIndexes,
            prints.Length,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            needsIntegerFormatter,
            needsBooleanText);
        AppendMasmCode(
            source,
            program,
            analysis,
            variableIndexes,
            prints.Length,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            needsIntegerFormatter);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmData(
        StringBuilder source,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        bool needsIntegerFormatter,
        bool needsBooleanText)
    {
        if (variableIndexes.Count == 0 &&
            printCount == 0 &&
            statementBuffers.Count == 0 &&
            conditionBuffers.Values.All(buffers => buffers.Count == 0) &&
            booleanStringBuffers.Count == 0 &&
            !needsIntegerFormatter &&
            !needsBooleanText)
        {
            return;
        }

        AppendMasmLine(source, ".data", "Static bytes and variables live here.");

        if (printCount > 0)
        {
            AppendMasmLine(source, "STD_OUTPUT_HANDLE EQU -11", "Magic value for the console output handle.");
        }

        int printIndex = 0;
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    int variableIndex = variableIndexes[let.Variable];
                    string valueLabel = VariableValueLabel(variableIndex);
                    string initialText = facts.Value.IsKnown
                        ? facts.Value.Value.ToDisplayText()
                        : string.Empty;
                    AppendMasmStringData(
                        source,
                        valueLabel,
                        initialText,
                        $"LET {let.Variable.Name} initial text.",
                        "Length of the variable's current text.");
                    AppendMasmLine(source, $"{VariablePointerLabel(variableIndex)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
                    AppendMasmLine(source, $"{VariableLengthLabel(variableIndex)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
                    if (let.Variable.Type is SmileType.Integer)
                    {
                        AppendMasmLine(
                            source,
                            $"{VariableIntegerLabel(variableIndex)} QWORD ?",
                            $"Runtime signed Integer value for {let.Variable.Name} conditions.");
                    }
                    else if (let.Variable.Type is SmileType.Boolean)
                    {
                        AppendMasmLine(
                            source,
                            $"{VariableBooleanLabel(variableIndex)} BYTE ?",
                            $"Runtime Boolean value for {let.Variable.Name} expressions.");
                    }

                    break;

                case BoundSetStatement set:
                    if (!facts.Value.IsKnown)
                    {
                        // Runtime lowering below reads current storage or
                        // materializes the complete expression on its reached
                        // path. Never bake the selected concrete branch here.
                        break;
                    }

                    string setText = facts.Value.Value.ToDisplayText();
                    AppendMasmStringData(
                        source,
                        SetValueLabel(facts.Ordinal),
                        setText,
                        $"SET {set.Variable.Name} assigned text.",
                        "Length of this assigned value.");
                    break;

                case BoundPrintStatement print:
                    AppendMasmPrintData(source, print, facts, printIndex);

                    printIndex++;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        int comparisonIndex = 0;
                        AppendMasmConditionData(
                            source,
                            clause.Condition,
                            analysis.GetClauseFacts(clause),
                            conditionBuffers[clause],
                            ref comparisonIndex);
                    }

                    break;
            }
        }

        foreach (RuntimeStringBuffer buffer in statementBuffers.Values
                     .Concat(conditionBuffers.Values.SelectMany(value => value))
                     .Concat(booleanStringBuffers.Values))
        {
            AppendMasmLine(
                source,
                $"{buffer.Label} BYTE {buffer.Capacity} DUP (?)",
                "Stable runtime text storage for one source expression.");
            AppendMasmLine(
                source,
                $"{buffer.Label}Length DWORD ?",
                "Logical UTF-8 byte length of this runtime text.");
        }

        if (needsIntegerFormatter)
        {
            AppendMasmLine(
                source,
                $"{IntegerFormatBufferLabel} BYTE 21 DUP (?)",
                "Temporary signed Int64 decimal text (sign plus 19 digits).");
        }

        if (needsBooleanText)
        {
            AppendMasmStringData(
                source,
                "smileBooleanTrue",
                "TRUE",
                "Canonical runtime Boolean true text.",
                "Length of canonical true text.");
            AppendMasmStringData(
                source,
                "smileBooleanFalse",
                "FALSE",
                "Canonical runtime Boolean false text.",
                "Length of canonical false text.");
        }

        if (printCount > 0)
        {
            AppendMasmLine(source, "newline BYTE 13, 10", "SMILE PRINT appends CR/LF on Windows.");
            AppendMasmLine(source, "newlineLength EQU $ - newline", "Length of the newline bytes.");
            AppendMasmLine(source, "stdoutHandle QWORD ?", "Cached standard output handle.");
            AppendMasmLine(source, "bytesWritten DWORD ?", "WriteFile stores how many bytes it wrote.");
        }

        source.AppendLine();
    }

    private static void AppendMasmPrintData(
        StringBuilder source,
        BoundPrintStatement print,
        BoundStatementAnalysis facts,
        int printIndex)
    {
        if (!print.IsBlankLine && print.Value is BoundVariableExpression)
        {
            return;
        }

        if (print.IsBlankLine || facts.Value.IsKnown)
        {
            string text = print.IsBlankLine
                ? string.Empty
                : facts.Value.Value.ToDisplayText();
            AppendMasmStringData(
                source,
                PrintLiteralLabel(printIndex, 0),
                text,
                $"PRINT #{printIndex + 1} canonical text.",
                "Length of this print text.");
            return;
        }

        IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            if (segments[segmentIndex] is not RuntimeLiteralTextSegment { Text.Length: > 0 } literal)
            {
                continue;
            }

            AppendMasmStringData(
                source,
                PrintLiteralLabel(printIndex, segmentIndex),
                literal.Text,
                $"PRINT #{printIndex + 1} literal segment.",
                "Length of this print segment.");
        }
    }

    private static void AppendMasmStringData(
        StringBuilder source,
        string label,
        string value,
        string valueComment,
        string lengthComment)
    {
        AppendMasmLine(source, $"{label} BYTE {TargetEscapes.MasmByteInitializers(value)}", valueComment);

        // MASM needs at least one byte after a BYTE label, so the empty string
        // uses a 0 placeholder for storage. The logical SMILE string length is
        // still zero; otherwise WriteFile would emit an invisible NUL byte.
        string lengthExpression = Encoding.UTF8.GetByteCount(value) == 0
            ? "0"
            : $"$ - {label}";
        AppendMasmLine(source, $"{label}Length EQU {lengthExpression}", lengthComment);
    }

    private static void AppendMasmConditionData(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyList<RuntimeStringBuffer> runtimeBuffers,
        ref int comparisonIndex)
    {
        if (expression is BoundUnaryExpression unary)
        {
            AppendMasmConditionData(
                source,
                unary.Operand,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            return;
        }

        if (expression is not BoundBinaryExpression binary)
        {
            return;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            AppendMasmConditionData(
                source,
                binary.Left,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            AppendMasmConditionData(
                source,
                binary.Right,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            return;
        }

        if (!CanEmitMasmDirectEquality(binary))
        {
            return;
        }

        int currentComparison = comparisonIndex++;
        AppendMasmConditionOperandData(
            source,
            binary.Left,
            clauseFacts,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, currentComparison, "Left"));
        AppendMasmConditionOperandData(
            source,
            binary.Right,
            clauseFacts,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, currentComparison, "Right"));
    }

    private static void AppendMasmConditionOperandData(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyList<RuntimeStringBuffer> runtimeBuffers,
        string label)
    {
        if (expression is BoundVariableExpression ||
            runtimeBuffers.Any(buffer => ReferenceEquals(buffer.Expression, expression)))
        {
            return;
        }

        string text = expression switch
        {
            BoundStringLiteralExpression literal => literal.Value,
            BoundIntegerLiteralExpression literal =>
                literal.Value.ToString(CultureInfo.InvariantCulture),
            BoundBooleanLiteralExpression literal => literal.Value ? "TRUE" : "FALSE",
            _ => throw new InvalidOperationException(
                "A static MASM IF operand must be a bound literal.")
        };
        AppendMasmStringData(
            source,
            label,
            text,
            "Static operand for a runtime IF comparison.",
            "Length of this IF operand.");
    }

    private static void AppendMasmCode(
        StringBuilder source,
        BoundProgram program,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        bool needsIntegerFormatter)
    {
        AppendMasmLine(source, ".code", "CPU instructions live here.");
        AppendMasmLine(source, "main PROC", "Program entry point.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");

        if (printCount > 0)
        {
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, STD_OUTPUT_HANDLE", "Ask Windows for stdout.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stdout handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stdoutHandle], rax", "Cache stdout for every PRINT segment.");
        }

        int printIndex = 0;
        AppendMasmStatements(
            source,
            program.Statements,
            analysis,
            variableIndexes,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            ref printIndex);

        source.AppendLine();
        AppendMasmLine(source, "    xor ecx, ecx", "ExitProcess arg 1: process exit code 0.");
        AppendMasmLine(source, "    call ExitProcess", "End the program.");
        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        if (needsIntegerFormatter)
        {
            source.AppendLine();
            AppendMasmIntegerFormatter(source);
        }

        source.AppendLine();
        source.AppendLine("END");
    }

    private static void AppendMasmStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int printIndex)
    {
        foreach (BoundStatement statement in statements)
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    if (!facts.Value.IsKnown &&
                        let.Initializer is BoundVariableExpression directLetSource)
                    {
                        AppendMasmStorageCopy(
                            source,
                            variableIndexes[let.Variable],
                            variableIndexes[directLetSource.Variable],
                            let.Variable.Type,
                            let.Variable.Name,
                            directLetSource.Variable.Name);
                    }
                    else if (!facts.Value.IsKnown)
                    {
                        AppendMasmRuntimeAssignment(
                            source,
                            let.Variable,
                            let.Initializer,
                            statementBuffers[let],
                            variableIndexes,
                            $"let{facts.Ordinal}",
                            booleanStringBuffers);
                    }
                    else
                    {
                        AppendMasmStorageUpdate(
                            source,
                            variableIndexes[let.Variable],
                            let.Variable,
                            facts.Value.Value,
                            VariableValueLabel(variableIndexes[let.Variable]),
                            $"Address of LET {let.Variable.Name} text.");
                    }

                    break;

                case BoundSetStatement set:
                    if (!facts.Value.IsKnown && set.Value is BoundVariableExpression directSource)
                    {
                        AppendMasmStorageCopy(
                            source,
                            variableIndexes[set.Variable],
                            variableIndexes[directSource.Variable],
                            set.Variable.Type,
                            set.Variable.Name,
                            directSource.Variable.Name);
                    }
                    else if (!facts.Value.IsKnown)
                    {
                        AppendMasmRuntimeAssignment(
                            source,
                            set.Variable,
                            set.Value,
                            statementBuffers[set],
                            variableIndexes,
                            $"set{facts.Ordinal}",
                            booleanStringBuffers);
                    }
                    else
                    {
                        AppendMasmStorageUpdate(
                            source,
                            variableIndexes[set.Variable],
                            set.Variable,
                            facts.Value.Value,
                            SetValueLabel(facts.Ordinal),
                            $"Address of SET {set.Variable.Name} text.");
                    }

                    break;

                case BoundPrintStatement print:
                    AppendMasmPrint(
                        source,
                        print,
                        facts,
                        printIndex,
                        variableIndexes,
                        $"print{printIndex}",
                        booleanStringBuffers);
                    printIndex++;
                    break;

                case BoundIfStatement conditional:
                    AppendMasmIf(
                        source,
                        conditional,
                        analysis,
                        variableIndexes,
                        statementBuffers,
                        conditionBuffers,
                        booleanStringBuffers,
                        ref printIndex);
                    break;
            }
        }
    }

    private static void AppendMasmIf(
        StringBuilder source,
        BoundIfStatement conditional,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int printIndex)
    {
        int ifOrdinal = analysis.GetIfOrdinal(conditional);
        string endLabel = IfEndLabel(ifOrdinal);

        source.AppendLine();
        AppendMasmLine(source, $"; IF #{ifOrdinal + 1}", "Evaluate clauses in source order.");
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            if (clauseIndex > 0)
            {
                AppendMasmLine(source, $"{IfClauseLabel(ifOrdinal, clauseIndex)}:", "Next ELSE IF clause.");
            }

            string falseLabel = clauseIndex + 1 < conditional.Clauses.Count
                ? IfClauseLabel(ifOrdinal, clauseIndex + 1)
                : conditional.HasElseClause
                    ? IfElseLabel(ifOrdinal)
                    : endLabel;
            int comparisonIndex = 0;
            int partIndex = 0;
            var runtimeBufferMap = new Dictionary<BoundExpression, RuntimeStringBuffer>(
                ReferenceEqualityComparer.Instance);
            foreach (RuntimeStringBuffer buffer in conditionBuffers[clause])
            {
                runtimeBufferMap.Add(buffer.Expression, buffer);
            }

            AppendMasmCondition(
                source,
                clause.Condition,
                clauseFacts,
                variableIndexes,
                runtimeBufferMap,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, "    test eax, eax", "Zero means this clause did not match.");
            AppendMasmLine(source, $"    jz {falseLabel}", "Continue with the next clause or ELSE.");
            AppendMasmStatements(
                source,
                clause.Statements,
                analysis,
                variableIndexes,
                statementBuffers,
                conditionBuffers,
                booleanStringBuffers,
                ref printIndex);
            AppendMasmLine(source, $"    jmp {endLabel}", "Only one IF branch executes.");
        }

        if (conditional.HasElseClause)
        {
            AppendMasmLine(source, $"{IfElseLabel(ifOrdinal)}:", "Final ELSE branch.");
            AppendMasmStatements(
                source,
                conditional.ElseStatements,
                analysis,
                variableIndexes,
                statementBuffers,
                conditionBuffers,
                booleanStringBuffers,
                ref printIndex);
        }

        AppendMasmLine(source, $"{endLabel}:", "Continue after the complete IF.");
    }

    private static void AppendMasmCondition(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int comparisonIndex,
        ref int partIndex)
    {
        if (expression is BoundUnaryExpression
            {
                Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
            } unary)
        {
            AppendMasmCondition(
                source,
                unary.Operand,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, "    xor eax, 1", "Invert the normalized Boolean condition result.");
            return;
        }

        if (expression is BoundBinaryExpression binary &&
            binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
        {
            AppendMasmCondition(
                source,
                binary.Left,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            string endLabel = MasmConditionPartLabel(
                clauseFacts.Ordinal,
                partIndex++,
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd ? "AndEnd" : "OrEnd");
            AppendMasmLine(source, "    test eax, eax", "Honor SMILE's left-to-right short circuit.");
            AppendMasmLine(
                source,
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                    ? $"    jz {endLabel}"
                    : $"    jnz {endLabel}",
                "Skip the unreachable right condition.");
            AppendMasmCondition(
                source,
                binary.Right,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, $"{endLabel}:", "Complete this logical condition.");
            return;
        }

        if (expression is BoundBinaryExpression integerComparison &&
            CanEmitMasmDirectIntegerComparison(integerComparison))
        {
            AppendMasmDirectIntegerComparison(
                source,
                integerComparison,
                variableIndexes);
            return;
        }

        if (expression is BoundBinaryExpression booleanComparison &&
            booleanComparison.Left.Type is SmileType.Boolean &&
            booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality)
        {
            AppendMasmBooleanExpression(
                source,
                booleanComparison,
                variableIndexes,
                $"ifCondition{clauseFacts.Ordinal}",
                booleanStringBuffers,
                ref partIndex);
            return;
        }

        if (expression is BoundBinaryExpression comparison &&
            CanEmitMasmDirectEquality(comparison))
        {
            foreach (BoundExpression operand in new[] { comparison.Left, comparison.Right })
            {
                if (runtimeBuffers.TryGetValue(operand, out RuntimeStringBuffer? buffer))
                {
                    AppendMasmRuntimeTextMaterialization(
                        source,
                        operand,
                        buffer,
                        variableIndexes,
                        $"ifCondition{clauseFacts.Ordinal}Part{partIndex}",
                        booleanStringBuffers,
                        ref partIndex);
                }
            }

            AppendMasmDirectEquality(
                source,
                comparison,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                comparisonIndex++,
                ref partIndex);
            return;
        }

        if (!GeneratorConditionFacts.TryEvaluateFromAnalyzedValues(
                expression,
                clauseFacts.ValuesBefore,
                out SmileValue provenCondition))
        {
            throw new InvalidOperationException(
                "MASM requires runtime lowering for an abstract-unknown IF condition.");
        }

        AppendMasmLine(
            source,
            $"    mov eax, {(provenCondition.BooleanValue ? 1 : 0)}",
            "Materialize an unsupported proven condition without deleting its branch.");
    }

    private static bool CanEmitMasmDirectEquality(BoundBinaryExpression expression) =>
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality) &&
        expression.Left.Type is not SmileType.Integer &&
        IsMasmDirectConditionOperand(expression.Left) &&
        IsMasmDirectConditionOperand(expression.Right) &&
        (ContainsVariableRead(expression.Left) || ContainsVariableRead(expression.Right));

    private static bool IsMasmDirectConditionOperand(BoundExpression expression) =>
        expression.Type is SmileType.String
            ? RuntimeTextPlan.CanFlatten(expression)
            : expression is BoundVariableExpression or
            BoundStringLiteralExpression or
            BoundIntegerLiteralExpression or
            BoundBooleanLiteralExpression;

    private static bool ContainsVariableRead(BoundExpression expression) =>
        expression switch
        {
            BoundVariableExpression => true,
            BoundUnaryExpression unary => ContainsVariableRead(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsVariableRead(binary.Left) || ContainsVariableRead(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsVariableRead(hole.Expression)),
            _ => false
        };

    private static bool CanEmitMasmDirectIntegerComparison(BoundBinaryExpression expression) =>
        expression.Left.Type is SmileType.Integer &&
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality or
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals) &&
        CanEmitMasmIntegerExpression(expression.Left) &&
        CanEmitMasmIntegerExpression(expression.Right);

    private static bool CanEmitMasmIntegerExpression(BoundExpression expression) =>
        expression switch
        {
            BoundVariableExpression { Variable.Type: SmileType.Integer } => true,
            BoundIntegerLiteralExpression => true,
            BoundUnaryExpression unary when
                unary.Operator.Kind is BoundUnaryOperatorKind.Identity or
                    BoundUnaryOperatorKind.Negation =>
                CanEmitMasmIntegerExpression(unary.Operand),
            BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division =>
                CanEmitMasmIntegerExpression(binary.Left) &&
                CanEmitMasmIntegerExpression(binary.Right),
            _ => false
        };

    private static void AppendMasmDirectIntegerComparison(
        StringBuilder source,
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes)
    {
        AppendMasmIntegerExpression(source, expression.Left, variableIndexes);
        AppendMasmLine(source, "    push rax", "Preserve the left signed Integer operand.");
        AppendMasmIntegerExpression(source, expression.Right, variableIndexes);
        AppendMasmLine(source, "    mov r9, rax", "Keep the right signed Integer operand.");
        AppendMasmLine(source, "    pop rax", "Restore the left signed Integer operand.");
        AppendMasmLine(source, "    cmp rax, r9", "Compare current signed Integer values.");
        string setInstruction = expression.Operator.Kind switch
        {
            BoundBinaryOperatorKind.Equality => "sete al",
            BoundBinaryOperatorKind.Inequality => "setne al",
            BoundBinaryOperatorKind.Less => "setl al",
            BoundBinaryOperatorKind.LessOrEquals => "setle al",
            BoundBinaryOperatorKind.Greater => "setg al",
            BoundBinaryOperatorKind.GreaterOrEquals => "setge al",
            _ => throw new InvalidOperationException("Unsupported MASM Integer comparison.")
        };
        AppendMasmLine(source, $"    {setInstruction}", "Materialize the signed comparison result.");
        AppendMasmLine(source, "    movzx eax, al", "Normalize the comparison result to zero or one.");
    }

    private static void AppendMasmIntegerExpression(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes)
    {
        switch (expression)
        {
            case BoundVariableExpression variable:
                AppendMasmLine(
                    source,
                    $"    mov rax, QWORD PTR [{VariableIntegerLabel(variableIndexes[variable.Variable])}]",
                    $"Read current signed Integer storage for {variable.Variable.Name}.");
                return;

            case BoundIntegerLiteralExpression literal:
                AppendMasmLine(
                    source,
                    $"    mov rax, {MasmIntegerImmediate(literal.Value)}",
                    "Materialize this signed Integer literal.");
                return;

            case BoundUnaryExpression unary:
                AppendMasmIntegerExpression(source, unary.Operand, variableIndexes);
                if (unary.Operator.Kind is BoundUnaryOperatorKind.Negation)
                {
                    AppendMasmLine(source, "    neg rax", "Apply SMILE signed Integer negation.");
                }

                return;

            case BoundBinaryExpression binary:
                AppendMasmIntegerExpression(source, binary.Left, variableIndexes);
                AppendMasmLine(source, "    push rax", "Preserve the left arithmetic operand.");
                AppendMasmIntegerExpression(source, binary.Right, variableIndexes);
                AppendMasmLine(source, "    mov r9, rax", "Keep the right arithmetic operand.");
                AppendMasmLine(source, "    pop rax", "Restore the left arithmetic operand.");
                switch (binary.Operator.Kind)
                {
                    case BoundBinaryOperatorKind.Addition:
                        AppendMasmLine(source, "    add rax, r9", "Apply SMILE signed Integer addition.");
                        break;

                    case BoundBinaryOperatorKind.Subtraction:
                        AppendMasmLine(source, "    sub rax, r9", "Apply SMILE signed Integer subtraction.");
                        break;

                    case BoundBinaryOperatorKind.Multiplication:
                        AppendMasmLine(source, "    imul rax, r9", "Apply SMILE signed Integer multiplication.");
                        break;

                    case BoundBinaryOperatorKind.Division:
                        AppendMasmLine(source, "    cqo", "Extend the signed dividend into RDX:RAX.");
                        AppendMasmLine(source, "    idiv r9", "Apply truncating signed Integer division.");
                        break;
                }

                return;
        }
    }

    private static void AppendMasmDirectEquality(
        StringBuilder source,
        BoundBinaryExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        int comparisonIndex,
        ref int partIndex)
    {
        string loopLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Compare");
        string differentLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Different");
        string doneLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Done");

        AppendMasmLoadConditionOperand(
            source,
            expression.Left,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, comparisonIndex, "Left"),
            "r10",
            "ecx");
        AppendMasmLoadConditionOperand(
            source,
            expression.Right,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, comparisonIndex, "Right"),
            "r11",
            "edx");
        AppendMasmLine(source, "    mov eax, 1", "Assume equal until a length or byte differs.");
        AppendMasmLine(source, "    cmp ecx, edx", "Exact SMILE values must have equal logical lengths.");
        AppendMasmLine(source, $"    jne {differentLabel}", "Different lengths cannot be equal.");
        AppendMasmLine(source, "    test ecx, ecx", "Empty values are equal when both lengths are zero.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes remain to compare.");
        AppendMasmLine(source, $"{loopLabel}:", "Compare current target storage one byte at a time.");
        AppendMasmLine(source, "    mov r8b, BYTE PTR [r10]", "Read the next left byte.");
        AppendMasmLine(source, "    cmp r8b, BYTE PTR [r11]", "Compare it to the next right byte.");
        AppendMasmLine(source, $"    jne {differentLabel}", "A differing byte makes the values unequal.");
        AppendMasmLine(source, "    inc r10", "Advance the left pointer.");
        AppendMasmLine(source, "    inc r11", "Advance the right pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down the shared logical length.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue until every byte matches.");
        AppendMasmLine(source, $"    jmp {doneLabel}", "The complete values are equal.");
        AppendMasmLine(source, $"{differentLabel}:", "Normalize inequality to Boolean zero.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means the values differ.");
        AppendMasmLine(source, $"{doneLabel}:", "EAX now contains exact equality.");
        if (expression.Operator.Kind is BoundBinaryOperatorKind.Inequality)
        {
            AppendMasmLine(source, "    xor eax, 1", "Invert equality for SMILE's <> comparison.");
        }
    }

    private static void AppendMasmLoadConditionOperand(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        string staticLabel,
        string pointerRegister,
        string lengthRegister)
    {
        if (expression is BoundVariableExpression variable)
        {
            int variableIndex = variableIndexes[variable.Variable];
            AppendMasmLine(
                source,
                $"    mov {pointerRegister}, QWORD PTR [{VariablePointerLabel(variableIndex)}]",
                $"Read current {variable.Variable.Name} storage for this IF.");
            AppendMasmLine(
                source,
                $"    mov {lengthRegister}, DWORD PTR [{VariableLengthLabel(variableIndex)}]",
                $"Read current {variable.Variable.Name} logical length.");
            return;
        }

        if (runtimeBuffers.TryGetValue(expression, out RuntimeStringBuffer? buffer))
        {
            AppendMasmLine(
                source,
                $"    lea {pointerRegister}, {buffer.Label}",
                "Address of the runtime-composed IF operand.");
            AppendMasmLine(
                source,
                $"    mov {lengthRegister}, DWORD PTR [{buffer.Label}Length]",
                "Length of the runtime-composed IF operand.");
            return;
        }

        AppendMasmLine(source, $"    lea {pointerRegister}, {staticLabel}", "Address of the static IF operand.");
        AppendMasmLine(source, $"    mov {lengthRegister}, {staticLabel}Length", "Length of the static IF operand.");
    }

    private static void AppendMasmPrint(
        StringBuilder source,
        BoundPrintStatement print,
        BoundStatementAnalysis facts,
        int printIndex,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        source.AppendLine();
        AppendMasmLine(source, $"; PRINT #{printIndex + 1}", "Write each expression segment, then newline.");

        if (!print.IsBlankLine && print.Value is BoundVariableExpression directVariable)
        {
            AppendMasmWriteVariable(
                source,
                directVariable.Variable.Name,
                variableIndexes[directVariable.Variable]);
        }
        else if (print.IsBlankLine || facts.Value.IsKnown)
        {
            AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, 0));
        }
        else
        {
            IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                switch (segments[segmentIndex])
                {
                    case RuntimeLiteralTextSegment { Text.Length: > 0 }:
                        AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, segmentIndex));
                        break;

                    case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                        AppendMasmWriteVariable(
                            source,
                            variable.Variable.Name,
                            variableIndexes[variable.Variable]);
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Integer:
                        AppendMasmIntegerExpression(source, runtime.Expression, variableIndexes);
                        AppendMasmLine(source, "    mov rcx, rax", "Format this runtime PRINT Integer.");
                        AppendMasmLine(
                            source,
                            $"    call {IntegerFormatProcedure}",
                            "Return decimal pointer and byte length.");
                        AppendMasmWriteBuffer(source, "rax", "edx", "runtime Integer text");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Boolean:
                        int booleanPartIndex = 0;
                        AppendMasmBooleanExpression(
                            source,
                            runtime.Expression,
                            variableIndexes,
                            labelPrefix + "Segment" +
                                segmentIndex.ToString(CultureInfo.InvariantCulture),
                            booleanStringBuffers,
                            ref booleanPartIndex);
                        string falseLabel = labelPrefix + "BooleanFalse" +
                            segmentIndex.ToString(CultureInfo.InvariantCulture);
                        string readyLabel = labelPrefix + "BooleanReady" +
                            segmentIndex.ToString(CultureInfo.InvariantCulture);
                        AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean PRINT text.");
                        AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
                        AppendMasmLine(source, "    lea rax, smileBooleanTrue", "Address of TRUE text.");
                        AppendMasmLine(source, "    mov edx, smileBooleanTrueLength", "Length of TRUE text.");
                        AppendMasmLine(source, $"    jmp {readyLabel}", "Skip the FALSE selection.");
                        AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
                        AppendMasmLine(source, "    lea rax, smileBooleanFalse", "Address of FALSE text.");
                        AppendMasmLine(source, "    mov edx, smileBooleanFalseLength", "Length of FALSE text.");
                        AppendMasmLine(source, $"{readyLabel}:", "Boolean PRINT text is ready.");
                        AppendMasmWriteBuffer(source, "rax", "edx", "runtime Boolean text");
                        break;
                }
            }
        }

        AppendMasmWriteLiteral(source, "newline");
    }

    private static bool CanEmitLivePrintSegments(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanEmitLivePrintSegments(binary.Left) && CanEmitLivePrintSegments(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart hole => CanEmitLivePrintSegments(hole.Expression),
                _ => false
            }),
            _ => false
        };

    private static void AppendMasmStorageUpdate(
        StringBuilder source,
        int variableIndex,
        VariableSymbol variable,
        SmileValue assignedValue,
        string valueLabel,
        string addressComment)
    {
        source.AppendLine();
        AppendMasmLine(source, $"    lea rax, {valueLabel}", addressComment);
        AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Store the runtime string pointer.");
        AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], {valueLabel}Length", "Store the runtime string length.");
        if (variable.Type is SmileType.Integer)
        {
            AppendMasmLine(
                source,
                $"    mov rax, {MasmIntegerImmediate(assignedValue.IntegerValue)}",
                "Materialize the signed Integer value for runtime comparisons.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(variableIndex)}], rax",
                "Update the runtime signed Integer storage.");
        }
        else if (variable.Type is SmileType.Boolean)
        {
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(variableIndex)}], {(assignedValue.BooleanValue ? 1 : 0)}",
                "Update the runtime Boolean storage.");
        }
    }

    private static void AppendMasmStorageCopy(
        StringBuilder source,
        int destinationIndex,
        int sourceIndex,
        SmileType variableType,
        string destinationName,
        string sourceName)
    {
        source.AppendLine();
        AppendMasmLine(
            source,
            $"    mov rax, QWORD PTR [{VariablePointerLabel(sourceIndex)}]",
            $"Read current {sourceName} pointer for SET {destinationName}.");
        AppendMasmLine(
            source,
            $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
            $"Store the copied pointer in {destinationName}.");
        AppendMasmLine(
            source,
            $"    mov eax, DWORD PTR [{VariableLengthLabel(sourceIndex)}]",
            $"Read current {sourceName} logical length.");
        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], eax",
            $"Store the copied logical length in {destinationName}.");
        if (variableType is SmileType.Integer)
        {
            AppendMasmLine(
                source,
                $"    mov rax, QWORD PTR [{VariableIntegerLabel(sourceIndex)}]",
                $"Read current {sourceName} signed Integer storage.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(destinationIndex)}], rax",
                $"Store the copied signed Integer in {destinationName}.");
        }
        else if (variableType is SmileType.Boolean)
        {
            AppendMasmLine(
                source,
                $"    mov al, BYTE PTR [{VariableBooleanLabel(sourceIndex)}]",
                $"Read current {sourceName} Boolean storage.");
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(destinationIndex)}], al",
                $"Store the copied Boolean in {destinationName}.");
        }
    }

    private static void AppendMasmRuntimeAssignment(
        StringBuilder source,
        VariableSymbol destination,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        int destinationIndex = variableIndexes[destination];
        if (destination.Type is SmileType.Integer)
        {
            AppendMasmIntegerExpression(source, expression, variableIndexes);
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(destinationIndex)}], rax",
                $"Update current signed Integer storage for {destination.Name}.");
        }
        else if (destination.Type is SmileType.Boolean)
        {
            int booleanPartIndex = 0;
            AppendMasmBooleanExpression(
                source,
                expression,
                variableIndexes,
                labelPrefix,
                booleanStringBuffers,
                ref booleanPartIndex);
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(destinationIndex)}], al",
                $"Update current Boolean storage for {destination.Name}.");
            string falseLabel = labelPrefix + "BooleanFalse";
            string doneLabel = labelPrefix + "BooleanDone";
            AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean display text.");
            AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
            AppendMasmLine(source, "    lea rax, smileBooleanTrue", "Address of canonical TRUE text.");
            AppendMasmLine(source, "    mov edx, smileBooleanTrueLength", "Length of TRUE text.");
            AppendMasmLine(source, $"    jmp {doneLabel}", "Skip the FALSE selection.");
            AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
            AppendMasmLine(source, "    lea rax, smileBooleanFalse", "Address of canonical FALSE text.");
            AppendMasmLine(source, "    mov edx, smileBooleanFalseLength", "Length of FALSE text.");
            AppendMasmLine(source, $"{doneLabel}:", "Boolean pointer and length are ready.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
                "Store the runtime Boolean text pointer.");
            AppendMasmLine(
                source,
                $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], edx",
                "Store the runtime Boolean text length.");
            return;
        }

        int partIndex = 0;
        AppendMasmRuntimeTextMaterialization(
            source,
            expression,
            buffer,
            variableIndexes,
            labelPrefix,
            booleanStringBuffers,
            ref partIndex);
        AppendMasmLine(
            source,
            $"    lea rax, {buffer.Label}",
            $"Address of runtime-composed {destination.Name} text.");
        AppendMasmLine(
            source,
            $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
            "Store the runtime text pointer.");
        AppendMasmLine(
            source,
            $"    mov eax, DWORD PTR [{buffer.Label}Length]",
            "Read the runtime text length.");
        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], eax",
            "Store the runtime text length.");
    }

    private static void AppendMasmBooleanExpression(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int partIndex)
    {
        switch (expression)
        {
            case BoundBooleanLiteralExpression literal:
                AppendMasmLine(
                    source,
                    $"    mov eax, {(literal.Value ? 1 : 0)}",
                    "Materialize this Boolean literal.");
                return;

            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                AppendMasmLine(
                    source,
                    $"    movzx eax, BYTE PTR [{VariableBooleanLabel(variableIndexes[variable.Variable])}]",
                    $"Read current Boolean storage for {variable.Variable.Name}.");
                return;

            case BoundUnaryExpression
                {
                    Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
                } unary:
                AppendMasmBooleanExpression(
                    source,
                    unary.Operand,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    xor eax, 1", "Invert the normalized Boolean result.");
                return;

            case BoundBinaryExpression logical when
                logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr:
                AppendMasmBooleanExpression(
                    source,
                    logical.Left,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                string shortCircuitLabel = labelPrefix + "BooleanPart" +
                    partIndex++.ToString(CultureInfo.InvariantCulture);
                AppendMasmLine(source, "    test eax, eax", "Honor left-to-right Boolean short circuit.");
                AppendMasmLine(
                    source,
                    logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                        ? $"    jz {shortCircuitLabel}"
                        : $"    jnz {shortCircuitLabel}",
                    "Skip the unreachable Boolean operand.");
                AppendMasmBooleanExpression(
                    source,
                    logical.Right,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, $"{shortCircuitLabel}:", "Complete this Boolean expression.");
                return;

            case BoundBinaryExpression integerComparison when
                CanEmitMasmDirectIntegerComparison(integerComparison):
                AppendMasmDirectIntegerComparison(source, integerComparison, variableIndexes);
                return;

            case BoundBinaryExpression stringComparison when
                stringComparison.Left.Type is SmileType.String &&
                stringComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality:
                RuntimeStringBuffer leftBuffer = booleanStringBuffers[stringComparison.Left];
                RuntimeStringBuffer rightBuffer = booleanStringBuffers[stringComparison.Right];
                AppendMasmRuntimeTextMaterialization(
                    source,
                    stringComparison.Left,
                    leftBuffer,
                    variableIndexes,
                    labelPrefix + "StringLeft",
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmRuntimeTextMaterialization(
                    source,
                    stringComparison.Right,
                    rightBuffer,
                    variableIndexes,
                    labelPrefix + "StringRight",
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmRuntimeBufferEquality(
                    source,
                    leftBuffer,
                    rightBuffer,
                    stringComparison.Operator.Kind is BoundBinaryOperatorKind.Inequality,
                    labelPrefix,
                    ref partIndex);
                return;

            case BoundBinaryExpression booleanComparison when
                booleanComparison.Left.Type is SmileType.Boolean &&
                booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality:
                AppendMasmBooleanExpression(
                    source,
                    booleanComparison.Left,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    push rax", "Preserve the left Boolean operand.");
                AppendMasmBooleanExpression(
                    source,
                    booleanComparison.Right,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    mov r9d, eax", "Keep the right Boolean operand.");
                AppendMasmLine(source, "    pop rax", "Restore the left Boolean operand.");
                AppendMasmLine(source, "    cmp eax, r9d", "Compare normalized Boolean values.");
                AppendMasmLine(
                    source,
                    booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality
                        ? "    sete al"
                        : "    setne al",
                    "Materialize Boolean equality.");
                AppendMasmLine(source, "    movzx eax, al", "Normalize the Boolean comparison.");
                return;
        }

        throw new InvalidOperationException(
            "MASM could not lower an abstract-unknown Boolean expression.");
    }

    private static void AppendMasmRuntimeTextMaterialization(
        StringBuilder source,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int partIndex)
    {
        source.AppendLine();
        AppendMasmLine(source, $"    lea r10, {buffer.Label}", "Start this runtime text buffer.");
        AppendMasmLine(source, "    xor r8d, r8d", "Current logical byte length is zero.");

        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    foreach (byte value in Encoding.UTF8.GetBytes(literal.Text))
                    {
                        AppendMasmLine(
                            source,
                            $"    mov BYTE PTR [r10], {value.ToString(CultureInfo.InvariantCulture)}",
                            "Append one compiler-known UTF-8 byte.");
                        AppendMasmLine(source, "    inc r10", "Advance the runtime text destination.");
                        AppendMasmLine(source, "    inc r8d", "Count the appended byte.");
                    }

                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                    int variableIndex = variableIndexes[variable.Variable];
                    AppendMasmLine(
                        source,
                        $"    mov r11, QWORD PTR [{VariablePointerLabel(variableIndex)}]",
                        $"Read current {variable.Variable.Name} text storage.");
                    AppendMasmLine(
                        source,
                        $"    mov ecx, DWORD PTR [{VariableLengthLabel(variableIndex)}]",
                        $"Read current {variable.Variable.Name} logical length.");
                    AppendMasmCopyBytes(
                        source,
                        labelPrefix,
                        ref partIndex);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Integer:
                    AppendMasmLine(source, "    push r10", "Preserve the runtime text destination.");
                    AppendMasmLine(source, "    push r8", "Preserve the accumulated text length.");
                    AppendMasmIntegerExpression(source, runtime.Expression, variableIndexes);
                    AppendMasmLine(source, "    mov rcx, rax", "Format this signed Integer value.");
                    AppendMasmLine(source, $"    call {IntegerFormatProcedure}", "Return decimal text in RAX/EDX.");
                    AppendMasmLine(source, "    mov r11, rax", "Use the formatted text as the copy source.");
                    AppendMasmLine(source, "    mov ecx, edx", "Use its exact decimal byte length.");
                    AppendMasmLine(source, "    pop r8", "Restore the accumulated text length.");
                    AppendMasmLine(source, "    pop r10", "Restore the runtime text destination.");
                    AppendMasmCopyBytes(
                        source,
                        labelPrefix,
                        ref partIndex);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Boolean:
                    AppendMasmLine(source, "    push r10", "Preserve the runtime text destination.");
                    AppendMasmLine(source, "    push r8", "Preserve the accumulated text length.");
                    AppendMasmBooleanExpression(
                        source,
                        runtime.Expression,
                        variableIndexes,
                        labelPrefix + "Boolean",
                        booleanStringBuffers,
                        ref partIndex);
                    string falseLabel = labelPrefix + "BooleanText" +
                        partIndex++.ToString(CultureInfo.InvariantCulture) + "False";
                    string readyLabel = labelPrefix + "BooleanText" +
                        partIndex++.ToString(CultureInfo.InvariantCulture) + "Ready";
                    AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean text.");
                    AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
                    AppendMasmLine(source, "    lea r11, smileBooleanTrue", "Address of TRUE text.");
                    AppendMasmLine(source, "    mov ecx, smileBooleanTrueLength", "Length of TRUE text.");
                    AppendMasmLine(source, $"    jmp {readyLabel}", "Skip the FALSE selection.");
                    AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
                    AppendMasmLine(source, "    lea r11, smileBooleanFalse", "Address of FALSE text.");
                    AppendMasmLine(source, "    mov ecx, smileBooleanFalseLength", "Length of FALSE text.");
                    AppendMasmLine(source, $"{readyLabel}:", "Boolean text source is ready.");
                    AppendMasmLine(source, "    pop r8", "Restore the accumulated text length.");
                    AppendMasmLine(source, "    pop r10", "Restore the runtime text destination.");
                    AppendMasmCopyBytes(source, labelPrefix, ref partIndex);
                    break;
            }
        }

        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{buffer.Label}Length], r8d",
            "Store the complete runtime text length.");
    }

    private static void AppendMasmRuntimeBufferEquality(
        StringBuilder source,
        RuntimeStringBuffer left,
        RuntimeStringBuffer right,
        bool invert,
        string labelPrefix,
        ref int partIndex)
    {
        string labelBase = labelPrefix + "StringCompare" +
            partIndex++.ToString(CultureInfo.InvariantCulture);
        string loopLabel = labelBase + "Loop";
        string differentLabel = labelBase + "Different";
        string doneLabel = labelBase + "Done";
        AppendMasmLine(source, $"    lea r10, {left.Label}", "Address of the left runtime String.");
        AppendMasmLine(source, $"    lea r11, {right.Label}", "Address of the right runtime String.");
        AppendMasmLine(source, $"    mov ecx, DWORD PTR [{left.Label}Length]", "Left logical byte length.");
        AppendMasmLine(source, $"    mov edx, DWORD PTR [{right.Label}Length]", "Right logical byte length.");
        AppendMasmLine(source, "    mov eax, 1", "Assume the complete String values are equal.");
        AppendMasmLine(source, "    cmp ecx, edx", "Exact Strings must have equal lengths.");
        AppendMasmLine(source, $"    jne {differentLabel}", "Different lengths cannot be equal.");
        AppendMasmLine(source, "    test ecx, ecx", "Empty Strings with equal lengths are equal.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes remain to compare.");
        AppendMasmLine(source, $"{loopLabel}:", "Compare the next exact UTF-8 byte.");
        AppendMasmLine(source, "    mov r8b, BYTE PTR [r10]", "Read the left byte.");
        AppendMasmLine(source, "    cmp r8b, BYTE PTR [r11]", "Compare the right byte.");
        AppendMasmLine(source, $"    jne {differentLabel}", "A differing byte makes the Strings unequal.");
        AppendMasmLine(source, "    inc r10", "Advance the left pointer.");
        AppendMasmLine(source, "    inc r11", "Advance the right pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down the shared byte length.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue through every byte.");
        AppendMasmLine(source, $"    jmp {doneLabel}", "The complete Strings are equal.");
        AppendMasmLine(source, $"{differentLabel}:", "Materialize String inequality.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means unequal.");
        AppendMasmLine(source, $"{doneLabel}:", "String equality is normalized in EAX.");
        if (invert)
        {
            AppendMasmLine(source, "    xor eax, 1", "Invert equality for SMILE inequality.");
        }
    }

    private static void AppendMasmCopyBytes(
        StringBuilder source,
        string labelPrefix,
        ref int partIndex)
    {
        string loopLabel = $"{labelPrefix}Copy{partIndex}Loop";
        string doneLabel = $"{labelPrefix}Copy{partIndex}Done";
        partIndex++;
        AppendMasmLine(source, "    mov edx, ecx", "Preserve this segment's logical length.");
        AppendMasmLine(source, "    test ecx, ecx", "Skip an exact empty segment.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes need copying.");
        AppendMasmLine(source, $"{loopLabel}:", "Copy current runtime text byte by byte.");
        AppendMasmLine(source, "    mov al, BYTE PTR [r11]", "Read the next source byte.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], al", "Append it to the destination buffer.");
        AppendMasmLine(source, "    inc r11", "Advance the source pointer.");
        AppendMasmLine(source, "    inc r10", "Advance the destination pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down this segment.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue until every byte is copied.");
        AppendMasmLine(source, "    add r8d, edx", "Add this segment to the complete length.");
        AppendMasmLine(source, $"{doneLabel}:", "Continue with the next runtime text segment.");
    }

    private static void AppendMasmIntegerFormatter(StringBuilder source)
    {
        AppendMasmLine(source, $"{IntegerFormatProcedure} PROC", "Format RCX as exact signed Int64 decimal text.");
        AppendMasmLine(source, $"    lea r10, {IntegerFormatBufferLabel} + 21", "Build digits backward from the buffer end.");
        AppendMasmLine(source, "    mov rax, rcx", "Copy the signed input value.");
        AppendMasmLine(source, "    xor r11d, r11d", "Remember whether a minus sign is required.");
        AppendMasmLine(source, "    test rax, rax", "Inspect the signed input.");
        AppendMasmLine(source, "    jge smileFormatIntegerMagnitude", "A nonnegative value is already a magnitude.");
        AppendMasmLine(source, "    mov r11d, 1", "Record the negative sign.");
        AppendMasmLine(source, "    neg rax", "Use the unsigned magnitude; Int64.MinValue remains representable as bits.");
        AppendMasmLine(source, "smileFormatIntegerMagnitude:", "Convert at least one decimal digit.");
        AppendMasmLine(source, "    mov r8d, 10", "Decimal divisor.");
        AppendMasmLine(source, "smileFormatIntegerDigit:", "Extract the next least-significant digit.");
        AppendMasmLine(source, "    xor edx, edx", "Clear the high unsigned dividend.");
        AppendMasmLine(source, "    div r8", "RAX becomes quotient; RDX is the digit.");
        AppendMasmLine(source, "    add dl, '0'", "Convert the digit to ASCII.");
        AppendMasmLine(source, "    dec r10", "Reserve one byte before the current text.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], dl", "Store this decimal digit.");
        AppendMasmLine(source, "    test rax, rax", "More quotient digits remain?");
        AppendMasmLine(source, "    jnz smileFormatIntegerDigit", "Continue until the quotient is zero.");
        AppendMasmLine(source, "    test r11d, r11d", "Does the value need a sign?");
        AppendMasmLine(source, "    jz smileFormatIntegerReady", "Positive and zero values are complete.");
        AppendMasmLine(source, "    dec r10", "Reserve the leading sign byte.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], '-'", "Prepend the minus sign.");
        AppendMasmLine(source, "smileFormatIntegerReady:", "Return pointer and exact length.");
        AppendMasmLine(source, $"    lea rax, {IntegerFormatBufferLabel} + 21", "Point one byte past the formatted text.");
        AppendMasmLine(source, "    sub rax, r10", "Compute the formatted byte length.");
        AppendMasmLine(source, "    mov edx, eax", "Return length in EDX.");
        AppendMasmLine(source, "    mov rax, r10", "Return text pointer in RAX.");
        AppendMasmLine(source, "    ret", "Return to generated code.");
        AppendMasmLine(source, $"{IntegerFormatProcedure} ENDP", "End signed Integer formatter.");
    }

    private static IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer>
        CreateMasmStatementBuffers(BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundStatement, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            BoundExpression? expression = statement switch
            {
                BoundLetStatement let when
                    !facts.Value.IsKnown && let.Initializer is not BoundVariableExpression =>
                    let.Initializer,
                BoundSetStatement set when
                    !facts.Value.IsKnown && set.Value is not BoundVariableExpression =>
                    set.Value,
                _ => null
            };
            if (expression is null)
            {
                continue;
            }

            string label = $"runtimeStatement{facts.Ordinal}Value";
            buffers.Add(
                statement,
                new RuntimeStringBuffer(
                    expression,
                    label,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(expression))));
        }

        return buffers;
    }

    private static IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>
        CreateMasmConditionBuffers(BoundProgramAnalysis analysis)
    {
        var plans = new Dictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundIfStatement conditional in analysis.EnumerateStatements().OfType<BoundIfStatement>())
        {
            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                int ordinal = analysis.GetClauseFacts(clause).Ordinal;
                var buffers = new List<RuntimeStringBuffer>();
                CollectMasmConditionBuffers(clause.Condition, ordinal, analysis, buffers);
                plans.Add(clause, buffers);
            }
        }

        return plans;
    }

    private static void CollectMasmConditionBuffers(
        BoundExpression expression,
        int clauseOrdinal,
        BoundProgramAnalysis analysis,
        List<RuntimeStringBuffer> buffers)
    {
        if (expression is BoundUnaryExpression unary)
        {
            CollectMasmConditionBuffers(unary.Operand, clauseOrdinal, analysis, buffers);
            return;
        }

        if (expression is not BoundBinaryExpression binary)
        {
            return;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            CollectMasmConditionBuffers(binary.Left, clauseOrdinal, analysis, buffers);
            CollectMasmConditionBuffers(binary.Right, clauseOrdinal, analysis, buffers);
            return;
        }

        if (binary.Left.Type is not SmileType.String ||
            binary.Operator.Kind is not (BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality))
        {
            return;
        }

        Add(binary.Left);
        Add(binary.Right);

        void Add(BoundExpression operand)
        {
            if (operand is BoundVariableExpression or BoundStringLiteralExpression)
            {
                return;
            }

            string label = $"ifCondition{clauseOrdinal}Runtime{buffers.Count}";
            buffers.Add(new RuntimeStringBuffer(
                operand,
                label,
                Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateMasmBooleanStringBuffers(BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    Collect(print.Value);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Collect(clause.Condition);
                    }

                    break;
            }
        }

        return buffers;

        void Collect(BoundExpression expression)
        {
            if (expression is BoundBinaryExpression comparison &&
                comparison.Left.Type is SmileType.String &&
                comparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(comparison.Left);
                Add(comparison.Right);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Collect(unary.Operand);
                    break;

                case BoundBinaryExpression binary:
                    Collect(binary.Left);
                    Collect(binary.Right);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Collect(hole.Expression);
                    }

                    break;
            }
        }

        void Add(BoundExpression operand)
        {
            if (buffers.ContainsKey(operand))
            {
                return;
            }

            string label = $"runtimeBooleanString{buffers.Count}";
            buffers.Add(
                operand,
                new RuntimeStringBuffer(
                    operand,
                    label,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static bool NeedsMasmIntegerFormatter(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        if (statementBuffers.Values
                .Concat(conditionBuffers.Values.SelectMany(value => value))
                .Concat(booleanStringBuffers.Values)
                .Any(buffer => RuntimeTextPlan.Flatten(buffer.Expression).Any(segment =>
                    segment is RuntimeExpressionTextSegment runtime &&
                    runtime.Expression.Type is SmileType.Integer &&
                    runtime.Expression is not BoundVariableExpression)))
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundPrintStatement print &&
            !analysis.GetStatementFacts(statement).Value.IsKnown &&
            RuntimeTextPlan.Flatten(print.Value).Any(segment =>
                segment is RuntimeExpressionTextSegment runtime &&
                runtime.Expression.Type is SmileType.Integer &&
                runtime.Expression is not BoundVariableExpression));
    }

    private static bool NeedsMasmBooleanText(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        if (statementBuffers.Values
                .Concat(conditionBuffers.Values.SelectMany(value => value))
                .Concat(booleanStringBuffers.Values)
                .Any(buffer => RuntimeTextPlan.Flatten(buffer.Expression).Any(segment =>
                    segment is RuntimeExpressionTextSegment runtime &&
                    runtime.Expression.Type is SmileType.Boolean)))
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundPrintStatement print &&
            !analysis.GetStatementFacts(statement).Value.IsKnown &&
            RuntimeTextPlan.Flatten(print.Value).Any(segment =>
                segment is RuntimeExpressionTextSegment runtime &&
                runtime.Expression.Type is SmileType.Boolean));
    }

    private static void AppendMasmWriteLiteral(StringBuilder source, string label)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    lea rdx, {label}", "WriteFile arg 2: address of literal bytes.");
        AppendMasmLine(source, $"    mov r8d, {label}Length", "WriteFile arg 3: byte count.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this literal segment.");
    }

    private static void AppendMasmWriteVariable(StringBuilder source, string name, int variableIndex)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    mov rdx, QWORD PTR [{VariablePointerLabel(variableIndex)}]", $"WriteFile arg 2: {name} pointer.");
        AppendMasmLine(source, $"    mov r8d, DWORD PTR [{VariableLengthLabel(variableIndex)}]", $"WriteFile arg 3: {name} length.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this variable segment.");
    }

    private static void AppendMasmWriteBuffer(
        StringBuilder source,
        string pointerRegister,
        string lengthRegister,
        string description)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    mov r8d, {lengthRegister}", $"WriteFile arg 3: {description} length.");
        AppendMasmLine(source, $"    mov rdx, {pointerRegister}", $"WriteFile arg 2: {description} pointer.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this runtime segment.");
    }

    private static void AppendMasmLine(StringBuilder source, string code, string? comment = null)
    {
        if (comment is null)
        {
            source.AppendLine(code);
            return;
        }

        const int commentColumn = 48;
        int padding = Math.Max(1, commentColumn - code.Length);
        source.AppendLine(code + new string(' ', padding) + "; " + comment);
    }

    private static string VariableValueLabel(int index) => $"variable{index}Value";

    private static string VariablePointerLabel(int index) => $"variable{index}Ptr";

    private static string VariableLengthLabel(int index) => $"variable{index}Length";

    private static string VariableIntegerLabel(int index) => $"variable{index}Integer";

    private static string VariableBooleanLabel(int index) => $"variable{index}Boolean";

    private static string MasmIntegerImmediate(long value) =>
        "0" + unchecked((ulong)value).ToString("X16", CultureInfo.InvariantCulture) + "h";

    private static string SetValueLabel(int statementIndex) => $"set{statementIndex}Value";

    private static string PrintLiteralLabel(int printIndex, int segmentIndex) =>
        $"print{printIndex}Segment{segmentIndex}";

    private static string IfClauseLabel(int ifOrdinal, int clauseIndex) =>
        $"if{ifOrdinal}Clause{clauseIndex}";

    private static string IfElseLabel(int ifOrdinal) => $"if{ifOrdinal}Else";

    private static string IfEndLabel(int ifOrdinal) => $"if{ifOrdinal}End";

    private static string MasmConditionOperandLabel(
        int clauseOrdinal,
        int comparisonIndex,
        string side) =>
        $"ifCondition{clauseOrdinal}Comparison{comparisonIndex}{side}";

    private static string MasmConditionPartLabel(
        int clauseOrdinal,
        int partIndex,
        string purpose) =>
        $"ifCondition{clauseOrdinal}Part{partIndex}{purpose}";

}

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();

        AppendStatements(source, program.Statements, string.Empty, identifiers, integers);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.js", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"{indent}let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers, integers)};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.JavaScript(set.Value, identifiers, integers)};");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "console.log();"
                        : $"console.log({TargetExpression.JavaScriptDisplay(print.Value, identifiers, integers)});");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, integers);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.JavaScript(clause.Condition, identifiers, integers))
                .AppendLine(") {");
            AppendStatements(source, clause.Statements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendStatements(source, conditional.ElseStatements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }
    }
}

internal sealed class JavaCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Java;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();
        source.AppendLine("public final class Program");
        source.AppendLine("{");
        source.AppendLine("    public static void main(String[] args)");
        source.AppendLine("    {");

        AppendStatements(source, program.Statements, "        ", identifiers, integers);

        source.AppendLine("    }");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.java", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.Java(let.Initializer, identifiers, integers);
                    source.AppendLine($"{indent}{TargetTypes.Java(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.Java(set.Value, identifiers, integers)};");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "System.out.println();"
                        : $"System.out.println({TargetExpression.JavaDisplay(print.Value, identifiers, integers)});");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, integers);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.Java(clause.Condition, identifiers, integers))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(source, clause.Statements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(source, conditional.ElseStatements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }
    }
}

internal sealed class CobolCodeGenerator : ICodeGenerator
{
    private const string RuntimePointerName = "SMILE-RUNTIME-POINTER";
    private const string RuntimeIntegerName = "SMILE-RUNTIME-INTEGER";
    private const string RuntimeIntegerTextName = "SMILE-RUNTIME-INTEGER-TEXT";
    private const string RuntimeConditionName = "SMILE-RUNTIME-CONDITION";

    private sealed record RuntimeStringBuffer(
        BoundExpression Expression,
        string ValueName,
        string LengthName,
        int Capacity);

    public TargetLanguage Language => TargetLanguage.Cobol;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        var source = new StringBuilder();
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths =
            CreateLogicalLengthNames(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, int> storageLengths =
            CreateStorageLengths(program, analysis);
        BoundConditionalClause[] clauses = analysis.EnumerateStatements()
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .ToArray();
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers =
            CreateRuntimeStringBuffers(program, analysis);
        bool needsRuntimeFacilities = NeedsRuntimeFacilities(analysis, runtimeStringBuffers);

        source.AppendLine(">>SOURCE FORMAT IS FREE");
        source.AppendLine("IDENTIFICATION DIVISION.");
        source.AppendLine("PROGRAM-ID. Program.");

        if (lets.Length > 0 || clauses.Length > 0)
        {
            source.AppendLine();
            source.AppendLine("DATA DIVISION.");
            source.AppendLine("WORKING-STORAGE SECTION.");
            source.AppendLine("*> SMILE LET values are stored before PROCEDURE DIVISION.");

            foreach (BoundLetStatement let in lets)
            {
                BoundStatementAnalysis facts = analysis.GetStatementFacts(let);
                AppendCobolLet(
                    source,
                    let,
                    facts.Value,
                    identifiers,
                    storageLengths,
                    logicalLengths);
            }

            foreach (BoundConditionalClause clause in clauses)
            {
                source.Append("01 ")
                    .Append(ConditionName(analysis.GetClauseFacts(clause).Ordinal))
                    .AppendLine(" PIC 9 COMP-5 VALUE 0.");
            }

            foreach (RuntimeStringBuffer buffer in runtimeStringBuffers.Values)
            {
                string picture = buffer.Capacity == 1
                    ? "PIC X"
                    : $"PIC X({buffer.Capacity})";
                source.Append("01 ").Append(buffer.ValueName).Append(' ')
                    .Append(picture).AppendLine(" VALUE SPACES.");
                source.Append("01 ").Append(buffer.LengthName)
                    .AppendLine(" PIC 9(9) COMP-5 VALUE 0.");
            }

            if (needsRuntimeFacilities)
            {
                source.Append("01 ").Append(RuntimePointerName)
                    .AppendLine(" PIC 9(9) COMP-5 VALUE 1.");
                source.Append("01 ").Append(RuntimeIntegerName)
                    .AppendLine(" PIC S9(18) COMP-5 VALUE 0.");
                source.Append("01 ").Append(RuntimeIntegerTextName)
                    .AppendLine(" PIC -(19)9 VALUE ZERO.");
                source.Append("01 ").Append(RuntimeConditionName)
                    .AppendLine(" PIC 9 COMP-5 VALUE 0.");
            }
        }

        source.AppendLine();
        source.AppendLine("PROCEDURE DIVISION.");
        source.AppendLine("*> SMILE PRINT reads current storage when it directly names a variable.");
        AppendStatements(
            source,
            program.Statements,
            "    ",
            analysis,
            identifiers,
            logicalLengths,
            storageLengths,
            runtimeStringBuffers,
            insideConditional: false);

        source.AppendLine("    STOP RUN.");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.cob", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendCobolLet(
        StringBuilder source,
        BoundLetStatement let,
        AnalyzedValue analyzedValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        string name = identifiers.Get(let.Variable);
        string text = analyzedValue.IsKnown
            ? analyzedValue.Value.ToDisplayText()
            : string.Empty;
        int storageLength = Math.Max(1, storageLengths[let.Variable]);
        string picture = storageLength == 1 ? "PIC X" : $"PIC X({storageLength})";
        string storageValue = text.Length == 0
            ? storageLength == 1 ? "SPACE" : "SPACES"
            : TargetEscapes.CobolString(text);
        source.AppendLine($"01 {name} {picture} VALUE {storageValue}.");

        if (logicalLengths.TryGetValue(let.Variable, out string? lengthName))
        {
            source.AppendLine(
                $"01 {lengthName} PIC 9(9) COMP-5 VALUE {TargetEscapes.CobolByteLength(text)}.");
        }
    }

    private static void AppendCobolSet(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        bool valueIsKnown,
        BoundSetStatement set,
        SmileValue knownValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        string terminator = terminateSentence ? "." : string.Empty;
        if (!valueIsKnown &&
            set.Value is BoundVariableExpression directSource &&
            !ReferenceEquals(set.Variable, directSource.Variable))
        {
            string sourceName = identifiers.Get(directSource.Variable);
            source.AppendLine(
                $"{indent}MOVE {sourceName} TO {identifiers.Get(set.Variable)}{terminator}");
            string sourceLength = logicalLengths[directSource.Variable];
            source.AppendLine(
                $"{indent}MOVE {sourceLength} TO {logicalLengths[set.Variable]}{terminator}");
            return;
        }

        if (!valueIsKnown)
        {
            AppendCobolRuntimeAssignment(
                source,
                indent,
                terminator,
                set.Variable,
                set.Value,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers);
            return;
        }

        string text = knownValue.ToDisplayText();
        string storageValue = text.Length == 0 ? "SPACES" : TargetEscapes.CobolString(text);
        source.AppendLine($"{indent}MOVE {storageValue} TO {identifiers.Get(set.Variable)}{terminator}");
        source.AppendLine(
            $"{indent}MOVE {TargetEscapes.CobolByteLength(text)} TO {logicalLengths[set.Variable]}{terminator}");
    }

    private static void AppendCobolRuntimeAssignment(
        StringBuilder source,
        string indent,
        string terminator,
        VariableSymbol destination,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        switch (destination.Type)
        {
            case SmileType.String:
                RuntimeStringBuffer stringBuffer = runtimeStringBuffers[expression];
                AppendCobolRuntimeStringMaterialization(
                    source,
                    indent,
                    terminator,
                    stringBuffer.ValueName,
                    stringBuffer.LengthName,
                    expression,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    RuntimeConditionName);
                source.Append(indent).Append("MOVE ").Append(stringBuffer.ValueName)
                    .Append(" TO ").Append(identifiers.Get(destination)).AppendLine(terminator);
                source.Append(indent).Append("MOVE ").Append(stringBuffer.LengthName)
                    .Append(" TO ").Append(logicalLengths[destination]).AppendLine(terminator);
                return;

            case SmileType.Integer when TryRenderCobolIntegerExpression(
                expression,
                identifiers,
                out string integer,
                out _):
                source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                    .Append(" = ").Append(integer).AppendLine(terminator);
                source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                    .Append(" TO ").Append(RuntimeIntegerTextName).AppendLine(terminator);
                source.Append(indent).Append("MOVE FUNCTION TRIM(")
                    .Append(RuntimeIntegerTextName).Append(") TO ")
                    .Append(identifiers.Get(destination)).AppendLine(terminator);
                source.Append(indent).Append("MOVE FUNCTION LENGTH(FUNCTION TRIM(")
                    .Append(RuntimeIntegerTextName).Append(")) TO ")
                    .Append(logicalLengths[destination]).AppendLine(terminator);
                return;

            case SmileType.Boolean:
                AppendCobolConditionEvaluation(
                    source,
                    indent,
                    RuntimeConditionName,
                    expression,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers);
                source.Append(indent).Append("IF ").Append(RuntimeConditionName).AppendLine(" = 1");
                AppendCobolFixedAssignment(
                    source,
                    indent + "    ",
                    destination,
                    "TRUE",
                    identifiers,
                    logicalLengths);
                source.Append(indent).AppendLine("ELSE");
                AppendCobolFixedAssignment(
                    source,
                    indent + "    ",
                    destination,
                    "FALSE",
                    identifiers,
                    logicalLengths);
                source.Append(indent).Append("END-IF").AppendLine(terminator);
                return;
        }

        throw new InvalidOperationException(
            $"COBOL cannot lower runtime {destination.Type} assignment expression.");
    }

    private static void AppendCobolFixedAssignment(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        string text,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        source.Append(indent).Append("MOVE ").Append(TargetEscapes.CobolString(text))
            .Append(" TO ").AppendLine(identifiers.Get(destination));
        source.Append(indent).Append("MOVE ").Append(Encoding.UTF8.GetByteCount(text))
            .Append(" TO ").AppendLine(logicalLengths[destination]);
    }

    private static void AppendCobolRuntimeStringMaterialization(
        StringBuilder source,
        string indent,
        string terminator,
        string destinationName,
        string destinationLength,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        string conditionName)
    {
        source.Append(indent).Append("MOVE SPACES TO ").Append(destinationName)
            .AppendLine(terminator);
        source.Append(indent).Append("MOVE 1 TO ").Append(RuntimePointerName)
            .AppendLine(terminator);

        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal when literal.Text.Length > 0:
                    AppendCobolStringInto(
                        source,
                        indent,
                        TargetEscapes.CobolString(literal.Text),
                        destinationName);
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string variableName = identifiers.Get(variable.Variable);
                    string variableLength = logicalLengths[variable.Variable];
                    source.Append(indent).Append("IF ").Append(variableLength).AppendLine(" > 0");
                    string variableSlice = $"{variableName}(1:{variableLength})";
                    AppendCobolStringInto(
                        source,
                        indent + "    ",
                        variableSlice,
                        destinationName);
                    source.Append(indent).AppendLine("END-IF");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Integer &&
                    TryRenderCobolIntegerExpression(
                        typed.Expression,
                        identifiers,
                        out string integer,
                        out _):
                    source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                        .Append(" = ").AppendLine(integer);
                    source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                        .Append(" TO ").AppendLine(RuntimeIntegerTextName);
                    AppendCobolStringInto(
                        source,
                        indent,
                        $"FUNCTION TRIM({RuntimeIntegerTextName})",
                        destinationName);
                    break;

                case RuntimeExpressionTextSegment typed when typed.Expression.Type is SmileType.Boolean:
                    // A nested Boolean comparison can materialize its own
                    // String operands and therefore reuse the shared STRING
                    // pointer. This buffer's length field is not final until
                    // the end, so it safely preserves the outer cursor here.
                    source.Append(indent).Append("MOVE ").Append(RuntimePointerName)
                        .Append(" TO ").AppendLine(destinationLength);
                    AppendCobolConditionEvaluation(
                        source,
                        indent,
                        conditionName,
                        typed.Expression,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    source.Append(indent).Append("MOVE ").Append(destinationLength)
                        .Append(" TO ").AppendLine(RuntimePointerName);
                    source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
                    AppendCobolStringInto(source, indent + "    ", "\"TRUE\"", destinationName);
                    source.Append(indent).AppendLine("ELSE");
                    AppendCobolStringInto(source, indent + "    ", "\"FALSE\"", destinationName);
                    source.Append(indent).AppendLine("END-IF");
                    break;
            }
        }

        source.Append(indent).Append("COMPUTE ").Append(destinationLength)
            .Append(" = ").Append(RuntimePointerName).Append(" - 1")
            .AppendLine(terminator);
    }

    private static void AppendCobolStringInto(
        StringBuilder source,
        string indent,
        string value,
        string destinationName)
    {
        source.Append(indent).Append("STRING ").Append(value)
            .Append(" DELIMITED BY SIZE INTO ").Append(destinationName)
            .Append(" WITH POINTER ").Append(RuntimePointerName)
            .AppendLine(" END-STRING");
    }

    private static void AppendCobolPrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        bool valueIsKnown,
        BoundPrintStatement print,
        SmileValue knownValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        if (!print.IsBlankLine && print.Value is BoundVariableExpression directVariable)
        {
            AppendCobolDirectVariablePrint(
                source,
                indent,
                terminateSentence,
                directVariable.Variable,
                identifiers,
                logicalLengths,
                storageLengths);
            return;
        }

        if (!print.IsBlankLine && !valueIsKnown)
        {
            AppendCobolRuntimePrint(
                source,
                indent,
                terminateSentence,
                print.Value,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers);
            return;
        }

        string text = print.IsBlankLine
            ? string.Empty
            : knownValue.ToDisplayText();
        if (text.Length == 0)
        {
            // DISPLAY "" emits one space in GnuCOBOL. A no-advancing line-feed
            // emits exactly the blank line SMILE PRINT requires.
            source.Append(indent).Append("DISPLAY X\"0A\" WITH NO ADVANCING")
                .AppendLine(terminateSentence ? "." : string.Empty);
            return;
        }

        source.Append(indent).Append("DISPLAY ");
        source.Append(TargetEscapes.CobolString(text));
        source.AppendLine(terminateSentence ? "." : string.Empty);
    }

    private static void AppendCobolRuntimePrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal when literal.Text.Length > 0:
                    source.Append(indent).Append("DISPLAY ")
                        .Append(TargetEscapes.CobolString(literal.Text))
                        .AppendLine(" WITH NO ADVANCING");
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                    AppendCobolVariableSegment(
                        source,
                        indent,
                        variable.Variable,
                        identifiers,
                        logicalLengths,
                        storageLengths);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Integer &&
                    TryRenderCobolIntegerExpression(
                        runtime.Expression,
                        identifiers,
                        out string integer,
                        out _):
                    source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                        .Append(" = ").AppendLine(integer);
                    source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                        .Append(" TO ").AppendLine(RuntimeIntegerTextName);
                    source.Append(indent).Append("DISPLAY FUNCTION TRIM(")
                        .Append(RuntimeIntegerTextName).AppendLine(") WITH NO ADVANCING");
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Boolean:
                    AppendCobolConditionEvaluation(
                        source,
                        indent,
                        RuntimeConditionName,
                        runtime.Expression,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    source.Append(indent).Append("IF ").Append(RuntimeConditionName).AppendLine(" = 1");
                    source.Append(indent).AppendLine("    DISPLAY \"TRUE\" WITH NO ADVANCING");
                    source.Append(indent).AppendLine("ELSE");
                    source.Append(indent).AppendLine("    DISPLAY \"FALSE\" WITH NO ADVANCING");
                    source.Append(indent).AppendLine("END-IF");
                    break;
            }
        }

        source.Append(indent).Append("DISPLAY X\"0A\" WITH NO ADVANCING")
            .AppendLine(terminateSentence ? "." : string.Empty);
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        bool insideConditional)
    {
        foreach (BoundStatement statement in statements)
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let when !facts.Value.IsKnown:
                    AppendCobolRuntimeAssignment(
                        source,
                        indent,
                        insideConditional ? string.Empty : ".",
                        let.Variable,
                        let.Initializer,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    break;

                case BoundSetStatement set:
                    AppendCobolSet(
                        source,
                        indent,
                        terminateSentence: !insideConditional,
                        valueIsKnown: facts.Value.IsKnown,
                        set,
                        facts.Value.Value,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    break;

                case BoundPrintStatement print:
                    AppendCobolPrint(
                        source,
                        indent,
                        terminateSentence: !insideConditional,
                        valueIsKnown: facts.Value.IsKnown,
                        print,
                        facts.Value.Value,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    break;

                case BoundIfStatement conditional:
                    AppendCobolIf(
                        source,
                        conditional,
                        indent,
                        analysis,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        terminateSentence: !insideConditional);
                    break;
            }
        }
    }

    private static void AppendCobolIf(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        bool terminateSentence)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            string conditionName = ConditionName(clauseFacts.Ordinal);

            if (clauseIndex > 0)
            {
                source.Append(indent).AppendLine("ELSE");
                indent += "    ";
            }

            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                clause.Condition,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers);
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
            if (clause.Statements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
            else
            {
                AppendStatements(
                    source,
                    clause.Statements,
                    indent + "    ",
                    analysis,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    insideConditional: true);
            }
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("ELSE");
            if (conditional.ElseStatements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
            else
            {
                AppendStatements(
                    source,
                    conditional.ElseStatements,
                    indent + "    ",
                    analysis,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    insideConditional: true);
            }
        }

        for (int clauseIndex = conditional.Clauses.Count - 1; clauseIndex >= 0; clauseIndex--)
        {
            bool closesCompleteStatement = clauseIndex == 0 && terminateSentence;
            source.Append(indent).Append("END-IF")
                .AppendLine(closesCompleteStatement ? "." : string.Empty);
            if (clauseIndex > 0)
            {
                indent = indent[..^4];
            }
        }
    }

    private static void AppendCobolVariableSegment(
        StringBuilder source,
        string indent,
        VariableSymbol variable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths)
    {
        string name = identifiers.Get(variable);
        string lengthName = logicalLengths[variable];

        source.Append(indent).Append("IF ").Append(lengthName).AppendLine(" > 0");
        source.Append(indent).Append("    DISPLAY ").Append(name)
            .Append("(1:").Append(lengthName).AppendLine(") WITH NO ADVANCING");
        source.Append(indent).AppendLine("END-IF");
    }

    private static void AppendCobolDirectVariablePrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        VariableSymbol variable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths)
    {
        string terminator = terminateSentence ? "." : string.Empty;
        string name = identifiers.Get(variable);
        string lengthName = logicalLengths[variable];
        // Preserve the established exact empty-String path. Only the final
        // END-IF receives a period at top level; inside a SMILE IF, even
        // that period is suppressed so it cannot close the outer scope.
        source.Append(indent).Append("IF ").Append(lengthName).AppendLine(" = 0");
        source.Append(indent).AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING");
        source.Append(indent).AppendLine("ELSE");
        source.Append(indent).Append("    DISPLAY ").Append(name)
            .Append("(1:").Append(lengthName).AppendLine(") WITH NO ADVANCING");
        source.Append(indent).AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING");
        source.Append(indent).Append("END-IF").AppendLine(terminator);
    }

    private static bool ContainsLiveVariable(BoundExpression expression) =>
        CanEmitLiveSegments(expression) && expression switch
        {
            BoundVariableExpression => true,
            BoundBinaryExpression binary =>
                ContainsLiveVariable(binary.Left) || ContainsLiveVariable(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole && ContainsLiveVariable(hole.Expression)),
            _ => false
        };

    private static bool CanEmitLiveSegments(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanEmitLiveSegments(binary.Left) && CanEmitLiveSegments(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart hole => CanEmitLiveSegments(hole.Expression),
                _ => false
            }),
            _ => false
        };

    private readonly record struct CobolStringConditionOperand(
        string Value,
        string Length,
        bool ReadsStorage);

    private static void AppendCobolConditionEvaluation(
        StringBuilder source,
        string indent,
        string conditionName,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers)
    {
        if (expression is BoundUnaryExpression
            {
                Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
            } unary)
        {
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                unary.Operand,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers);
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
            source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("ELSE");
            source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        if (expression is BoundBinaryExpression booleanComparison &&
            booleanComparison.Left.Type is SmileType.Boolean &&
            booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality)
        {
            string leftScratch = runtimeBuffers[expression].LengthName;
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                booleanComparison.Left,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers);
            source.Append(indent).Append("MOVE ").Append(conditionName)
                .Append(" TO ").AppendLine(leftScratch);
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                booleanComparison.Right,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers);
            string comparisonOperator = booleanComparison.Operator.Kind is
                BoundBinaryOperatorKind.Equality ? " = " : " NOT = ";
            source.Append(indent).Append("IF ").Append(leftScratch)
                .Append(comparisonOperator).AppendLine(conditionName);
            source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("ELSE");
            source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        if (expression is BoundBinaryExpression logical &&
            logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
        {
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                logical.Left,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers);
            string test = logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                ? " = 1"
                : " = 0";
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(test);
            AppendCobolConditionEvaluation(
                source,
                indent + "    ",
                conditionName,
                logical.Right,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        foreach (RuntimeStringBuffer buffer in runtimeBuffers.Values.Where(buffer =>
                     buffer.Expression.Type is SmileType.String &&
                     ContainsExpression(expression, buffer.Expression)))
        {
            AppendCobolRuntimeStringMaterialization(
                source,
                indent,
                string.Empty,
                buffer.ValueName,
                buffer.LengthName,
                buffer.Expression,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                conditionName);
        }

        int runtimeBufferIndex = 0;
        if (!TryRenderCobolCondition(
                expression,
                identifiers,
                logicalLengths,
                runtimeBuffers,
                ref runtimeBufferIndex,
                out string rendered))
        {
            throw new InvalidOperationException(
                "COBOL could not render a planned runtime condition.");
        }

        source.Append(indent).Append("IF ").AppendLine(rendered);
        source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
        source.Append(indent).AppendLine("ELSE");
        source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
        source.Append(indent).AppendLine("END-IF");
    }

    private static bool ContainsExpression(
        BoundExpression root,
        BoundExpression candidate)
    {
        if (ReferenceEquals(root, candidate))
        {
            return true;
        }

        return root switch
        {
            BoundUnaryExpression unary => ContainsExpression(unary.Operand, candidate),
            BoundBinaryExpression binary =>
                ContainsExpression(binary.Left, candidate) ||
                ContainsExpression(binary.Right, candidate),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsExpression(hole.Expression, candidate)),
            _ => false
        };
    }

    private static bool TryRenderCobolCondition(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out string rendered)
    {
        switch (expression)
        {
            case BoundBooleanLiteralExpression literal:
                rendered = literal.Value ? "1 = 1" : "1 = 0";
                return true;

            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                rendered = identifiers.Get(variable.Variable) + " = \"TRUE\"";
                return true;

            case BoundUnaryExpression { Operator.Kind: BoundUnaryOperatorKind.LogicalNegation } unary
                when TryRenderCobolCondition(
                    unary.Operand,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out string operand):
                rendered = $"NOT ({operand})";
                return true;

            case BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr:
                if (TryRenderCobolCondition(
                        binary.Left,
                        identifiers,
                        logicalLengths,
                        runtimeBuffers,
                        ref runtimeBufferIndex,
                        out string left) &&
                    TryRenderCobolCondition(
                        binary.Right,
                        identifiers,
                        logicalLengths,
                        runtimeBuffers,
                        ref runtimeBufferIndex,
                        out string right))
                {
                    string logicalOperator = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                        ? "AND"
                        : "OR";
                    rendered = $"({left}) {logicalOperator} ({right})";
                    return true;
                }

                break;

            case BoundBinaryExpression binary when TryRenderCobolDirectComparison(
                binary,
                identifiers,
                logicalLengths,
                runtimeBuffers,
                ref runtimeBufferIndex,
                out rendered):
                return true;
        }

        rendered = string.Empty;
        return false;
    }

    private static bool TryRenderCobolDirectComparison(
        BoundBinaryExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out string rendered)
    {
        bool isEquality = expression.Operator.Kind is BoundBinaryOperatorKind.Equality;
        bool isInequality = expression.Operator.Kind is BoundBinaryOperatorKind.Inequality;
        if (expression.Left.Type is SmileType.String)
        {
            if ((!isEquality && !isInequality) ||
                !TryGetCobolStringConditionOperand(
                    expression.Left,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out CobolStringConditionOperand left) ||
                !TryGetCobolStringConditionOperand(
                    expression.Right,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out CobolStringConditionOperand right))
            {
                rendered = string.Empty;
                return false;
            }

            string equality;
            if (!left.ReadsStorage && left.Length == "0")
            {
                equality = $"{right.Length} = 0";
            }
            else if (!right.ReadsStorage && right.Length == "0")
            {
                equality = $"{left.Length} = 0";
            }
            else
            {
                equality = $"({left.Length} = {right.Length} AND {left.Value} = {right.Value})";
            }

            rendered = isEquality ? equality : $"NOT ({equality})";
            return true;
        }

        if (!TryGetCobolScalarConditionOperand(expression.Left, identifiers, out string scalarLeft, out _) ||
            !TryGetCobolScalarConditionOperand(expression.Right, identifiers, out string scalarRight, out _))
        {
            rendered = string.Empty;
            return false;
        }

        string comparisonOperator = expression.Operator.Kind switch
        {
            BoundBinaryOperatorKind.Equality => "=",
            BoundBinaryOperatorKind.Inequality => "NOT =",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            _ => string.Empty
        };
        if (comparisonOperator.Length == 0)
        {
            rendered = string.Empty;
            return false;
        }

        rendered = $"{scalarLeft} {comparisonOperator} {scalarRight}";
        return true;
    }

    private static bool TryGetCobolStringConditionOperand(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out CobolStringConditionOperand operand)
    {
        switch (expression)
        {
            case BoundVariableExpression variable when
                logicalLengths.TryGetValue(variable.Variable, out string? lengthName):
                operand = new CobolStringConditionOperand(
                    identifiers.Get(variable.Variable),
                    lengthName,
                    ReadsStorage: true);
                return true;

            case BoundStringLiteralExpression literal:
                operand = new CobolStringConditionOperand(
                    TargetEscapes.CobolString(literal.Value),
                    TargetEscapes.CobolByteLength(literal.Value).ToString(CultureInfo.InvariantCulture),
                    ReadsStorage: false);
                return true;

            default:
                if (runtimeBuffers.TryGetValue(expression, out RuntimeStringBuffer? buffer))
                {
                    runtimeBufferIndex++;
                    operand = new CobolStringConditionOperand(
                        buffer.ValueName,
                        buffer.LengthName,
                        ReadsStorage: true);
                    return true;
                }

                operand = default;
                return false;
        }
    }

    private static bool TryGetCobolScalarConditionOperand(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        out string operand,
        out bool readsStorage)
    {
        if (expression.Type is SmileType.Integer &&
            TryRenderCobolIntegerExpression(
                expression,
                identifiers,
                out operand,
                out readsStorage))
        {
            return true;
        }

        switch (expression)
        {
            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                operand = identifiers.Get(variable.Variable);
                readsStorage = true;
                return true;

            case BoundBooleanLiteralExpression boolean:
                operand = TargetEscapes.CobolString(boolean.Value ? "TRUE" : "FALSE");
                readsStorage = false;
                return true;

            default:
                operand = string.Empty;
                readsStorage = false;
                return false;
        }
    }

    private static bool TryRenderCobolIntegerExpression(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        out string rendered,
        out bool readsStorage)
    {
        switch (expression)
        {
            case BoundVariableExpression { Variable.Type: SmileType.Integer } variable:
                rendered = $"FUNCTION NUMVAL({identifiers.Get(variable.Variable)})";
                readsStorage = true;
                return true;

            case BoundIntegerLiteralExpression integer:
                rendered = integer.Value.ToString(CultureInfo.InvariantCulture);
                readsStorage = false;
                return true;

            case BoundUnaryExpression unary when
                unary.Operator.Kind is BoundUnaryOperatorKind.Identity or
                    BoundUnaryOperatorKind.Negation:
                if (!TryRenderCobolIntegerExpression(
                        unary.Operand,
                        identifiers,
                        out string unaryOperand,
                        out readsStorage))
                {
                    rendered = string.Empty;
                    return false;
                }

                rendered = unary.Operator.Kind is BoundUnaryOperatorKind.Negation
                    ? $"(-({unaryOperand}))"
                    : $"({unaryOperand})";
                return true;

            case BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division:
                if (!TryRenderCobolIntegerExpression(
                        binary.Left,
                        identifiers,
                        out string left,
                        out bool leftReadsStorage) ||
                    !TryRenderCobolIntegerExpression(
                        binary.Right,
                        identifiers,
                        out string right,
                        out bool rightReadsStorage))
                {
                    rendered = string.Empty;
                    readsStorage = false;
                    return false;
                }

                readsStorage = leftReadsStorage || rightReadsStorage;
                if (binary.Operator.Kind is BoundBinaryOperatorKind.Division)
                {
                    // INTEGER-PART truncates toward zero, matching SMILE's
                    // signed Integer division instead of COBOL decimal math.
                    rendered = $"FUNCTION INTEGER-PART(({left}) / ({right}))";
                    return true;
                }

                string arithmeticOperator = binary.Operator.Kind switch
                {
                    BoundBinaryOperatorKind.Addition => "+",
                    BoundBinaryOperatorKind.Subtraction => "-",
                    BoundBinaryOperatorKind.Multiplication => "*",
                    _ => throw new InvalidOperationException("Unsupported COBOL Integer operator.")
                };
                rendered = $"(({left}) {arithmeticOperator} ({right}))";
                return true;

            default:
                rendered = string.Empty;
                readsStorage = false;
                return false;
        }
    }

    private static IReadOnlyDictionary<VariableSymbol, string> CreateLogicalLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramAnalysis analysis)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            // COBOL stores every SMILE value in an alphanumeric field. A
            // logical length beside every field keeps direct LET/SET copies,
            // exact empty Strings, embedded NUL, and runtime-formatted scalar
            // values uniform after branch merges.
            string preferred = $"SMILE-SET-LENGTH-{index}";
            string name = preferred;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = preferred + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            names.Add(variable, name);
        }

        return names;
    }

    private static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateRuntimeStringBuffers(
            BoundProgram program,
            BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement { Variable.Type: SmileType.String } let when
                    !facts.Value.IsKnown:
                    Add(let.Initializer, $"SMILE-STATEMENT-{facts.Ordinal}-STRING");
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement { Variable.Type: SmileType.String } set when
                    !facts.Value.IsKnown:
                    Add(set.Value, $"SMILE-STATEMENT-{facts.Ordinal}-STRING");
                    Collect(set.Value);
                    break;

                case BoundLetStatement let:
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    Collect(print.Value);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Collect(clause.Condition);
                    }

                    break;
            }
        }

        return buffers;

        void Collect(BoundExpression expression)
        {
            if (expression is BoundBinaryExpression booleanComparison &&
                booleanComparison.Left.Type is SmileType.Boolean &&
                booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(booleanComparison);
            }

            if (expression is BoundBinaryExpression comparison &&
                comparison.Left.Type is SmileType.String &&
                comparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(comparison.Left);
                Add(comparison.Right);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Collect(unary.Operand);
                    break;

                case BoundBinaryExpression binary:
                    Collect(binary.Left);
                    Collect(binary.Right);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Collect(hole.Expression);
                    }

                    break;
            }
        }

        void Add(BoundExpression operand, string? preferredName = null)
        {
            if (buffers.ContainsKey(operand) ||
                (preferredName is null &&
                 operand is BoundVariableExpression or BoundStringLiteralExpression))
            {
                return;
            }

            string valueName = preferredName ?? $"SMILE-EXPRESSION-{buffers.Count}-STRING";
            buffers.Add(operand, new RuntimeStringBuffer(
                operand,
                valueName,
                valueName + "-LENGTH",
                Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static bool NeedsRuntimeFacilities(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        if (runtimeStringBuffers.Count > 0)
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundLetStatement or BoundSetStatement or BoundPrintStatement &&
            !analysis.GetStatementFacts(statement).Value.IsKnown);
    }

    private static IReadOnlyDictionary<VariableSymbol, int> CreateStorageLengths(
        BoundProgram program,
        BoundProgramAnalysis analysis)
    {
        bool hasConditionalControlFlow = analysis.EnumerateStatements()
            .Any(statement => statement is BoundIfStatement);

        return program.Variables.ToDictionary(
            variable => variable,
            variable =>
            {
                int assignedLength = analysis.MaximumAssignedUtf8ByteLength(variable);
                if (hasConditionalControlFlow && assignedLength <= 1)
                {
                    // GnuCOBOL warns when a one-byte display field uses
                    // variable-length reference modification. IF makes
                    // runtime value paths possible, so one spare byte keeps
                    // the established exact-length spelling warning-free.
                    return 2;
                }

                return assignedLength;
            });
    }

    private static IEnumerable<VariableSymbol> EnumerateConditionVariables(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundVariableExpression variable:
                yield return variable.Variable;
                break;

            case BoundUnaryExpression unary:
                foreach (VariableSymbol nested in EnumerateConditionVariables(unary.Operand))
                {
                    yield return nested;
                }

                break;

            case BoundBinaryExpression binary:
                foreach (VariableSymbol nested in EnumerateConditionVariables(binary.Left))
                {
                    yield return nested;
                }

                foreach (VariableSymbol nested in EnumerateConditionVariables(binary.Right))
                {
                    yield return nested;
                }

                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolationExpressionPart hole in
                    interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                {
                    foreach (VariableSymbol nested in EnumerateConditionVariables(hole.Expression))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static string ConditionName(int ordinal) => $"SMILE-IF-CONDITION-{ordinal}";
}

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers =
            CCodeGenerator.CreateRuntimeStringBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers =
            CCodeGenerator.CreateRuntimeExpressionBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CCodeGenerator.CreateExactStringLengthNames(
                program,
                identifiers,
                analysis,
                runtimeStringBuffers.Keys.Select(statement => statement switch
                {
                    BoundLetStatement let => let.Variable,
                    BoundSetStatement set => set.Variable,
                    _ => throw new InvalidOperationException("Unexpected Objective-C runtime String statement.")
                }));
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (exactStringLengths.Count > 0 ||
            CGenerationFacts.NeedsStringComparison(analysis))
        {
            source.AppendLine("#include <string.h>");
        }

        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        foreach (CCodeGenerator.RuntimeStringBuffer buffer in runtimeExpressionBuffers.Values)
        {
            source.Append("    static char ").Append(buffer.Name).Append('[')
                .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                .AppendLine("] = { 0 };");
            source.Append("    size_t ").Append(buffer.Name).AppendLine("Used = 0;");
        }

        if (runtimeExpressionBuffers.Count > 0)
        {
            source.AppendLine();
        }

        bool emittedDeclaration = runtimeExpressionBuffers.Count > 0;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;
        AppendStatements(
            source,
            program.Statements,
            "    ",
            analysis,
            identifiers,
            integers,
            exactStringLengths,
            runtimeStringBuffers,
            runtimeExpressionBuffers,
            ref emittedDeclaration,
            ref emittedExecutable,
            ref emittedBodyStatement);

        if (emittedBodyStatement)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.m", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        foreach (BoundStatement statement in statements)
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    // The Windows-local Objective-C toolchain uses Clang/MSYS2
                    // without Foundation. C-compatible console types keep this
                    // target easy to build while still compiling as Objective-C.
                    if (let.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        if (let.Initializer is BoundVariableExpression letDirectSource)
                        {
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {identifiers.Get(letDirectSource.Variable)};");
                            if (exactStringLengths.TryGetValue(let.Variable, out string? directLetLength))
                            {
                                string sourceLength = exactStringLengths.TryGetValue(
                                    letDirectSource.Variable,
                                    out string? exactSourceLength)
                                    ? exactSourceLength
                                    : $"strlen({identifiers.Get(letDirectSource.Variable)})";
                                source.AppendLine($"{indent}size_t {directLetLength} = {sourceLength};");
                            }
                        }
                        else
                        {
                            CCodeGenerator.RuntimeStringBuffer buffer = runtimeStringBuffers[let];
                            source.AppendLine(
                                $"{indent}static char {buffer.Name}[{buffer.Capacity + 1}] = {{ 0 }};");
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {buffer.Name};");
                            source.AppendLine(
                                $"{indent}size_t {exactStringLengths[let.Variable]} = 0;");
                            CCodeGenerator.AppendCRuntimeStringAssignment(
                                source,
                                indent,
                                let.Variable,
                                let.Initializer,
                                buffer,
                                identifiers,
                                integers,
                                exactStringLengths,
                                runtimeExpressionBuffers,
                                declareBuffer: false);
                        }
                    }
                    else
                    {
                        SmileValue letValue = let.Variable.Type is SmileType.String
                            ? facts.Value.Value
                            : default;
                        string initializer = let.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(letValue, integers)
                            : TargetExpression.ObjectiveC(
                                let.Initializer,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                        if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                        {
                            source.AppendLine($"{indent}size_t {letLengthName} = {CCodeGenerator.Utf8ByteLength(letValue)};");
                        }
                    }

                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    if (set.Variable.Type is SmileType.String &&
                        !facts.Value.IsKnown &&
                        set.Value is BoundVariableExpression directSource)
                    {
                        CCodeGenerator.AppendCDirectStringCopy(
                            source,
                            indent,
                            set.Variable,
                            directSource.Variable,
                            identifiers,
                            exactStringLengths);
                    }
                    else if (set.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        CCodeGenerator.AppendCRuntimeStringAssignment(
                            source,
                            indent,
                            set.Variable,
                            set.Value,
                            runtimeStringBuffers[set],
                            identifiers,
                            integers,
                            exactStringLengths,
                            runtimeExpressionBuffers,
                            declareBuffer: true);
                    }
                    else
                    {
                        SmileValue setValue = facts.Value.IsKnown
                            ? facts.Value.Value
                            : default;
                        string value = set.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(setValue, integers)
                            : TargetExpression.ObjectiveC(
                                set.Value,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {value};");
                        if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                        {
                            source.AppendLine($"{indent}{setLengthName} = {CCodeGenerator.Utf8ByteLength(setValue)};");
                        }
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendObjectiveCPrint(
                        source,
                        indent,
                        print,
                        identifiers,
                        integers,
                        facts.Value.IsKnown,
                        GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundIfStatement conditional:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        analysis,
                        identifiers,
                        integers,
                        exactStringLengths,
                        runtimeStringBuffers,
                        runtimeExpressionBuffers,
                        ref emittedDeclaration,
                        ref emittedExecutable,
                        ref emittedBodyStatement);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.ObjectiveC(
                    clause.Condition,
                    identifiers,
                    integers,
                    GeneratorConditionFacts.KnownValues(clauseFacts.ValuesBefore),
                    exactStringLengths,
                    runtimeExpressionBuffers))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendObjectiveCPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool valueIsKnown,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        if (CCodeGenerator.TryAppendDirectStringVariablePrint(
            source,
            indent,
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (!valueIsKnown && CCodeGenerator.TryAppendRuntimeStringSegments(
                source,
                indent,
                print,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers,
                TargetLanguage.ObjectiveC))
        {
            return;
        }

        if (valueIsKnown && CCodeGenerator.TryAppendExactNulStringPrint(source, indent, print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.ObjectiveC(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers),
            integers.RequiresSigned64Storage);
        CCodeGenerator.AppendPrintfCall(source, indent, plan);
    }
}

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();
        IReadOnlySet<VariableSymbol> mutatedVariables = BoundStatementTree.Enumerate(program)
            .OfType<BoundSetStatement>()
            .Select(set => set.Variable)
            .ToHashSet();
        bool needsConditionHelper = BoundStatementTree.Enumerate(program)
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .Any(clause => GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition));

        if (needsConditionHelper)
        {
            source.AppendLine("// Keep a valid source-constant IF as genuine control flow without warnings.");
            source.AppendLine("@inline(never)");
            source.AppendLine("func _smile_condition(_ value: Bool) -> Bool {");
            source.AppendLine("    value");
            source.AppendLine("}");
            source.AppendLine();
        }

        AppendStatements(
            source,
            program.Statements,
            string.Empty,
            identifiers,
            integers,
            mutatedVariables,
            needsConditionHelper);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.Swift(let.Initializer, identifiers, integers);
                    string declaration = mutatedVariables.Contains(let.Variable) ? "var" : "let";
                    source.AppendLine($"{indent}{declaration} {identifiers.Get(let.Variable)}: {TargetTypes.Swift(let.Variable.Type, integers)} = {initializer}");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = TargetExpression.Swift(set.Value, identifiers, integers);
                    if (set.Value is BoundVariableExpression variable &&
                        variable.Variable == set.Variable)
                    {
                        // Swift rejects a plain `value = value` as a compile-time
                        // error even though direct self-assignment is valid SMILE.
                        // Keep the required target storage update with the
                        // smallest type-preserving identity expression.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{indent}{name} = {value}");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({TargetExpression.SwiftDisplay(print.Value, identifiers, integers)})");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        mutatedVariables,
                        hasConditionHelper);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.Swift(clause.Condition, identifiers, integers);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if " : "else if ")
                .Append(condition)
                .AppendLine(" {");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }
    }
}

internal sealed class PythonCodeGenerator : ICodeGenerator
{
    private static readonly IReadOnlyDictionary<VariableSymbol, SmileValue> EmptyValues =
        new Dictionary<VariableSymbol, SmileValue>();

    public TargetLanguage Language => TargetLanguage.Python;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        var knownValues = new Dictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            knownValues[statement] = GeneratorConditionFacts.KnownValues(
                analysis.GetStatementFacts(statement).ValuesBefore);
        }

        var source = new StringBuilder();
        bool emittedHelper = false;

        if (PythonGenerationFacts.NeedsTextHelper(program))
        {
            source.AppendLine("def _smile_text(value: object) -> str:");
            source.AppendLine("    if isinstance(value, bool):");
            source.AppendLine("        return \"TRUE\" if value else \"FALSE\"");
            source.AppendLine();
            source.AppendLine("    return str(value)");
            emittedHelper = true;
        }

        if (PythonGenerationFacts.NeedsDivisionHelper(program))
        {
            if (emittedHelper)
            {
                source.AppendLine();
                source.AppendLine();
            }

            source.AppendLine("def _smile_div(left: int, right: int) -> int:");
            source.AppendLine("    quotient = abs(left) // abs(right)");
            source.AppendLine("    return -quotient if (left < 0) != (right < 0) else quotient");
            emittedHelper = true;
        }

        if (emittedHelper)
        {
            source.AppendLine();
            source.AppendLine();
        }

        source.AppendLine("def main() -> None:");
        if (program.Statements.Count == 0)
        {
            source.AppendLine("    pass");
        }
        else
        {
            AppendStatements(
                source,
                program.Statements,
                "    ",
                identifiers,
                knownValues);
        }

        source.AppendLine();
        source.AppendLine();
        source.AppendLine("if __name__ == \"__main__\":");
        source.AppendLine("    main()");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.py", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>> knownValues)
    {
        foreach (BoundStatement statement in statements)
        {
            IReadOnlyDictionary<VariableSymbol, SmileValue> values =
                knownValues.TryGetValue(statement, out var statementValues)
                    ? statementValues
                    : EmptyValues;
            var expressions = new PythonExpressionWriter(identifiers, values);

            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"{indent}{identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)}");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {expressions.Write(set.Value)}");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({expressions.WriteDisplay(print.Value)})");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, knownValues, expressions);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>> knownValues,
        PythonExpressionWriter expressions)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if " : "elif ")
                .Append(expressions.Write(clause.Condition))
                .AppendLine(":");
            if (clause.Statements.Count == 0)
            {
                source.Append(indent).AppendLine("    pass");
            }
            else
            {
                AppendStatements(source, clause.Statements, indent + "    ", identifiers, knownValues);
            }
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else:");
            if (conditional.ElseStatements.Count == 0)
            {
                source.Append(indent).AppendLine("    pass");
            }
            else
            {
                AppendStatements(
                    source,
                    conditional.ElseStatements,
                    indent + "    ",
                    identifiers,
                    knownValues);
            }
        }
    }

}

internal sealed class CppCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Cpp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var expressions = new CppExpressionWriter(identifiers, integers);
        var source = new StringBuilder();

        bool needsIostream = BoundStatementTree.Enumerate(program)
            .Any(statement => statement is BoundPrintStatement);
        bool needsString = CppGenerationFacts.NeedsStringHeader(program);

        if (needsIostream)
        {
            source.AppendLine("#include <iostream>");
        }

        if (needsString)
        {
            source.AppendLine("#include <string>");
        }

        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <cstdint>");
        }

        if (needsIostream || needsString || integers.RequiresSigned64Storage)
        {
            source.AppendLine();
        }

        source.AppendLine("int main()");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        AppendStatements(
            source,
            program.Statements,
            "    ",
            expressions,
            identifiers,
            integers,
            ref emittedDeclaration,
            ref emittedExecutable);

        if (program.Statements.Count > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile(
                    "Program.cpp",
                    TextOutput.EnsureOneTrailingNewLine(source.ToString()),
                    IsPrimary: true)
            });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine(
                        $"{indent}{TargetTypes.Cpp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)};");
                    emittedDeclaration = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {expressions.Write(set.Value)};");
                    emittedExecutable = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendPrint(source, indent, print, expressions);
                    emittedExecutable = true;
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        expressions,
                        identifiers,
                        integers,
                        ref emittedDeclaration,
                        ref emittedExecutable);
                    emittedExecutable = true;
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(expressions.Write(clause.Condition))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                expressions,
                identifiers,
                integers,
                ref emittedDeclaration,
                ref emittedExecutable);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                expressions,
                identifiers,
                integers,
                ref emittedDeclaration,
                ref emittedExecutable);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        CppExpressionWriter expressions)
    {
        source.Append(indent).Append("std::cout");

        if (print.IsBlankLine)
        {
            source.AppendLine(" << '\\n';");
            return;
        }

        if (print.Value is BoundInterpolatedStringExpression interpolated)
        {
            bool emittedPart = false;
            foreach (BoundInterpolatedPart part in interpolated.Parts)
            {
                string? text = part switch
                {
                    BoundInterpolatedTextPart literal when literal.Text.Length > 0 =>
                        expressions.WriteStringLiteral(literal.Text),
                    BoundInterpolationExpressionPart hole => expressions.WriteForStream(hole.Expression),
                    _ => null
                };

                if (text is not null)
                {
                    source.Append(" << ");
                    source.Append(text);
                    emittedPart = true;
                }
            }

            if (!emittedPart)
            {
                source.Append(" << \"\"");
            }
        }
        else
        {
            source.Append(" << ");
            source.Append(expressions.WriteForStream(print.Value));
        }

        source.AppendLine(" << '\\n';");
    }
}

internal static class CppGenerationFacts
{
    public static bool NeedsStringHeader(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let =>
                let.Variable.Type is SmileType.String || ContainsStringFacility(let.Initializer),
            BoundSetStatement set => ContainsStringFacility(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                ContainsDirectStreamStringFacility(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsStringFacility(clause.Condition)),
            _ => false
        });

    private static bool ContainsDirectStreamStringFacility(BoundExpression expression) =>
        expression is BoundInterpolatedStringExpression interpolated
            ? interpolated.Parts.Any(part => part switch
            {
                BoundInterpolatedTextPart text => text.Text.Contains('\0', StringComparison.Ordinal),
                BoundInterpolationExpressionPart hole => ContainsStringFacility(hole.Expression),
                _ => false
            })
            : ContainsStringFacility(expression);

    private static bool ContainsStringFacility(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundUnaryExpression unary => ContainsStringFacility(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation ||
                (binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
                ContainsStringFacility(binary.Left) ||
                ContainsStringFacility(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };
}

internal sealed class CppExpressionWriter
{
    private readonly TargetIdentifierMap _identifiers;
    private readonly TargetIntegerProfile _integers;

    public CppExpressionWriter(
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        _identifiers = identifiers;
        _integers = integers;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteForStream(BoundExpression expression) =>
        expression.Type is SmileType.Boolean
            ? $"({Write(expression)} ? \"TRUE\" : \"FALSE\")"
            : Write(expression);

    public string WriteStringLiteral(string value) =>
        value.Contains('\0', StringComparison.Ordinal)
            ? $"std::string{{{TargetEscapes.CString(value)}, {Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)}}}"
            : TargetEscapes.CString(value);

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => WriteStringLiteral(literal.Value),
            BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
            BoundBooleanLiteralExpression literal => literal.Value ? "true" : "false",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => "std::string{}"
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        const int precedence = 7;
        string op = expression.Operator.Kind switch
        {
            BoundUnaryOperatorKind.Identity => "+",
            BoundUnaryOperatorKind.Negation => "-",
            BoundUnaryOperatorKind.LogicalNegation => "!",
            _ => string.Empty
        };
        string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
        string text = op + operand;
        return precedence < parentPrecedence ? "(" + text + ")" : text;
    }

    private string WriteBinary(
        BoundBinaryExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
        string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);

        if (expression.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation &&
            !ProducesOwnedString(expression.Left))
        {
            // Two C++ string literals cannot be added because both decay to
            // pointers. Starting the chain with an owned std::string keeps the
            // source natural while making every legal SMILE concatenation valid.
            left = "std::string{" + left + "}";
        }

        if (expression.Left.Type is SmileType.String &&
            expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality &&
            !ProducesOwnedString(expression.Left))
        {
            // std::string equality is length-aware, including embedded NUL.
            // Owning a literal left operand also avoids pointer comparison.
            left = "std::string{" + left + "}";
        }

        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;
        return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        var segments = new List<(string Text, bool IsOwned)>();

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart text when text.Text.Length > 0:
                    segments.Add((WriteStringLiteral(text.Text), text.Text.Contains('\0', StringComparison.Ordinal)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.String:
                    segments.Add((Write(hole.Expression), ProducesOwnedString(hole.Expression)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Integer:
                    segments.Add(($"std::to_string({Write(hole.Expression)})", true));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Boolean:
                    segments.Add(($"({Write(hole.Expression)} ? \"TRUE\" : \"FALSE\")", false));
                    break;
            }
        }

        if (segments.Count == 0)
        {
            return "std::string{}";
        }

        if (!segments[0].IsOwned)
        {
            segments[0] = ("std::string{" + segments[0].Text + "}", true);
        }

        return string.Join(" + ", segments.Select(segment => segment.Text));
    }

    private string IntegerLiteral(long value)
    {
        if (!_integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static bool ProducesOwnedString(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundBinaryExpression binary => binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation,
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Division => "/",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "&&",
            BoundBinaryOperatorKind.LogicalOr => "||",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
            BoundBinaryOperatorKind.Less or BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or BoundBinaryOperatorKind.GreaterOrEquals => 4,
            BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
            BoundBinaryOperatorKind.LogicalAnd => 2,
            BoundBinaryOperatorKind.LogicalOr => 1,
            _ => 0
        };

    private static bool NeedsParentheses(
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
        {
            return true;
        }

        return isRightChild &&
            precedence == parentPrecedence &&
            parentOperator is not (
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.StringConcatenation or
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr);
    }
}

internal static class PythonGenerationFacts
{
    public static bool NeedsTextHelper(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => ContainsTextConversion(let.Initializer),
            BoundSetStatement set => ContainsTextConversion(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                print.Value.Type is not SmileType.String || ContainsTextConversion(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsTextConversion(clause.Condition)),
            _ => false
        });

    public static bool NeedsDivisionHelper(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => ContainsDivision(let.Initializer),
            BoundSetStatement set => ContainsDivision(set.Value),
            BoundPrintStatement print when !print.IsBlankLine => ContainsDivision(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsDivision(clause.Condition)),
            _ => false
        });

    private static bool ContainsTextConversion(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsTextConversion(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsTextConversion(binary.Left) || ContainsTextConversion(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                (hole.Expression.Type is not SmileType.String || ContainsTextConversion(hole.Expression))),
            _ => false
        };

    private static bool ContainsDivision(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsDivision(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.Division ||
                ContainsDivision(binary.Left) ||
                ContainsDivision(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole && ContainsDivision(hole.Expression)),
            _ => false
        };
}

internal sealed class PythonExpressionWriter
{
    private const int OrPrecedence = 1;
    private const int AndPrecedence = 2;
    private const int NotPrecedence = 3;
    private const int ComparisonPrecedence = 4;
    private const int AdditionPrecedence = 5;
    private const int MultiplicationPrecedence = 6;
    private const int UnaryPrecedence = 7;
    private const int CallPrecedence = 8;

    private readonly TargetIdentifierMap _identifiers;
    private readonly IReadOnlyDictionary<VariableSymbol, SmileValue> _values;

    public PythonExpressionWriter(
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        _identifiers = identifiers;
        _values = values;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteDisplay(BoundExpression expression) =>
        expression.Type is SmileType.String
            ? Write(expression)
            : $"_smile_text({Write(expression)})";

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.PythonString(literal.Value),
            BoundIntegerLiteralExpression literal => literal.Value.ToString(CultureInfo.InvariantCulture),
            BoundBooleanLiteralExpression literal => literal.Value ? "True" : "False",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => TargetEscapes.PythonString(string.Empty)
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        if (expression.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation)
        {
            string operand = expression.Operand is BoundBinaryExpression
                ? "(" + WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) + ")"
                : WriteExpression(expression.Operand, NotPrecedence, isRightChild: true, parentOperator: null);
            string logicalText = "not " + operand;
            return NotPrecedence < parentPrecedence ? "(" + logicalText + ")" : logicalText;
        }

        string op = expression.Operator.Kind is BoundUnaryOperatorKind.Negation ? "-" : "+";
        string value = WriteExpression(
            expression.Operand,
            UnaryPrecedence,
            isRightChild: true,
            parentOperator: null);
        string text = op + value;
        return UnaryPrecedence < parentPrecedence ? "(" + text + ")" : text;
    }

    private string WriteBinary(
        BoundBinaryExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (expression.Operator.Kind is BoundBinaryOperatorKind.Division)
        {
            string call =
                "_smile_div(" +
                WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) +
                ", " +
                WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) +
                ")";
            return CallPrecedence < parentPrecedence ? "(" + call + ")" : call;
        }

        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(
            expression.Left,
            precedence,
            isRightChild: false,
            parentOperator: expression.Operator.Kind);
        string right = WriteExpression(
            expression.Right,
            precedence,
            isRightChild: true,
            parentOperator: expression.Operator.Kind);
        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

        return NeedsParentheses(
            expression.Operator.Kind,
            precedence,
            parentPrecedence,
            isRightChild,
            parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        if (!expression.Parts.Any(part => part is BoundInterpolationExpressionPart))
        {
            string literalText = string.Concat(
                expression.Parts.OfType<BoundInterpolatedTextPart>().Select(part => part.Text));
            return TargetEscapes.PythonString(literalText);
        }

        if (expression.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsStringLiteral(hole.Expression) &&
                !TryGetDisplayText(hole.Expression, out _)))
        {
            // Python 3.10 cannot parse a backslash or a same-quote String
            // literal inside an f-string hole. If branch-aware facts cannot
            // prove that hole, keep it live by lowering the complete template
            // to ordinary String concatenation instead of folding today's
            // selected branch into one unrelated literal.
            string[] runtimeSegments = expression.Parts
                .Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.PythonString(text.Text),
                    BoundInterpolationExpressionPart hole => WriteDisplay(hole.Expression),
                    _ => TargetEscapes.PythonString(string.Empty)
                })
                .Where(segment => segment != TargetEscapes.PythonString(string.Empty))
                .ToArray();
            return runtimeSegments.Length == 0
                ? TargetEscapes.PythonString(string.Empty)
                : string.Join(" + ", runtimeSegments);
        }

        var fStringText = new StringBuilder();
        bool emittedExpressionHole = false;

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart literal:
                    fStringText.Append(TargetEscapes.PythonFStringText(literal.Text));
                    break;

                case BoundInterpolationExpressionPart hole when
                    ContainsStringLiteral(hole.Expression) &&
                    TryGetDisplayText(hole.Expression, out string foldedText):
                    // Python 3.10 rejects backslashes and same-quote literals
                    // inside f-string expressions. Current SMILE holes are
                    // compile-time constants, so fold only that unsafe hole
                    // into f-string text while preserving all safe holes.
                    fStringText.Append(TargetEscapes.PythonFStringText(foldedText));
                    break;

                case BoundInterpolationExpressionPart hole:
                    fStringText.Append('{').Append(WriteDisplay(hole.Expression)).Append('}');
                    emittedExpressionHole = true;
                    break;
            }
        }

        if (!emittedExpressionHole)
        {
            if (BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value))
            {
                return TargetEscapes.PythonString(value.ToDisplayText());
            }

            // Every folded hole above was individually proved from the same
            // abstract-known environment, so reaching this defensive path is
            // only possible for a future bound node. Preserve a live f-string
            // rather than consulting the selected concrete execution trace.
            return "f\"" + fStringText + "\"";
        }

        return "f\"" + fStringText + "\"";
    }

    private bool TryGetDisplayText(BoundExpression expression, out string text)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value))
        {
            text = value.ToDisplayText();
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool ContainsStringLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundUnaryExpression unary => ContainsStringLiteral(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsStringLiteral(binary.Left) || ContainsStringLiteral(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "and",
            BoundBinaryOperatorKind.LogicalOr => "or",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication => MultiplicationPrecedence,
            BoundBinaryOperatorKind.Addition or
            BoundBinaryOperatorKind.Subtraction or
            BoundBinaryOperatorKind.StringConcatenation => AdditionPrecedence,
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals or
            BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality => ComparisonPrecedence,
            BoundBinaryOperatorKind.LogicalAnd => AndPrecedence,
            BoundBinaryOperatorKind.LogicalOr => OrPrecedence,
            _ => 0
        };

    private static bool NeedsParentheses(
        BoundBinaryOperatorKind currentOperator,
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
        {
            return true;
        }

        // Python chains adjacent comparisons. Parenthesize either child so a
        // nested SMILE equality tree never becomes Python's chained syntax.
        if (parentOperator.HasValue &&
            IsComparison(currentOperator) &&
            IsComparison(parentOperator.Value))
        {
            return true;
        }

        return isRightChild &&
            precedence == parentPrecedence &&
            parentOperator is not (
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.StringConcatenation or
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr);
    }

    private static bool IsComparison(BoundBinaryOperatorKind kind) =>
        kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality or
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals;
}

internal sealed record CPrintfPlan(
    string FormatText,
    IReadOnlyList<string> Arguments)
{
    public static CPrintfPlan FromPrint(
        BoundPrintStatement print,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        var format = new StringBuilder();
        var arguments = new List<string>();

        // FormatText is raw printf format text, not C source text. Literal
        // percent signs are doubled here for printf safety; C string escaping
        // happens later exactly once when the call is emitted.
        if (!print.IsBlankLine)
        {
            AppendExpression(format, arguments, print.Value, renderExpression, usesSigned64Integers);
        }

        format.Append('\n');
        return new CPrintfPlan(format.ToString(), arguments);
    }

    private static void AppendExpression(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        if (expression.Type is not SmileType.String)
        {
            AppendTypedArgument(format, arguments, expression, renderExpression, usesSigned64Integers);
            return;
        }

        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                AppendLiteralToFormat(format, literal.Value);
                break;

            case BoundVariableExpression:
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                AppendExpression(format, arguments, binary.Left, renderExpression, usesSigned64Integers);
                AppendExpression(format, arguments, binary.Right, renderExpression, usesSigned64Integers);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart text:
                            AppendLiteralToFormat(format, text.Text);
                            break;

                        case BoundInterpolationExpressionPart interpolation:
                            AppendExpression(format, arguments, interpolation.Expression, renderExpression, usesSigned64Integers);
                            break;
                    }
                }

                break;

            default:
                // Current String expressions are literals, variables,
                // concatenation, or interpolation. Keeping a defensive %s
                // fallback makes future String nodes fail visibly in target
                // compilation rather than silently dropping output.
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;
        }
    }

    private static void AppendTypedArgument(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        string rendered = renderExpression(expression);
        switch (expression.Type)
        {
            case SmileType.Integer:
                if (usesSigned64Integers)
                {
                    // int64_t is not guaranteed to alias long long on every
                    // C implementation. The explicit value-preserving cast
                    // keeps the conventional %lld format portable.
                    format.Append("%lld");
                    arguments.Add("(long long)(" + rendered + ")");
                }
                else
                {
                    format.Append("%d");
                    arguments.Add(rendered);
                }

                break;

            case SmileType.Boolean:
                format.Append("%s");
                string condition = expression is BoundVariableExpression or BoundBooleanLiteralExpression
                    ? rendered
                    : "(" + rendered + ")";
                arguments.Add(condition + " ? \"TRUE\" : \"FALSE\"");
                break;

            case SmileType.String:
                format.Append("%s");
                arguments.Add(rendered);
                break;
        }
    }

    private static void AppendLiteralToFormat(StringBuilder format, string text)
    {
        foreach (char value in text)
        {
            // A user-authored '%' is data, never a printf directive. Doubling
            // it keeps every generated format string compiler-owned and safe.
            if (value == '%')
            {
                format.Append("%%");
            }
            else
            {
                format.Append(value);
            }
        }
    }
}

internal static class CGenerationFacts
{
    public static bool NeedsBooleanHeader(BoundProgram program) =>
        program.Variables.Any(variable => variable.Type is SmileType.Boolean) ||
        EnumerateRootExpressions(program).Any(ContainsBooleanLiteral);

    public static bool NeedsStringComparison(BoundProgramAnalysis analysis)
    {
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            bool needsComparison = statement switch
            {
                BoundLetStatement let when
                    let.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(let.Initializer),
                BoundSetStatement set when
                    set.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(set.Value),
                BoundPrintStatement { IsBlankLine: false } print =>
                    ContainsStringComparison(print.Value),
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    ContainsStringComparison(clause.Condition)),
                _ => false
            };

            if (needsComparison)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<BoundExpression> EnumerateRootExpressions(BoundProgram program)
    {
        foreach (BoundStatement statement in BoundStatementTree.Enumerate(program))
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    yield return let.Initializer;
                    break;

                case BoundSetStatement set:
                    yield return set.Value;
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    yield return print.Value;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        yield return clause.Condition;
                    }

                    break;
            }
        }
    }

    private static bool ContainsBooleanLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundBooleanLiteralExpression => true,
            BoundUnaryExpression unary => ContainsBooleanLiteral(unary.Operand),
            BoundBinaryExpression binary => ContainsBooleanLiteral(binary.Left) ||
                ContainsBooleanLiteral(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsBooleanLiteral(interpolation.Expression)),
            _ => false
        };

    private static bool ContainsStringComparison(BoundExpression expression) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or
                        BoundBinaryOperatorKind.Inequality ||
                ContainsStringComparison(binary.Left) ||
                ContainsStringComparison(binary.Right),
            BoundUnaryExpression unary =>
                ContainsStringComparison(unary.Operand),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsStringComparison(interpolation.Expression)),
            _ => false
        };

    internal static bool ShouldUseExactStorageComparison(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (expression.Left.Type is not SmileType.String ||
            expression.Operator.Kind is not (BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
            !IsDirectStringStorageOperand(expression.Left) ||
            !IsDirectStringStorageOperand(expression.Right) ||
            expression.Left is not BoundVariableExpression &&
            expression.Right is not BoundVariableExpression)
        {
            return false;
        }

        return RequiresExactLength(expression.Left, exactStringLengths) ||
            RequiresExactLength(expression.Right, exactStringLengths);
    }

    private static bool IsDirectStringStorageOperand(BoundExpression expression) =>
        expression is BoundVariableExpression or BoundStringLiteralExpression;

    private static bool RequiresExactLength(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        expression switch
        {
            BoundVariableExpression variable => exactStringLengths.ContainsKey(variable.Variable),
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            _ => false
        };

}

internal static class TargetTypes
{
    public static string CSharp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "bool",
            _ => "object"
        };

    public static string Java(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "boolean",
            _ => "Object"
        };

    public static string Swift(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "Int64" : "Int",
            SmileType.Boolean => "Bool",
            _ => "String"
        };

    public static string C(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "const char *",
            SmileType.Integer => integers.RequiresSigned64Storage ? "int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "const char *"
        };

    public static string CDeclaration(
        SmileType type,
        string name,
        TargetIntegerProfile integers) =>
        type is SmileType.String
            ? C(type, integers) + name
            : C(type, integers) + " " + name;

    public static string Cpp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "std::string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "std::int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "std::string"
        };
}

internal static class CSharpGenerationFacts
{
    public static bool NeedsInvariantCulture(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => NeedsInvariantCulture(let.Initializer, displayContext: false),
            BoundSetStatement set => NeedsInvariantCulture(set.Value, displayContext: false),
            BoundPrintStatement print => !print.IsBlankLine && NeedsInvariantCulture(print.Value, displayContext: true),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                NeedsInvariantCulture(clause.Condition, displayContext: false)),
            _ => false
        });

    private static bool NeedsInvariantCulture(BoundExpression expression, bool displayContext)
    {
        // C# only needs CultureInfo when a SMILE Integer is converted to text.
        // Its storage type is selected once from the complete bound program.
        if (displayContext && expression.Type is SmileType.Integer)
        {
            return true;
        }

        return expression switch
        {
            BoundUnaryExpression unary => NeedsInvariantCulture(unary.Operand, displayContext: false),
            BoundBinaryExpression binary => NeedsInvariantCulture(binary.Left, displayContext: false) ||
                NeedsInvariantCulture(binary.Right, displayContext: false),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                NeedsInvariantCulture(interpolation.Expression, displayContext: true)),
            _ => false
        };
    }
}

internal static class GeneratorValueFacts
{
    public static SmileValue Evaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value;
        }

        throw new InvalidOperationException("Bound expression could not be evaluated for target lowering.");
    }

    public static bool AssignedValuesContainNul(
        BoundProgramAnalysis analysis,
        VariableSymbol variable) =>
        analysis.AssignedValuesMayContainNul(variable);

    public static int MaximumAssignedUtf8ByteLength(
        BoundProgramAnalysis analysis,
        VariableSymbol variable) =>
        analysis.MaximumAssignedUtf8ByteLength(variable);

    public static bool TryGetNulContainingString(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out string value)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue evaluated) &&
            evaluated.Type is SmileType.String &&
            evaluated.StringValue.Contains('\0', StringComparison.Ordinal))
        {
            value = evaluated.StringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string DisplayText(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        return Evaluate(expression, values).ToDisplayText();
    }
}

internal static class TargetExpression
{
    public static string CSharp(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers).Write(expression);

    public static string CSharpDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers).WriteDisplay(expression);

    public static string JavaScript(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers).Write(expression);

    public static string JavaScriptDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers).WriteDisplay(expression);

    public static string Java(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Java, identifiers, integers).Write(expression);

    public static string JavaDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Java, identifiers, integers).WriteDisplay(expression);

    public static string Swift(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Swift, identifiers, integers).Write(expression);

    public static string SwiftDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Swift, identifiers, integers).WriteDisplay(expression);

    public static string C(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>? runtimeStringBuffers = null) =>
        new Writer(
            TargetLanguage.C,
            identifiers,
            integers,
            values,
            exactStringLengths,
            runtimeStringBuffers).Write(expression);

    public static string ObjectiveC(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>? runtimeStringBuffers = null) =>
        new Writer(
            TargetLanguage.ObjectiveC,
            identifiers,
            integers,
            values,
            exactStringLengths,
            runtimeStringBuffers).Write(expression);

    public static string CConstant(SmileValue value, TargetIntegerProfile integers) =>
        value.Type switch
        {
            SmileType.String => TargetEscapes.CString(value.StringValue),
            SmileType.Integer => CIntegerLiteral(value.IntegerValue, integers),
            SmileType.Boolean => value.BooleanValue ? "true" : "false",
            _ => TargetEscapes.CString(string.Empty)
        };

    private static string CIntegerLiteral(long value, TargetIntegerProfile integers)
    {
        if (!integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private sealed class Writer
    {
        private readonly TargetLanguage _language;
        private readonly TargetIdentifierMap _identifiers;
        private readonly TargetIntegerProfile _integers;
        private readonly IReadOnlyDictionary<VariableSymbol, SmileValue>? _values;
        private readonly IReadOnlyDictionary<VariableSymbol, string>? _exactStringLengths;
        private readonly IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>?
            _runtimeStringBuffers;

        private readonly record struct CStringStorageOperand(
            string Value,
            string Length,
            string? Build = null);

        public Writer(
            TargetLanguage language,
            TargetIdentifierMap identifiers,
            TargetIntegerProfile integers,
            IReadOnlyDictionary<VariableSymbol, SmileValue>? values = null,
            IReadOnlyDictionary<VariableSymbol, string>? exactStringLengths = null,
            IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>?
                runtimeStringBuffers = null)
        {
            _language = language;
            _identifiers = identifiers;
            _integers = integers;
            _values = values;
            _exactStringLengths = exactStringLengths;
            _runtimeStringBuffers = runtimeStringBuffers;
        }

        public string Write(BoundExpression expression) =>
            WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

        public string WriteDisplay(BoundExpression expression) =>
            expression.Type switch
            {
                SmileType.String => Write(expression),
                SmileType.Integer => _language switch
                {
                    TargetLanguage.CSharp => $"{MaybeParenthesizeForCall(Write(expression))}.ToString(CultureInfo.InvariantCulture)",
                    TargetLanguage.JavaScript => $"({Write(expression)}).toString()",
                    TargetLanguage.Java => _integers.RequiresSigned64Storage
                        ? $"Long.toString({Write(expression)})"
                        : $"Integer.toString({Write(expression)})",
                    TargetLanguage.Swift => $"String({Write(expression)})",
                    _ => Write(expression)
                },
                SmileType.Boolean => _language switch
                {
                    TargetLanguage.CSharp => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.JavaScript => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Java => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Swift => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    _ => Write(expression)
                },
                _ => EmptyStringLiteral()
            };

        private string WriteExpression(
            BoundExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            return expression switch
            {
                BoundStringLiteralExpression literal => StringLiteral(literal.Value),
                BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
                BoundBooleanLiteralExpression literal => BooleanLiteral(literal.Value),
                BoundVariableExpression variable => _identifiers.Get(variable.Variable),
                BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
                BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
                BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
                _ => EmptyStringLiteral()
            };
        }

        private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
        {
            int precedence = 7;
            string op = expression.Operator.Kind switch
            {
                // JavaScript BigInt deliberately has no unary-plus operator.
                // The SMILE identity operator is still preserved semantically
                // by emitting its already-typed operand unchanged.
                BoundUnaryOperatorKind.Identity when
                    _language is TargetLanguage.JavaScript &&
                    _integers.RequiresJavaScriptBigInt => string.Empty,
                BoundUnaryOperatorKind.Identity => "+",
                BoundUnaryOperatorKind.Negation => "-",
                BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Swift ? "!" : "!",
                _ => string.Empty
            };

            string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
            string text = op + operand;
            return precedence < parentPrecedence ? "(" + text + ")" : text;
        }

        private string WriteBinary(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (_language is TargetLanguage.JavaScript &&
                !_integers.RequiresJavaScriptBigInt &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Division)
            {
                // Number division is floating point. Math.trunc restores
                // SMILE's signed Integer quotient semantics while leaving
                // ordinary safe-Integer programs on idiomatic Number values.
                const int divisionPrecedence = 6;
                string call =
                    "Math.trunc(" +
                    WriteExpression(
                        expression.Left,
                        divisionPrecedence,
                        isRightChild: false,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    " / " +
                    WriteExpression(
                        expression.Right,
                        divisionPrecedence,
                        isRightChild: true,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    ")";
                return parentPrecedence > 7 ? "(" + call + ")" : call;
            }

            if (_language is TargetLanguage.Java &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteJavaStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteCStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            int precedence = Precedence(expression.Operator.Kind);
            string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
            string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);
            string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

            if (NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator))
            {
                return "(" + text + ")";
            }

            return text;
        }

        private string WriteJavaStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            int precedence = expression.Operator.Kind is BoundBinaryOperatorKind.Inequality
                ? 7
                : Precedence(expression.Operator.Kind);
            string receiver = IsSimpleReceiver(expression.Left)
                ? WriteExpression(expression.Left, 8, isRightChild: false, parentOperator: null)
                : "(" + WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) + ")";
            string text = receiver + ".equals(" + WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) + ")";
            if (expression.Operator.Kind is BoundBinaryOperatorKind.Inequality)
            {
                text = "!" + text;
            }

            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private string WriteCStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            bool hasRuntimeOperand = _runtimeStringBuffers is not null &&
                (_runtimeStringBuffers.ContainsKey(expression.Left) ||
                 _runtimeStringBuffers.ContainsKey(expression.Right));
            if (_exactStringLengths is not null &&
                (hasRuntimeOperand ||
                 CGenerationFacts.ShouldUseExactStorageComparison(expression, _exactStringLengths)))
            {
                CStringStorageOperand left = WriteCStringStorageOperand(expression.Left);
                CStringStorageOperand right = WriteCStringStorageOperand(expression.Right);
                bool equality = expression.Operator.Kind is BoundBinaryOperatorKind.Equality;
                string lengthOperator = equality ? " == " : " != ";
                string logicalOperator = equality ? " && " : " || ";
                string byteOperator = equality ? " == 0" : " != 0";

                // Compare lengths first so memcmp is reached only when both
                // operands expose the same number of logical UTF-8 bytes. That
                // keeps prefix collisions exact and never reads past either
                // current target value.
                string exactComparison = left.Length + lengthOperator + right.Length +
                    logicalOperator +
                    "memcmp(" + left.Value + ", " + right.Value + ", " + left.Length + ")" +
                    byteOperator;
                string[] parts = new[] { left.Build, right.Build, exactComparison }
                    .Where(part => !string.IsNullOrEmpty(part))
                    .Select(part => part!)
                    .ToArray();
                return "(" + string.Join(", ", parts) + ")";
            }

            if ((GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                     expression.Left,
                     out SmileValue constantLeft) &&
                 constantLeft.Type is SmileType.String &&
                 constantLeft.StringValue.Contains('\0', StringComparison.Ordinal)) ||
                (GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                     expression.Right,
                     out SmileValue constantRight) &&
                 constantRight.Type is SmileType.String &&
                 constantRight.StringValue.Contains('\0', StringComparison.Ordinal)))
            {
                // strcmp stops at NUL, so a constant NUL-bearing comparison
                // is the one literal-only case that cannot use the natural C
                // spelling below. Both operands are pure and path-independent.
                if (!GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                        expression,
                        out SmileValue evaluated))
                {
                    throw new InvalidOperationException(
                        "A constant C String comparison could not be evaluated.");
                }

                return evaluated.BooleanValue ? "1" : "0";
            }

            int precedence = Precedence(expression.Operator.Kind);
            string comparison = expression.Operator.Kind is BoundBinaryOperatorKind.Equality
                ? " == 0"
                : " != 0";
            string text =
                "strcmp(" +
                WriteCStringEqualityOperand(expression.Left) +
                ", " +
                WriteCStringEqualityOperand(expression.Right) +
                ")" +
                comparison;
            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private CStringStorageOperand WriteCStringStorageOperand(BoundExpression expression)
        {
            if (expression is BoundVariableExpression variable)
            {
                return WriteCStringVariableStorageOperand(variable);
            }

            if (expression is BoundStringLiteralExpression literal)
            {
                return new CStringStorageOperand(
                    TargetEscapes.CString(literal.Value),
                    Encoding.UTF8.GetByteCount(literal.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (_runtimeStringBuffers is not null &&
                _runtimeStringBuffers.TryGetValue(
                    expression,
                    out CCodeGenerator.RuntimeStringBuffer? buffer))
            {
                return new CStringStorageOperand(
                    buffer.Name,
                    buffer.Name + "Used",
                    WriteCStringRuntimeBuild(expression, buffer));
            }

            if (_values is not null &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
                value.Type is SmileType.String)
            {
                return new CStringStorageOperand(
                    TargetEscapes.CString(value.StringValue),
                    Encoding.UTF8.GetByteCount(value.StringValue)
                        .ToString(CultureInfo.InvariantCulture));
            }

            throw new InvalidOperationException(
                "Exact C String storage comparisons require a planned operand.");
        }

        private CStringStorageOperand WriteCStringVariableStorageOperand(BoundVariableExpression variable)
        {
            string name = _identifiers.Get(variable.Variable);
            string length = _exactStringLengths!.TryGetValue(variable.Variable, out string? exactLength)
                ? exactLength
                : $"strlen({name})";
            return new CStringStorageOperand(name, length);
        }

        private string WriteCStringRuntimeBuild(
            BoundExpression expression,
            CCodeGenerator.RuntimeStringBuffer buffer)
        {
            string used = buffer.Name + "Used";
            var operations = new List<string> { used + " = 0" };
            foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
            {
                switch (segment)
                {
                    case RuntimeLiteralTextSegment literal:
                        AppendBytes(TargetEscapes.CString(literal.Text),
                            Encoding.UTF8.GetByteCount(literal.Text));
                        break;

                    case RuntimeExpressionTextSegment
                        {
                            Expression: BoundVariableExpression variable
                        } when variable.Variable.Type is SmileType.String:
                        string name = _identifiers.Get(variable.Variable);
                        string length = _exactStringLengths!.TryGetValue(
                            variable.Variable,
                            out string? exactLength)
                            ? exactLength
                            : $"strlen({name})";
                        operations.Add(
                            "(memcpy(" + buffer.Name + " + " + used + ", " + name + ", " +
                            length + "), " + used + " += " + length + ")");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Integer:
                        string integer = WriteExpression(
                            runtime.Expression,
                            parentPrecedence: 0,
                            isRightChild: false,
                            parentOperator: null);
                        string format = _integers.RequiresSigned64Storage ? "%lld" : "%d";
                        string argument = _integers.RequiresSigned64Storage
                            ? "(long long)(" + integer + ")"
                            : integer;
                        operations.Add(
                            used + " += (size_t)snprintf(" + buffer.Name + " + " + used + ", " +
                            (buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture) + " - " + used +
                            ", \"" + format + "\", " + argument + ")");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Boolean:
                        string boolean = WriteExpression(
                            runtime.Expression,
                            parentPrecedence: 0,
                            isRightChild: false,
                            parentOperator: null);
                        operations.Add(
                            "((" + boolean + ") ? " +
                            "(memcpy(" + buffer.Name + " + " + used + ", \"TRUE\", 4), " + used + " += 4) : " +
                            "(memcpy(" + buffer.Name + " + " + used + ", \"FALSE\", 5), " + used + " += 5))");
                        break;
                }
            }

            operations.Add(buffer.Name + "[" + used + "] = '\\0'");
            return "(" + string.Join(", ", operations) + ")";

            void AppendBytes(string value, int byteLength)
            {
                if (byteLength == 0)
                {
                    return;
                }

                operations.Add(
                    "(memcpy(" + buffer.Name + " + " + used + ", " + value + ", " +
                    byteLength.ToString(CultureInfo.InvariantCulture) + "), " + used + " += " +
                    byteLength.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }

        private string WriteCStringEqualityOperand(BoundExpression expression)
        {
            if (expression is BoundStringLiteralExpression or BoundVariableExpression)
            {
                return WriteExpression(expression, 0, isRightChild: false, parentOperator: null);
            }

            if (_values is not null &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
                value.Type is SmileType.String)
            {
                // Statement-order abstract facts may prove a composite operand
                // without selecting a concrete IF path. Keep the generated C
                // compact by rendering that proven value as an ordinary literal.
                return TargetEscapes.CString(value.StringValue);
            }

            throw new InvalidOperationException(
                "A complex C String comparison operand must have runtime storage.");
        }

        private string WriteInterpolatedString(BoundInterpolatedStringExpression expression) =>
            _language switch
            {
                TargetLanguage.CSharp => "$\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.CSharpInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "{" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "\"",

                TargetLanguage.JavaScript => "`" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaScriptTemplateText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "${" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "`",

                TargetLanguage.Java => JoinJavaDisplaySegments(expression.Parts),

                TargetLanguage.Swift => "\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.SwiftInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "\\(" + WriteDisplay(interpolation.Expression) + ")",
                    _ => string.Empty
                })) + "\"",

                _ => EmptyStringLiteral()
            };

        private string JoinJavaDisplaySegments(IReadOnlyList<BoundInterpolatedPart> parts)
        {
            string[] segments = parts
                .Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaString(text.Text),
                    BoundInterpolationExpressionPart interpolation => WriteDisplay(interpolation.Expression),
                    _ => TargetEscapes.JavaString(string.Empty)
                })
                .Where(segment => segment != TargetEscapes.JavaString(string.Empty))
                .ToArray();
            return segments.Length == 0
                ? TargetEscapes.JavaString(string.Empty)
                : string.Join(" + ", segments);
        }

        private string StringLiteral(string value) =>
            _language switch
            {
                TargetLanguage.CSharp => TargetEscapes.CSharpString(value),
                TargetLanguage.JavaScript => TargetEscapes.JavaScriptString(value),
                TargetLanguage.Java => TargetEscapes.JavaString(value),
                TargetLanguage.Swift => TargetEscapes.SwiftString(value),
                _ => TargetEscapes.CString(value)
            };

        private string EmptyStringLiteral() => StringLiteral(string.Empty);

        private string IntegerLiteral(long value) =>
            _language switch
            {
                TargetLanguage.CSharp when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "long.MinValue"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.JavaScript when _integers.RequiresJavaScriptBigInt =>
                    value == long.MinValue
                        ? "(-9223372036854775808n)"
                        : value.ToString(CultureInfo.InvariantCulture) + "n",
                TargetLanguage.Java when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "Long.MIN_VALUE"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.Swift when _integers.RequiresSigned64Storage && value == long.MinValue =>
                    "Int64.min",
                TargetLanguage.C or TargetLanguage.ObjectiveC => CIntegerLiteral(value, _integers),
                _ => value.ToString(CultureInfo.InvariantCulture)
            };

        private string BooleanLiteral(bool value) =>
            _language is TargetLanguage.Swift
                ? value ? "true" : "false"
                : value ? "true" : "false";

        private string OperatorText(BoundBinaryOperatorKind kind) =>
            _language switch
            {
                TargetLanguage.JavaScript => kind switch
                {
                    BoundBinaryOperatorKind.Equality => "===",
                    BoundBinaryOperatorKind.Inequality => "!==",
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                TargetLanguage.Swift => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                _ => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                }
            };

        private static string CommonOperatorText(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
                BoundBinaryOperatorKind.Subtraction => "-",
                BoundBinaryOperatorKind.Multiplication => "*",
                BoundBinaryOperatorKind.Division => "/",
                BoundBinaryOperatorKind.Equality => "==",
                BoundBinaryOperatorKind.Inequality => "!=",
                BoundBinaryOperatorKind.Less => "<",
                BoundBinaryOperatorKind.LessOrEquals => "<=",
                BoundBinaryOperatorKind.Greater => ">",
                BoundBinaryOperatorKind.GreaterOrEquals => ">=",
                _ => string.Empty
            };

        private static int Precedence(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals => 4,
                BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
                BoundBinaryOperatorKind.LogicalAnd => 2,
                BoundBinaryOperatorKind.LogicalOr => 1,
                _ => 0
            };

        private static bool NeedsParentheses(
            int precedence,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (parentOperator.HasValue &&
                IsComparison(parentOperator.Value) &&
                precedence is 3 or 4)
            {
                // Swift comparison operators are non-associative, and the
                // grouping is educationally clearer in every destination.
                // Preserve both sides of nested SMILE Boolean comparisons.
                return true;
            }

            if (precedence < parentPrecedence)
            {
                return true;
            }

            return isRightChild &&
                precedence == parentPrecedence &&
                parentOperator is not (
                    BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.StringConcatenation or
                    BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr);
        }

        private static bool IsComparison(BoundBinaryOperatorKind kind) =>
            kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality or
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals;

        private static bool IsSimpleReceiver(BoundExpression expression) =>
            expression is BoundStringLiteralExpression or BoundVariableExpression;

        private static string MaybeParenthesizeForCall(string expression) =>
            IsSimpleCSharpCallReceiver(expression)
                ? expression
                : "(" + expression + ")";

        private static bool IsSimpleCSharpCallReceiver(string expression)
        {
            if (string.IsNullOrEmpty(expression) ||
                !SyntaxFacts.IsIdentifierStart(expression[0]))
            {
                return false;
            }

            // Identifiers and dotted constants such as long.MinValue can receive
            // a method call directly. Operators, negative literals, and grouped
            // expressions are parenthesized before .ToString(...) is appended.
            return expression.All(character =>
                SyntaxFacts.IsIdentifierPart(character) ||
                character == '.');
        }
    }
}

internal static class TargetEscapes
{
    public static string CSharpString(string text) => Quote(EscapeCSharp(text));

    public static string CString(string text) => Quote(EscapeCStyle(text));

    public static string ObjectiveCString(string text) => "@" + Quote(EscapeCStyle(text));

    public static string CPrintfFormatString(string text) => CString(text);

    public static string JavaScriptString(string text) => Quote(EscapeJavaScript(text));

    public static string JavaString(string text) => Quote(EscapeJava(text));

    public static string CobolString(string text) =>
        CanUsePlainCobolLiteral(text)
            ? Quote(text.Replace("\"", "\"\"", StringComparison.Ordinal))
            : "X\"" + ToHex(Encoding.UTF8.GetBytes(text)) + "\"";

    public static int CobolByteLength(string text) =>
        Encoding.UTF8.GetByteCount(text);

    public static string SwiftString(string text) => Quote(EscapeSwift(text));

    public static string PythonString(string text) => Quote(EscapePython(text, escapeBraces: false));

    public static string CSharpInterpolatedText(string text) => EscapeCSharpInterpolatedText(text);

    public static string JavaScriptTemplateText(string text) => EscapeJavaScriptTemplateText(text);

    public static string SwiftInterpolatedText(string text) => EscapeSwift(text);

    public static string PythonFStringText(string text) => EscapePython(text, escapeBraces: true);

    public static string MasmByteInitializers(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var parts = new List<string>();
        var currentText = new StringBuilder();

        foreach (byte value in bytes)
        {
            if (value is >= 32 and <= 126 and not (byte)'"')
            {
                currentText.Append((char)value);
                continue;
            }

            FlushText();
            parts.Add(value.ToString());
        }

        FlushText();
        return parts.Count == 0 ? "0" : string.Join(", ", parts);

        void FlushText()
        {
            if (currentText.Length == 0)
            {
                return;
            }

            parts.Add(Quote(currentText.ToString()));
            currentText.Clear();
        }
    }

    private static string Quote(string text) => $"\"{text}\"";

    private static bool CanUsePlainCobolLiteral(string text) =>
        text.Length > 0 &&
        text.All(value => value is >= ' ' and <= '~');

    private static string ToHex(byte[] bytes)
    {
        const string digits = "0123456789ABCDEF";
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (byte value in bytes)
        {
            builder.Append(digits[value >> 4]);
            builder.Append(digits[value & 0xF]);
        }

        return builder.ToString();
    }

    private static string EscapeCSharp(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeCSharpInterpolatedText(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' => "{{",
                '}' => "}}",
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeCStyle(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\000",
                '\a' => "\\007",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\013",
                _ when char.IsControl(value) => EscapeUtf8BytesAsOctal(value),
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeUtf8BytesAsOctal(char value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToString());
        var builder = new StringBuilder();

        foreach (byte utf8Byte in bytes)
        {
            builder.Append('\\');
            builder.Append(ToFixedOctal(utf8Byte));
        }

        return builder.ToString();
    }

    private static string ToFixedOctal(byte value)
    {
        Span<char> digits = stackalloc char[3];
        digits[0] = (char)('0' + ((value >> 6) & 0b111));
        digits[1] = (char)('0' + ((value >> 3) & 0b111));
        digits[2] = (char)('0' + (value & 0b111));
        return new string(digits);
    }

    private static string EscapeJava(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when value < 32 => "\\" + ToFixedOctal((byte)value),
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeJavaScript(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeJavaScriptTemplateText(string text)
    {
        var builder = new StringBuilder();

        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '`' => "\\`",
                '$' when index + 1 < text.Length && text[index + 1] == '{' => "\\$",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeSwift(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{{{(int)value:x}}}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapePython(string text, bool escapeBraces)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' when escapeBraces => "{{",
                '}' when escapeBraces => "}}",
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\x00",
                '\b' => "\\x08",
                '\f' => "\\x0c",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }
}

internal static class TextOutput
{
    public static string EnsureOneTrailingNewLine(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

        return normalized.TrimEnd('\r', '\n') + Environment.NewLine;
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Expected value was not found.");
    }
}
