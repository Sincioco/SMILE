namespace SMILE.Engine;

/// <summary>
/// Walks nested bound control flow for generator planning. Keeping this traversal
/// in one place prevents a backend pre-scan from forgetting a legal container,
/// such as Select Case, and then emitting the wrong loop-exit machinery.
/// </summary>
internal static class CoreBasicBoundControlFlow
{
    public static bool ContainsExitTargetingLoop(
        IReadOnlyList<BoundSourceItem> items,
        BoundExitKind targetKind,
        bool requireInterveningOtherLoop = false)
    {
        return VisitForExit(
            items,
            targetKind,
            requireInterveningOtherLoop,
            nestedSameKindLoops: 0,
            nestedOtherKindLoops: 0);
    }

    public static IEnumerable<(BoundStatement Statement, BoundExitKind Kind)> EnumerateLoops(
        IReadOnlyList<BoundSourceItem> items)
    {
        foreach (BoundSourceItem item in items)
        {
            if (item is not BoundStatement statement)
            {
                continue;
            }

            switch (statement)
            {
                case BoundForStatement loop:
                    yield return (loop, BoundExitKind.For);
                    foreach (var nested in EnumerateLoops(loop.SourceItems))
                    {
                        yield return nested;
                    }

                    break;
                case BoundDoStatement loop:
                    yield return (loop, BoundExitKind.Do);
                    foreach (var nested in EnumerateLoops(loop.SourceItems))
                    {
                        yield return nested;
                    }

                    break;
                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        foreach (var nested in EnumerateLoops(clause.SourceItems))
                        {
                            yield return nested;
                        }
                    }

                    foreach (var nested in EnumerateLoops(conditional.ElseSourceItems))
                    {
                        yield return nested;
                    }

                    break;
                case BoundSelectStatement select:
                    foreach (BoundSelectCaseClause clause in select.Cases)
                    {
                        foreach (var nested in EnumerateLoops(clause.SourceItems))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }
    }

    private static bool VisitForExit(
        IReadOnlyList<BoundSourceItem> items,
        BoundExitKind targetKind,
        bool requireInterveningOtherLoop,
        int nestedSameKindLoops,
        int nestedOtherKindLoops)
    {
        foreach (BoundStatement statement in items.OfType<BoundStatement>())
        {
            if (statement is BoundExitStatement exit &&
                exit.Kind == targetKind &&
                nestedSameKindLoops == 0 &&
                (!requireInterveningOtherLoop || nestedOtherKindLoops > 0))
            {
                return true;
            }

            switch (statement)
            {
                case BoundIfStatement conditional:
                    if (conditional.Clauses.Any(clause => VisitForExit(
                            clause.SourceItems,
                            targetKind,
                            requireInterveningOtherLoop,
                            nestedSameKindLoops,
                            nestedOtherKindLoops)) ||
                        VisitForExit(
                            conditional.ElseSourceItems,
                            targetKind,
                            requireInterveningOtherLoop,
                            nestedSameKindLoops,
                            nestedOtherKindLoops))
                    {
                        return true;
                    }

                    break;
                case BoundSelectStatement select:
                    if (select.Cases.Any(clause => VisitForExit(
                            clause.SourceItems,
                            targetKind,
                            requireInterveningOtherLoop,
                            nestedSameKindLoops,
                            nestedOtherKindLoops)))
                    {
                        return true;
                    }

                    break;
                case BoundForStatement loop:
                    if (VisitForExit(
                            loop.SourceItems,
                            targetKind,
                            requireInterveningOtherLoop,
                            nestedSameKindLoops + (targetKind is BoundExitKind.For ? 1 : 0),
                            nestedOtherKindLoops + (targetKind is BoundExitKind.Do ? 1 : 0)))
                    {
                        return true;
                    }

                    break;
                case BoundDoStatement loop:
                    if (VisitForExit(
                            loop.SourceItems,
                            targetKind,
                            requireInterveningOtherLoop,
                            nestedSameKindLoops + (targetKind is BoundExitKind.Do ? 1 : 0),
                            nestedOtherKindLoops + (targetKind is BoundExitKind.For ? 1 : 0)))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
