using System.Text;

namespace SMILE.Engine;

public sealed record EvaluationResult(
    bool Success,
    string Output,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class SmileEvaluator
{
    private readonly SmileTranspiler _transpiler = new();

    public EvaluationResult Evaluate(string source)
    {
        BindResult bindResult = _transpiler.Bind(source);
        if (!bindResult.Success || bindResult.Program is null)
        {
            return new EvaluationResult(false, string.Empty, bindResult.Diagnostics);
        }

        var output = new StringBuilder();
        var values = new Dictionary<VariableSymbol, string>();

        foreach (BoundStatement statement in bindResult.Program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    // The binder already evaluated official LET v1.0 strings
                    // once. The evaluator stores that value so later PRINT
                    // expressions read variables the same way target programs do.
                    values[let.Variable] = let.ConstantValue;
                    break;

                case BoundPrintStatement print:
                    if (!print.IsBlankLine)
                    {
                        output.Append(EvaluateExpression(print.Value, values));
                    }

                    output.Append('\n');
                    break;
            }
        }

        return new EvaluationResult(true, output.ToString(), bindResult.Diagnostics);
    }

    private static string EvaluateExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> values)
    {
        if (BoundStringConstantEvaluator.TryEvaluate(expression, values, out string value))
        {
            return value;
        }

        // This should be unreachable for a successfully bound v1.0 program.
        // Throwing here keeps accidental semantic-model corruption obvious to
        // tests instead of silently producing a misleading reference output.
        throw new InvalidOperationException("Bound string expression could not be evaluated.");
    }
}
