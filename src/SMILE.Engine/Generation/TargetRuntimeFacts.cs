namespace SMILE.Engine;

// INPUT and loop back-edges are the boundaries between source-known programs
// and programs whose expressions can fail only when a runtime path is reached.
// Keeping these small tree queries shared prevents ten generators from
// drifting on when input and checked-arithmetic support is required.
internal static class TargetRuntimeFacts
{
    public static IReadOnlyList<BoundInputStatement> Inputs(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).OfType<BoundInputStatement>().ToArray();

    public static bool HasInput(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement is BoundInputStatement);

    public static bool HasInput(BoundProgram program, SmileType type) =>
        BoundStatementTree.Enumerate(program).Any(statement =>
            statement is BoundInputStatement input && input.Variable.Type == type);

    public static bool RequiresUtf8Output(BoundProgram program) =>
        // Runtime String input can contain any valid Unicode scalar, while a
        // source-only program needs explicit UTF-8 output only when one of its
        // bound values actually contains non-ASCII text.
        HasInput(program) ||
        BoundStatementTree.EnumerateExpressions(program).Any(ContainsNonAsciiText);

    public static bool NeedsCheckedIntegerArithmetic(BoundProgram program) =>
        (HasInput(program) ||
         BoundStatementTree.Enumerate(program).Any(statement => statement is BoundWhileStatement)) &&
        BoundStatementTree.EnumerateExpressions(program).Any(ContainsIntegerArithmetic);

    public static bool ContainsIntegerArithmetic(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary =>
                unary.Operator.Kind is BoundUnaryOperatorKind.Negation &&
                unary.Operand.Type is SmileType.Integer ||
                ContainsIntegerArithmetic(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Left.Type is SmileType.Integer &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division ||
                ContainsIntegerArithmetic(binary.Left) ||
                ContainsIntegerArithmetic(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsIntegerArithmetic(hole.Expression)),
            _ => false
        };

    private static bool ContainsNonAsciiText(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal =>
                literal.Value.Any(character => character > 0x7f),
            BoundUnaryExpression unary => ContainsNonAsciiText(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsNonAsciiText(binary.Left) || ContainsNonAsciiText(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part switch
                {
                    BoundInterpolatedTextPart text =>
                        text.Text.Any(character => character > 0x7f),
                    BoundInterpolationExpressionPart hole =>
                        ContainsNonAsciiText(hole.Expression),
                    _ => false
                }),
            _ => false
        };
}
