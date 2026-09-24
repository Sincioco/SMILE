namespace SMILE.Engine;

internal sealed partial class Binder
{
    private SmileValue? BindParameterDefault(ParameterSyntax parameter)
    {
        if (parameter.DefaultValue is null) return null;
        ExpressionSyntax source = parameter.DefaultValue;
        while (source is ParenthesizedExpressionSyntax parentheses) source = parentheses.Expression;
        bool permitted = source is IntegerLiteralExpressionSyntax or StringLiteralExpressionSyntax or
            BooleanLiteralExpressionSyntax or NameExpressionSyntax or
            UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken, Operand: IntegerLiteralExpressionSyntax };
        BoundExpression expression = BindExpression(source, constantsOnly: true);
        StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(expression, _constantValues);
        if (!permitted || !result.IsKnown || result.Value.Type != parameter.DeclaredType)
        {
            Report("SMILE2161", "An Optional default must be a literal or Const of the exact parameter type.", source.Span);
            return null;
        }
        return result.Value;
    }

    private BoundArguments BindCallArguments(RoutineSymbol routine, IReadOnlyList<ExpressionSyntax> arguments, TextSpan callSpan)
    {
        var values = new List<BoundExpression>();
        int[] slots = Enumerable.Repeat(-1, routine.Parameters.Count).ToArray();
        bool sawNamed = false;
        for (int sourceIndex = 0; sourceIndex < arguments.Count; sourceIndex++)
        {
            ExpressionSyntax source = arguments[sourceIndex];
            int parameterIndex = sourceIndex;
            if (source is NamedArgumentExpressionSyntax named)
            {
                sawNamed = true;
                parameterIndex = routine.Parameters.ToList().FindIndex(parameter =>
                    string.Equals(parameter.Name, named.Name, StringComparison.OrdinalIgnoreCase));
                source = named.Value;
                if (parameterIndex < 0) Report("SMILE2162", $"Routine '{routine.Name}' has no parameter '{named.Name}'.", named.Span);
            }
            else if (sawNamed)
            {
                Report("SMILE2163", "Positional arguments must precede named arguments.", source.Span);
            }

            BoundExpression value = BindExpression(source);
            values.Add(value);
            if (parameterIndex < 0 || parameterIndex >= slots.Length)
            {
                if (parameterIndex >= slots.Length) Report("SMILE2148", $"Too many arguments for '{routine.Name}'.", source.Span);
                continue;
            }
            if (slots[parameterIndex] >= 0) Report("SMILE2164", $"Parameter '{routine.Parameters[parameterIndex].Name}' is supplied twice.", source.Span);
            slots[parameterIndex] = sourceIndex;
            if (value.Type is not SmileType.Error && value.Type != routine.Parameters[parameterIndex].Type)
            {
                Report("SMILE2149", $"Argument {parameterIndex + 1} for '{routine.Parameters[parameterIndex].Name}' must be {DisplayType(routine.Parameters[parameterIndex].Type)}.", source.Span);
            }
        }

        for (int parameterIndex = 0; parameterIndex < slots.Length; parameterIndex++)
        {
            if (slots[parameterIndex] >= 0) continue;
            VariableSymbol parameter = routine.Parameters[parameterIndex];
            slots[parameterIndex] = values.Count;
            if (parameter.DefaultValue is SmileValue defaultValue) values.Add(RoutineArguments.Literal(defaultValue));
            else
            {
                Report("SMILE2148", $"Required parameter '{parameter.Name}' was not supplied for '{routine.Name}'.", callSpan);
                values.Add(new BoundErrorExpression());
            }
        }
        return new BoundArguments(values, !sawNamed && slots.SequenceEqual(Enumerable.Range(0, slots.Length)) ? null : slots);
    }
}
