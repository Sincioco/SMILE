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
        var values = new Dictionary<VariableSymbol, SmileValue>();
        ExecuteStatements(bindResult.Program.Statements, values, output);

        return new EvaluationResult(true, output.ToString(), bindResult.Diagnostics);
    }

    private static void ExecuteStatements(
        IReadOnlyList<BoundStatement> statements,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    values.Add(let.Variable, EvaluateExpression(let.Initializer, values));
                    break;

                case BoundSetStatement set:
                    // Evaluate into a temporary first. The old target value is
                    // visible throughout the right side, and the environment
                    // changes only after the complete expression succeeds.
                    SmileValue assignedValue = EvaluateExpression(set.Value, values);
                    values[set.Variable] = assignedValue;
                    break;

                case BoundPrintStatement print:
                    if (!print.IsBlankLine)
                    {
                        output.Append(EvaluateExpression(print.Value, values).ToDisplayText());
                    }

                    output.Append('\n');
                    break;

                case BoundIfStatement conditional:
                    ExecuteIf(conditional, values, output);
                    break;
            }
        }
    }

    private static void ExecuteIf(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output)
    {
        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            if (!EvaluateExpression(clause.Condition, values).BooleanValue)
            {
                continue;
            }

            ExecuteStatements(clause.Statements, values, output);
            return;
        }

        if (conditional.HasElseClause)
        {
            ExecuteStatements(conditional.ElseStatements, values, output);
        }
    }

    private static SmileValue EvaluateExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value;
        }

        // This should be unreachable for a successfully bound program.
        // Throwing here keeps accidental semantic-model corruption obvious to
        // tests instead of silently producing a misleading reference output.
        throw new InvalidOperationException("Bound expression could not be evaluated.");
    }
}
