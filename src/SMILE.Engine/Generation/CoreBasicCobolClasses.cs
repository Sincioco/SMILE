namespace SMILE.Engine;

internal sealed partial class CobolWriter
{
    private void WriteClassViews(ProcedurePlan plan, bool section)
    {
        Temporary[] views = plan.Temporaries.Where(temporary => temporary.IsClassView).ToArray();
        if (section && views.Length > 0) Line("       LINKAGE SECTION.");
        foreach (Temporary view in views) WriteRecordShape(view.Name, (ClassTypeSymbol)view.Type, [], 1, linkage: true);
    }

    private void WriteClassRoots(ProcedurePlan plan, BoundRoutineDeclaration? routine, bool register)
    {
        if (_program.ClassTypes.Count == 0) return;
        if (register && routine is null) Line("           CALL \"smile_object_initialize\"");
        if (!register && routine?.Symbol.ReturnType is ClassTypeSymbol)
            Line("           CALL \"smile_object_return\" USING BY VALUE SMILE-RETURN-VALUE");
        IEnumerable<VariableSymbol> variables = routine is null ? _program.Variables : routine.Locals;
        IEnumerable<string> roots = variables.Where(variable => variable.Type is ClassTypeSymbol && !variable.IsByRef).Select(Name)
            .Concat(plan.Temporaries.Where(temporary => temporary.Type is ClassTypeSymbol && !temporary.IsClassView).Select(temporary => temporary.Name));
        foreach (string root in register ? roots : roots.Reverse())
            Line($"           CALL \"smile_object_{(register ? "register" : "unregister")}\" USING BY REFERENCE {root}");
        if (!register)
        {
            Line("           CALL \"smile_object_collect\"");
            if (routine is null) Line("           CALL \"smile_object_shutdown\"");
        }
    }

    private sealed partial class ProcedureEmitter
    {
        private string NewClassView(ClassTypeSymbol type)
        {
            string name = $"SMILE-INSTANCE-{++_tempId}";
            _temporaries.Add(new Temporary(name, type, IsClassView: true));
            return name;
        }

        private string ClassView(ClassTypeSymbol type, string reference, int indent)
        {
            string view = NewClassView(type);
            Line(indent, $"SET ADDRESS OF {view} TO {reference}");
            return view;
        }

        private void RequireClass(string reference, int indent) =>
            Line(indent, $"CALL \"smile_object_require\" USING BY VALUE {reference} RETURNING {reference}");

        private string CaptureClassReference(BoundExpression expression, int indent)
        {
            string value = PrepareExpression(expression, indent);
            Temporary temporary = NewTemporary(expression.Type);
            Line(indent, $"SET {temporary.Name} TO {value}");
            RequireClass(temporary.Name, indent);
            return temporary.Name;
        }

        private string PrepareNew(BoundNewExpression creation, int indent)
        {
            Temporary reference = NewTemporary(creation.Class);
            string view = NewClassView(creation.Class);
            Line(indent, $"CALL \"smile_object_allocate_cobol\" USING BY VALUE LENGTH OF {view} RETURNING {reference.Name}");
            Line(indent, $"SET ADDRESS OF {view} TO {reference.Name}");
            Line(indent, $"INITIALIZE {view}");
            EmitCall(creation.Class.Constructor, creation.Arguments, indent, null, creation.ParameterOrder, reference.Name);
            return reference.Name;
        }

        private string PrepareIdentity(BoundIdentityExpression identity, int indent)
        {
            Temporary left = NewTemporary(identity.Left.Type);
            string value = PrepareExpression(identity.Left, indent);
            Line(indent, $"SET {left.Name} TO {value}");
            string right = PrepareExpression(identity.Right, indent);
            Temporary result = NewTemporary(SmileType.Boolean);
            Line(indent, $"IF {left.Name} {(identity.Negated ? "NOT =" : "=")} {right}");
            Line(indent + 1, $"MOVE 1 TO {result.Name}");
            Line(indent, "ELSE");
            Line(indent + 1, $"MOVE 0 TO {result.Name}");
            Line(indent, "END-IF");
            return result.Name;
        }

        private void EndClassStatement(int firstTemporary, int indent)
        {
            if (_owner._program.ClassTypes.Count == 0) return;
            foreach (Temporary temporary in _temporaries.Skip(firstTemporary).Where(temporary => temporary.Type is ClassTypeSymbol && !temporary.IsClassView))
                Line(indent, $"SET {temporary.Name} TO NULL");
            Line(indent, "CALL \"smile_object_collect\"");
        }
    }
}
