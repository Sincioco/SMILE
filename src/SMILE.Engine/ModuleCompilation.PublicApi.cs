namespace SMILE.Engine;

internal sealed partial class ModuleCompilation
{
    private void ValidatePublicApi(BoundProgram program)
    {
        var variables = program.Variables.ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var routines = program.Routines.Where(routine => routine.Symbol.Owner is null)
            .ToDictionary(routine => routine.Symbol.Name, routine => routine.Symbol, StringComparer.OrdinalIgnoreCase);
        var types = program.InstanceTypes.ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);
        foreach (ModuleMember member in program.ModuleMembers.Values.Where(member => member.IsPublic))
        {
            if (variables.TryGetValue(member.BoundName, out VariableSymbol? variable)) Check(variable.Type, variable.DeclarationSpan);
            if (routines.TryGetValue(member.BoundName, out RoutineSymbol? routine)) CheckRoutine(routine);
            if (!types.TryGetValue(member.BoundName, out InstanceTypeSymbol? type)) continue;
            foreach (InstanceFieldSymbol field in type.Fields.Where(field => !field.IsPrivate)) Check(field.Type, field.Span);
            foreach (RoutineSymbol method in type.Methods.Where(method => !method.IsPrivate)) CheckRoutine(method);
            foreach (InstancePropertySymbol property in type.Properties.Where(property => !property.IsPrivate)) Check(property.Type, property.Span);
            if (type is ClassTypeSymbol reference) CheckRoutine(reference.Constructor);
        }

        void CheckRoutine(RoutineSymbol routine)
        {
            if (routine.ReturnType is { } result) Check(result, routine.DeclarationSpan);
            foreach (VariableSymbol parameter in routine.Parameters) Check(parameter.Type, parameter.DeclarationSpan);
        }

        void Check(SmileType type, TextSpan span)
        {
            if (program.ModuleMembers.TryGetValue(type.Name, out ModuleMember? typeMember) && !typeMember.IsPublic)
                Report("SMILE3513", $"A Public declaration exposes Private type '{typeMember.Owner.Name}.{typeMember.Name}'.", span);
        }
    }
}
