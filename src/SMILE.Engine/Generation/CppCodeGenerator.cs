using System.Globalization;
using System.Text;

namespace SMILE.Engine;

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
        AppendSourceItems(
            source,
            program.SourceItems,
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

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.Cpp, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

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
            AppendSourceItems(
                source,
                clause.SourceItems,
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
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
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
