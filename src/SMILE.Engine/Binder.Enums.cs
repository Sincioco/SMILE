using System.Globalization;

namespace SMILE.Engine;

internal sealed partial class Binder
{
    private readonly Dictionary<string, EnumTypeSymbol> _enums = new(StringComparer.OrdinalIgnoreCase);

    private SmileType ResolveType(TypeNameSyntax syntax)
    {
        if (_records.TryGetValue(syntax.Name, out RecordTypeSymbol? record)) return record;
        if (_enums.TryGetValue(syntax.Name, out EnumTypeSymbol? nominal)) return nominal;
        SmileType? builtin = syntax.Name.ToUpperInvariant() switch
        {
            "NUMBER" => SmileType.Integer, "DOUBLE" => SmileType.Double,
            "TEXT" => SmileType.String, "BOOLEAN" => SmileType.Boolean, _ => null
        };
        if (builtin is not null) return builtin;
        Report("SMILE3423", $"Type '{syntax.Name}' is not declared.", syntax.Span);
        return SmileType.Error;
    }

    private void BindEnums()
    {
        foreach (EnumTypeSymbol type in _enums.Values)
        {
            var members = new List<EnumMemberSymbol>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long previous = -1;
            foreach (EnumMemberDeclarationSyntax member in type.Declaration.Members)
            {
                long value = 0;
                try
                {
                    if (member.Value is null) value = checked(previous + 1);
                    else if (!TryEnumNumber(member.Value, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out value))
                        Report("SMILE3422", "Enum member value must be a checked compile-time Number expression.", member.Value.Span);
                }
                catch (OverflowException) { Report("SMILE3422", "Implicit Enum value exceeds Number range.", member.Span); }
                if (!names.Add(member.Name)) { Report("SMILE3421", $"Enum member '{member.Name}' is already declared.", member.Span); continue; }
                members.Add(new EnumMemberSymbol(type, member.Name, value, member.Span));
                previous = value;
            }
            type.Members = members;
            if (members.Count == 0) Report("SMILE3421", "An Enum must declare at least one member.", type.Declaration.Span);
        }
    }

    // Enum initializers are checked even where ordinary target Number arithmetic
    // wraps. Resolve source Const expressions independently so no overflow is lost.
    private bool TryEnumNumber(ExpressionSyntax source, HashSet<string> resolving, out long value)
    {
        value = 0;
        try
        {
            switch (source)
            {
                case IntegerLiteralExpressionSyntax literal:
                    return long.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
                case ParenthesizedExpressionSyntax parentheses:
                    return TryEnumNumber(parentheses.Expression, resolving, out value);
                case NameExpressionSyntax name when _constantSyntax.TryGetValue(name.Name, out ConstStatementSyntax? constant):
                    if (!resolving.Add(name.Name)) return false;
                    bool known = TryEnumNumber(constant.Initializer, resolving, out value);
                    resolving.Remove(name.Name);
                    return known;
                case UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } unary:
                    if (unary.Operand is IntegerLiteralExpressionSyntax { Text: "9223372036854775808" }) { value = long.MinValue; return true; }
                    if (!TryEnumNumber(unary.Operand, resolving, out long operand)) return false;
                    value = checked(-operand); return true;
                case BinaryExpressionSyntax binary:
                    if (!TryEnumNumber(binary.Left, resolving, out long left) || !TryEnumNumber(binary.Right, resolving, out long right)) return false;
                    value = binary.OperatorToken.Kind switch
                    {
                        SyntaxKind.PlusToken => checked(left + right), SyntaxKind.MinusToken => checked(left - right),
                        SyntaxKind.StarToken => checked(left * right), SyntaxKind.SlashToken => checked(left / right),
                        SyntaxKind.ModKeyword => left % right, _ => throw new InvalidOperationException()
                    };
                    return true;
                case CallExpressionSyntax call:
                    if (call.Arguments.Count is < 1 or > 2 || !TryEnumNumber(call.Arguments[0], resolving, out long first)) return false;
                    if (call.Arguments.Count == 1 && call.Name.Equals("Abs", StringComparison.OrdinalIgnoreCase)) { value = Math.Abs(first); return true; }
                    if (call.Arguments.Count != 2 || !TryEnumNumber(call.Arguments[1], resolving, out long second)) return false;
                    if (call.Name.Equals("Min", StringComparison.OrdinalIgnoreCase)) { value = Math.Min(first, second); return true; }
                    if (call.Name.Equals("Max", StringComparison.OrdinalIgnoreCase)) { value = Math.Max(first, second); return true; }
                    return false;
            }
        }
        catch (Exception error) when (error is OverflowException or DivideByZeroException or InvalidOperationException) { }
        return false;
    }

    private BoundExpression BindMember(MemberAccessExpressionSyntax syntax, bool constantsOnly)
    {
        if (syntax.Receiver is NameExpressionSyntax name && _enums.TryGetValue(name.Name, out EnumTypeSymbol? type))
        {
            EnumMemberSymbol? member = type.Members.FirstOrDefault(item => item.Name.Equals(syntax.Name, StringComparison.OrdinalIgnoreCase));
            if (member is not null) return new BoundEnumExpression(type, member.Value, member);
        }
        if (syntax.Receiver is NameExpressionSyntax enumName && _enums.ContainsKey(enumName.Name))
        {
            Report("SMILE3423", $"Enum member '{syntax.Name}' is not declared.", syntax.NameSpan);
            return new BoundErrorExpression();
        }
        return BindRecordField(syntax, null, constantsOnly);
    }

    private BoundSourceItem? BindNestedEnum(EnumDeclarationSyntax syntax)
    {
        Report("SMILE3420", "Enum declarations must appear directly at program level.", syntax.Span);
        return null;
    }
}
