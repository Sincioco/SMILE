namespace SMILE.Engine;

internal static class BoundProgramSimplifier
{
    public static BoundProgram Simplify(BoundProgram program)
    {
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        var values = new Dictionary<VariableSymbol, SmileValue>();
        IReadOnlyList<BoundSourceItem> sourceItems = SimplifySourceItems(
            program.SourceItems,
            values,
            analysis);
        return new BoundProgram(sourceItems, program.Variables);
    }

    private static IReadOnlyList<BoundSourceItem> SimplifySourceItems(
        IReadOnlyList<BoundSourceItem> sourceItems,
        Dictionary<VariableSymbol, SmileValue> values,
        BoundProgramAnalysis analysis)
    {
        var simplifiedItems = new List<BoundSourceItem>(sourceItems.Count);

        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            if (sourceItem is not BoundStatement statement)
            {
                // Layout has no value facts to simplify, but its exact place
                // in the ordered body is part of generated source fidelity.
                simplifiedItems.Add(sourceItem);
                continue;
            }

            switch (statement)
            {
                case BoundLetStatement let:
                    BoundExpression initializer = SimplifyExpression(let.Initializer, values);
                    simplifiedItems.Add(let with { Initializer = initializer });
                    UpdateKnownValue(values, let.Variable, initializer);
                    break;

                case BoundSetStatement set:
                    // SET sees the old value throughout its complete right side.
                    // Only after simplification and evaluation succeeds does the
                    // new value become visible to later statements.
                    BoundExpression value = SimplifyExpression(set.Value, values);
                    simplifiedItems.Add(set with { Value = value });
                    UpdateKnownValue(values, set.Variable, value);
                    break;

                case BoundInputStatement input:
                    // INPUT preserves the variable's declared type but replaces
                    // every source-known value with runtime data.
                    simplifiedItems.Add(input);
                    values.Remove(input.Variable);
                    break;

                case BoundPrintStatement print:
                    simplifiedItems.Add(print with
                    {
                        Value = SimplifyExpression(print.Value, values)
                    });
                    break;

                case BoundIfStatement conditional:
                    simplifiedItems.Add(SimplifyIfStatement(conditional, values, analysis));
                    break;

                case BoundWhileStatement loop:
                    simplifiedItems.Add(SimplifyWhileStatement(loop, values, analysis));
                    break;

                default:
                    simplifiedItems.Add(statement);
                    break;
            }
        }

        return simplifiedItems;
    }

    private static BoundIfStatement SimplifyIfStatement(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values,
        BoundProgramAnalysis analysis)
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
            IReadOnlyList<BoundSourceItem> branchSourceItems =
                SimplifySourceItems(clause.SourceItems, branchValues, analysis);
            clauses.Add(new BoundConditionalClause(condition, branchSourceItems));
            outgoingEnvironments.Add(branchValues);
        }

        var elseValues = new Dictionary<VariableSymbol, SmileValue>(values);
        IReadOnlyList<BoundSourceItem> elseSourceItems = conditional.HasElseClause
            ? SimplifySourceItems(conditional.ElseSourceItems, elseValues, analysis)
            : conditional.ElseSourceItems;

        // An IF without ELSE has an implicit unchanged path. Every explicit
        // branch is retained and contributes to the merge even when its
        // condition is currently known. This prevents a branch-specific value
        // from leaking into later simplification or target planning.
        outgoingEnvironments.Add(
            conditional.HasElseClause
                ? elseValues
                : new Dictionary<VariableSymbol, SmileValue>(values));
        MergeKnownValues(values, outgoingEnvironments);

        return new BoundIfStatement(
            clauses,
            elseSourceItems,
            conditional.HasElseClause);
    }

    private static BoundWhileStatement SimplifyWhileStatement(
        BoundWhileStatement loop,
        Dictionary<VariableSymbol, SmileValue> values,
        BoundProgramAnalysis analysis)
    {
        BoundWhileStatementAnalysis loopFacts = analysis.GetWhileFacts(loop);
        var headValues = new Dictionary<VariableSymbol, SmileValue>(
            GeneratorConditionFacts.KnownValues(loopFacts.ValuesAtHead));

        // A WHILE condition and every expression in its body may run after an
        // arbitrary number of back-edges. Only fixed-point loop-head facts are
        // safe here; pre-loop LET values would freeze the first iteration into
        // generated code and could even turn a finite loop into an infinite one.
        BoundExpression condition = SimplifyExpression(loop.Condition, headValues);
        if (condition is BoundBooleanLiteralExpression &&
            GeneratorConditionFacts.ContainsVariableRead(loop.Condition))
        {
            // Retain source variable reads when short-circuit simplification
            // proves the complete condition. The target warning-safe wrapper
            // makes the condition runtime-opaque, while the original AND/OR
            // still suppresses its unreachable operand. This keeps strict C#
            // and Java builds warning-free without deleting learner code or
            // introducing a synthetic discard read.
            condition = loop.Condition;
        }

        IReadOnlyList<BoundSourceItem> body = SimplifySourceItems(
            loop.SourceItems,
            headValues,
            analysis);

        ReplaceKnownValues(values, loopFacts.ValuesAfter);
        return new BoundWhileStatement(condition, body, loop.KeywordSpan);
    }

    private static void ReplaceKnownValues(
        Dictionary<VariableSymbol, SmileValue> destination,
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> analyzedValues)
    {
        destination.Clear();
        foreach ((VariableSymbol variable, AnalyzedValue analyzed) in analyzedValues)
        {
            if (analyzed.IsKnown)
            {
                destination.Add(variable, analyzed.Value);
            }
        }
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
            if (left is BoundBooleanLiteralExpression { Value: false })
            {
                return new BoundBooleanLiteralExpression(false);
            }

            if (right is BoundBooleanLiteralExpression { Value: false } &&
                CanEvaluateWithoutRuntimeFailure(left, values))
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
            if (left is BoundBooleanLiteralExpression { Value: true })
            {
                return new BoundBooleanLiteralExpression(true);
            }

            if (right is BoundBooleanLiteralExpression { Value: true } &&
                CanEvaluateWithoutRuntimeFailure(left, values))
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

    private static bool CanEvaluateWithoutRuntimeFailure(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(expression, values);
        return !result.IsInvalid && !result.MayFailAtRuntime;
    }
}
