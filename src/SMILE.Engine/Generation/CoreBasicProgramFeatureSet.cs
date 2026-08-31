namespace SMILE.Engine;

internal sealed record CoreBasicProgramFeatureSet(
    bool HasArrays,
    bool HasTwoDimensionalArrays,
    bool HasGetKey,
    bool HasClearScreen,
    bool HasMoveCursor,
    bool HasTextColor,
    bool HasWait,
    bool HasRandom,
    bool HasTimer,
    bool HasAbs,
    bool HasMin,
    bool HasMax)
{
    public bool HasInteractiveConsole => HasGetKey || HasClearScreen || HasMoveCursor || HasTextColor;

    public bool HasConsoleRuntime => HasInteractiveConsole || HasWait || HasRandom || HasTimer;

    public static CoreBasicProgramFeatureSet Create(BoundProgram program)
    {
        BoundStatement[] statements = EnumerateStatements(program).ToArray();
        BoundExpression[] expressions = EnumerateExpressions(statements).ToArray();
        return new CoreBasicProgramFeatureSet(
            program.AllVariables.Any(variable => variable.IsArray),
            program.AllVariables.Any(variable => variable.ArrayRank == 2),
            statements.Any(statement => statement is BoundGetKeyStatement),
            statements.Any(statement => statement is BoundClearScreenStatement),
            statements.Any(statement => statement is BoundMoveCursorStatement),
            statements.Any(statement => statement is BoundTextColorStatement),
            statements.Any(statement => statement is BoundWaitStatement),
            statements.Any(statement => statement is BoundRandomStatement),
            expressions.Any(expression => expression is BoundIntrinsicExpression { Kind: BoundIntrinsicKind.Timer }),
            expressions.Any(expression => expression is BoundIntrinsicExpression { Kind: BoundIntrinsicKind.Abs }),
            expressions.Any(expression => expression is BoundIntrinsicExpression { Kind: BoundIntrinsicKind.Min }),
            expressions.Any(expression => expression is BoundIntrinsicExpression { Kind: BoundIntrinsicKind.Max }));
    }

    private static IEnumerable<BoundStatement> EnumerateStatements(BoundProgram program)
    {
        foreach (BoundStatement statement in EnumerateItems(program.SourceItems))
        {
            yield return statement;
        }

        foreach (BoundRoutineDeclaration routine in program.Routines)
        {
            foreach (BoundStatement statement in EnumerateItems(routine.SourceItems))
            {
                yield return statement;
            }
        }
    }

    private static IEnumerable<BoundStatement> EnumerateItems(IReadOnlyList<BoundSourceItem> items)
    {
        foreach (BoundStatement statement in items.OfType<BoundStatement>())
        {
            yield return statement;
            IEnumerable<IReadOnlyList<BoundSourceItem>> children = statement switch
            {
                BoundIfStatement conditional => conditional.Clauses.Select(clause => clause.SourceItems)
                    .Append(conditional.ElseSourceItems),
                BoundSelectStatement select => select.Cases.Select(clause => clause.SourceItems),
                BoundForStatement loop => new[] { loop.SourceItems },
                BoundDoStatement loop => new[] { loop.SourceItems },
                _ => Array.Empty<IReadOnlyList<BoundSourceItem>>()
            };
            foreach (IReadOnlyList<BoundSourceItem> child in children)
            {
                foreach (BoundStatement nested in EnumerateItems(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<BoundExpression> EnumerateExpressions(IEnumerable<BoundStatement> statements)
    {
        foreach (BoundStatement statement in statements)
        {
            IEnumerable<BoundExpression> roots = statement switch
            {
                BoundSetStatement set => new[] { set.Value },
                BoundArraySetStatement set => set.Indices.Append(set.Value),
                BoundConstStatement constant => new[] { constant.Initializer },
                BoundCallStatement call => call.Arguments,
                BoundReturnStatement { Value: not null } returned => new[] { returned.Value },
                BoundCorePrintStatement print => print.Values,
                BoundIfStatement conditional => conditional.Clauses.Select(clause => clause.Condition),
                BoundSelectStatement select => new[] { select.Selector },
                BoundForStatement loop => new[] { loop.LowerBound, loop.UpperBound },
                BoundDoStatement { UntilCondition: not null } loop => new[] { loop.UntilCondition },
                BoundWaitStatement wait => new[] { wait.Duration },
                BoundMoveCursorStatement moveCursor => new[] { moveCursor.Column, moveCursor.Row },
                BoundRandomStatement random => new[] { random.LowerBound, random.UpperBound },
                _ => Array.Empty<BoundExpression>()
            };
            foreach (BoundExpression root in roots)
            {
                foreach (BoundExpression expression in Walk(root))
                {
                    yield return expression;
                }
            }
        }
    }

    private static IEnumerable<BoundExpression> Walk(BoundExpression expression)
    {
        yield return expression;
        IEnumerable<BoundExpression> children = expression switch
        {
            BoundArrayExpression array => array.Indices,
            BoundCallExpression call => call.Arguments,
            BoundIntrinsicExpression intrinsic => intrinsic.Arguments,
            BoundUnaryExpression unary => new[] { unary.Operand },
            BoundBinaryExpression binary => new[] { binary.Left, binary.Right },
            _ => Array.Empty<BoundExpression>()
        };
        foreach (BoundExpression child in children)
        {
            foreach (BoundExpression descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
