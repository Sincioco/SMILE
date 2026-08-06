using System.Globalization;
using System.Text;

namespace SMILE.Engine;

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
