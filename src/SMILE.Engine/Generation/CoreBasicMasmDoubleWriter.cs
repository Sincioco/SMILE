using System.Globalization;

namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private readonly Dictionary<long, string> _doubleLiterals = new();
    private string InternDouble(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        if (_doubleLiterals.TryGetValue(bits, out string? label)) return label;
        label = $"smile_double_{_doubleLiterals.Count + 1}";
        _doubleLiterals.Add(bits, label);
        return label;
    }

    private void WriteDoubleDeclarations()
    {
        foreach ((long bits, string label) in _doubleLiterals)
            Line($"{label} REAL8 {DoubleSemantics.FormatLiteral(BitConverter.Int64BitsToDouble(bits))}");
    }

    private void WriteDoublePrototypes()
    {
        var features = new DoubleProgramFeatures(_program);
        if (features.Has(BoundIntrinsicKind.Abs)) Line("option nokeyword:<fabs>");
        foreach (string definition in NativeDoubleSupport.Definitions(features))
        {
            string declaration = definition.Split('\n')[0];
            string name = declaration[..declaration.IndexOf('(')].Split(' ')[^1];
            Line($"{name} PROTO");
        }
        if (features.HasFormatting) Line("smile_print_double PROTO :REAL8");
        if (features.Has(BoundIntrinsicKind.TextFromDouble)) Line("smile_text_from_double PROTO :REAL8");
        foreach (BoundIntrinsicKind kind in features.Intrinsics)
            if (DoubleMathName(kind) is string name) Line($"{name} PROTO");
    }

    private static string? DoubleMathName(BoundIntrinsicKind kind) => kind switch
    {
        BoundIntrinsicKind.Abs => "fabs", BoundIntrinsicKind.Sqrt => "sqrt", BoundIntrinsicKind.Sin => "sin",
        BoundIntrinsicKind.Cos => "cos", BoundIntrinsicKind.Atan2 => "atan2", BoundIntrinsicKind.Floor => "floor",
        BoundIntrinsicKind.Ceiling => "ceil", BoundIntrinsicKind.Truncate => "trunc", BoundIntrinsicKind.Round => "nearbyint", _ => null
    };

    public static string? GenerateDoubleRuntime(BoundProgram program)
    {
        var features = new DoubleProgramFeatures(program);
        if (!features.NeedsFailure && !features.HasFormatting) return null;
        string source = "#include <stdio.h>\n#include <stdlib.h>\n#include <stdint.h>\n#include <math.h>\n#include <string.h>\n\n";
        source += string.Join("\n", NativeDoubleSupport.Definitions(features)).Replace("static ", "");
        if (features.HasFormatting) source += """

void smile_print_double(double value)
{
    char buffer[32];
    smile_format_double(value, buffer);
    fputs(buffer, stdout);
}
""";
        if (features.Has(BoundIntrinsicKind.TextFromDouble)) source += """

char *smile_text_allocate(size_t length);
const char *smile_text_from_double(double value)
{
    char *buffer = smile_text_allocate(32);
    smile_format_double(value, buffer);
    return buffer;
}
""";
        return GeneratedSourceLayout.Normalize(source, TargetLanguage.C);
    }

    private sealed partial class ProcedureEmitter
    {
        private void EmitDoubleBinary(BoundBinaryExpression binary, Storage left, Storage right, int indent)
        {
            int line = binary.OperatorSpan.Line;
            if (binary.Operator.Kind is BoundBinaryOperatorKind.Division)
            {
                Emit(indent, $"movsd xmm0, QWORD PTR {Address(right.Offset)}");
                Emit(indent, $"mov edx, {line}");
                NoteCall(2);
                Emit(indent, "call smile_double_divisor");
                Emit(indent, "movsd xmm1, xmm0");
            }
            else Emit(indent, $"movsd xmm1, QWORD PTR {Address(right.Offset)}");
            Emit(indent, $"movsd xmm0, QWORD PTR {Address(left.Offset)}");
            if (binary.Type is SmileType.Boolean)
            {
                string comparison = binary.Operator.Kind switch
                {
                    BoundBinaryOperatorKind.Equality => "sete", BoundBinaryOperatorKind.Inequality => "setne",
                    BoundBinaryOperatorKind.Less => "setb", BoundBinaryOperatorKind.LessOrEquals => "setbe",
                    BoundBinaryOperatorKind.Greater => "seta", _ => "setae"
                };
                Emit(indent, "ucomisd xmm0, xmm1");
                Emit(indent, comparison + " al");
                Emit(indent, "movzx rax, al");
                return;
            }
            string operation = binary.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Addition => "addsd", BoundBinaryOperatorKind.Subtraction => "subsd",
                BoundBinaryOperatorKind.Multiplication => "mulsd", _ => "divsd"
            };
            Emit(indent, $"{operation} xmm0, xmm1");
            EmitCheckedDouble(line, indent);
        }

        private void EmitCheckedDouble(int line, int indent)
        {
            Emit(indent, $"mov edx, {line}");
            NoteCall(2);
            Emit(indent, "call smile_check_double");
            Emit(indent, "movq rax, xmm0");
        }

        private void EmitDoubleIntrinsic(BoundIntrinsicExpression intrinsic, int indent)
        {
            var captured = new List<Storage>();
            foreach (BoundExpression argument in intrinsic.Arguments)
            {
                EmitExpression(argument, indent);
                Storage temporary = NewTemporary();
                Emit(indent, $"mov QWORD PTR {Address(temporary.Offset)}, rax");
                if (_owner._usesManagedText && argument.Type is SmileType.String) _textTemporaryRoots.Add(temporary);
                captured.Add(temporary);
            }
            for (int index = 0; index < captured.Count; index++)
                Emit(indent, intrinsic.Arguments[index].Type is SmileType.Double
                    ? $"movsd xmm{index}, QWORD PTR {Address(captured[index].Offset)}"
                    : $"mov {ParameterRegisters[index]}, QWORD PTR {Address(captured[index].Offset)}");
            if (intrinsic.Kind is BoundIntrinsicKind.ToDouble)
            {
                Emit(indent, "cvtsi2sd xmm0, rcx");
            }
            else if (intrinsic.Kind is BoundIntrinsicKind.Min or BoundIntrinsicKind.Max)
            {
                string ready = NewLabel("double_selected");
                Emit(indent, "ucomisd xmm0, xmm1");
                Emit(indent, $"{(intrinsic.Kind is BoundIntrinsicKind.Min ? "jbe" : "jae")} {ready}");
                Emit(indent, "movsd xmm0, xmm1");
                Label(ready);
            }
            else
            {
                string function = intrinsic.Kind switch
                {
                    BoundIntrinsicKind.ToNumber => "smile_to_number", BoundIntrinsicKind.TextToDouble => "smile_text_to_double",
                    BoundIntrinsicKind.TextFromDouble => "smile_text_from_double", BoundIntrinsicKind.Clamp => "smile_double_clamp",
                    _ => DoubleMathName(intrinsic.Kind)!
                };
                bool needsLine = intrinsic.Kind is BoundIntrinsicKind.ToNumber or BoundIntrinsicKind.TextToDouble or BoundIntrinsicKind.Clamp;
                if (needsLine) Emit(indent, $"mov {ParameterRegisters[captured.Count]}, {intrinsic.Span.Line}");
                NoteCall(captured.Count + (needsLine ? 1 : 0));
                Emit(indent, $"call {function}");
                if (intrinsic.Kind is BoundIntrinsicKind.Sqrt or BoundIntrinsicKind.Sin or BoundIntrinsicKind.Cos or BoundIntrinsicKind.Atan2)
                {
                    EmitCheckedDouble(intrinsic.Span.Line, indent);
                    return;
                }
            }
            if (intrinsic.Type is SmileType.Double) Emit(indent, "movq rax, xmm0");
            else if (intrinsic.Type is SmileType.String)
            {
                Storage result = NewTemporary();
                Emit(indent, $"mov QWORD PTR {Address(result.Offset)}, rax");
                _textTemporaryRoots.Add(result);
            }
        }
    }
}
