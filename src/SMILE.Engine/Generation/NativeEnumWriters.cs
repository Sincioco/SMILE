using System.Globalization;

namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private string EnumOperand(EnumTypeSymbol type, long value, EnumMemberSymbol? authored = null)
    {
        EnumMemberSymbol? member = authored ?? type.Members.FirstOrDefault(item => item.Value == value);
        return member is null ? value.ToString(CultureInfo.InvariantCulture) : _identifiers.Get(member);
    }
}

internal sealed partial class CobolWriter
{
    private void WriteEnumConstants()
    {
        foreach (EnumTypeSymbol type in _program.EnumTypes)
            foreach (EnumMemberSymbol member in type.Members)
                Line($"       78 {_identifiers.Get(member)} VALUE {member.Value.ToString(CultureInfo.InvariantCulture)}.");
    }

    private string EnumOperand(EnumTypeSymbol type, long value, EnumMemberSymbol? authored = null)
    {
        EnumMemberSymbol? member = authored ?? type.Members.FirstOrDefault(item => item.Value == value);
        return member is null ? value.ToString(CultureInfo.InvariantCulture) : _identifiers.Get(member);
    }
}
