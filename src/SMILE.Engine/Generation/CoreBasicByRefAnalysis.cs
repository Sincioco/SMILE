namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private readonly HashSet<VariableSymbol> _addressedVariables = new();
        private readonly HashSet<VariableSymbol> _swiftReferenceParameters = new();

        private sealed record ReferenceCall(RoutineSymbol? Caller, RoutineSymbol Routine,
            IReadOnlyList<BoundExpression> Arguments, IReadOnlyList<int>? Order);

        private void FindReferenceStorage()
        {
            if (!_program.Routines.Any(routine => routine.Symbol.Parameters.Any(parameter => parameter.IsByRef))) return;
            ReferenceCall[] calls = ReferenceCalls(_program.SourceItems, null)
                .Concat(_program.Routines.SelectMany(routine => ReferenceCalls(routine.SourceItems, routine.Symbol))).ToArray();
            foreach (ReferenceCall call in calls)
            {
                for (int index = 0; index < call.Arguments.Count; index++)
                    if (RoutineArguments.ParameterAtSourceIndex(call.Routine, call.Order, index).IsByRef)
                        _addressedVariables.Add(LocationOwner(call.Arguments[index]));
            }
            if (_language is not TargetLanguage.Swift) return;

            // Swift inout requires exclusive access to the entire stored variable,
            // including its array. Use it whenever the call graph proves that safe.
            var globals = _program.Routines.ToDictionary(routine => routine.Symbol,
                routine => ReferencedGlobals(routine.SourceItems).ToHashSet());
            bool changed;
            do
            {
                changed = false;
                foreach (ReferenceCall call in calls.Where(call => call.Caller is not null))
                {
                    int before = globals[call.Caller!].Count;
                    globals[call.Caller!].UnionWith(globals[call.Routine]);
                    changed |= globals[call.Caller!].Count != before;
                }
            } while (changed);

            var shared = new HashSet<RoutineSymbol>();
            foreach (ReferenceCall call in calls)
            {
                VariableSymbol[] owners = call.Arguments.Select((argument, index) =>
                        RoutineArguments.ParameterAtSourceIndex(call.Routine, call.Order, index).IsByRef ? LocationOwner(argument) : null)
                    .OfType<VariableSymbol>().ToArray();
                bool overlap = owners.Distinct().Count() != owners.Length || owners.Any(owner =>
                    owner.IsByRef && owners.Any(other => other != owner && (other.IsByRef || other.IsGlobal)));
                bool globalAlias = owners.Any(owner => owner.IsGlobal && globals[call.Routine].Contains(owner) ||
                    owner.IsByRef && globals[call.Routine].Count > 0);
                if (overlap || globalAlias) shared.Add(call.Routine);
            }
            // An escaping location closure cannot capture an inout parameter.
            // Forwarding into such a routine gives the caller the same representation.
            do
            {
                changed = false;
                foreach (ReferenceCall call in calls.Where(call => call.Caller is not null && shared.Contains(call.Routine)))
                    if (call.Arguments.Select((argument, index) => (argument, index)).Any(item =>
                        RoutineArguments.ParameterAtSourceIndex(call.Routine, call.Order, item.index).IsByRef &&
                        LocationOwner(item.argument).IsByRef)) changed |= shared.Add(call.Caller!);
            } while (changed);
            foreach (RoutineSymbol routine in shared)
                _swiftReferenceParameters.UnionWith(routine.Parameters.Where(parameter => parameter.IsByRef));
        }

        private static IEnumerable<ReferenceCall> ReferenceCalls(IReadOnlyList<BoundSourceItem> items, RoutineSymbol? caller) =>
            EnumerateStatements(items).OfType<BoundCallStatement>()
                .Select(call => new ReferenceCall(caller, call.Routine, call.Arguments, call.ParameterOrder))
                .Concat(EnumerateExpressions(items).OfType<BoundCallExpression>()
                    .Select(call => new ReferenceCall(caller, call.Routine, call.Arguments, call.ParameterOrder)));

        private static VariableSymbol LocationOwner(BoundExpression expression) => expression switch
        {
            BoundVariableExpression variable => variable.Variable,
            BoundArrayExpression array => array.Array,
            _ => throw new InvalidOperationException("A bound ByRef argument must be writable.")
        };

        private static IEnumerable<VariableSymbol> ReferencedGlobals(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateExpressions(items).Select(expression => expression switch
            {
                BoundVariableExpression variable => variable.Variable,
                BoundArrayExpression array => array.Array,
                _ => null
            }).OfType<VariableSymbol>().Where(variable => variable.IsGlobal && !variable.IsConstant)
                .Concat(AssignedGlobals(items))
                .Concat(EnumerateStatements(items).Select(statement => statement switch
                {
                    BoundArraySetStatement set => set.Array,
                    BoundTextFileLoadStatement load => load.Destination,
                    BoundDataLoadStatement load => load.Destination,
                    BoundDataSaveStatement save => save.Source,
                    _ => null
                }).OfType<VariableSymbol>().Where(variable => variable.IsGlobal));
    }
}
