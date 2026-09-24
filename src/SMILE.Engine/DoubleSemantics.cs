using System.Globalization;
using System.Text.RegularExpressions;

namespace SMILE.Engine;

// Binary64 stays distinct from Number. Evaluation and constant analysis share
// the same checks; destination writers keep native arithmetic and math APIs.
internal static class DoubleSemantics
{
    public const string FailureMessage = "Double operation has an invalid domain, conversion, or nonfinite result.";

    public static bool IsIntrinsic(BoundIntrinsicKind kind) => kind >= BoundIntrinsicKind.ToDouble;

    public static SmileType ResultType(BoundIntrinsicKind kind, IReadOnlyList<BoundExpression> arguments) => kind switch
    {
        BoundIntrinsicKind.TextSlice or BoundIntrinsicKind.TextFromDouble => SmileType.String,
        BoundIntrinsicKind.ToNumber => SmileType.Integer,
        _ when IsIntrinsic(kind) => SmileType.Double,
        BoundIntrinsicKind.Abs or BoundIntrinsicKind.Min or BoundIntrinsicKind.Max when arguments.Count > 0 => arguments[0].Type,
        _ => SmileType.Integer
    };

    public static bool UsesDouble(BoundIntrinsicExpression expression) =>
        IsIntrinsic(expression.Kind) || expression.Type is SmileType.Double;

    public static string Format(double value)
    {
        Check(value);
        if (value == 0) return BitConverter.DoubleToInt64Bits(value) < 0 ? "-0.0" : "0.0";
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.IndexOfAny(['.', 'e', 'E']) < 0 ? text + ".0" : text;
    }

    public static string FormatLiteral(double value)
    {
        string text = Format(value);
        int exponent = text.IndexOfAny(['e', 'E']);
        return exponent >= 0 && !text[..exponent].Contains('.') ? text.Insert(exponent, ".0") : text;
    }

    public static bool TryParse(string text, out double value)
    {
        value = 0;
        return Regex.IsMatch(text, @"\A[\x09-\x0D ]*[+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?[\x09-\x0D ]*\z") &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static double Check(double value) => double.IsFinite(value)
        ? value : throw new ArithmeticException(FailureMessage);

    public static SmileValue Binary(BoundBinaryOperatorKind kind, double left, double right) => kind switch
    {
        BoundBinaryOperatorKind.Addition => SmileValue.FromDouble(Check(left + right)),
        BoundBinaryOperatorKind.Subtraction => SmileValue.FromDouble(Check(left - right)),
        BoundBinaryOperatorKind.Multiplication => SmileValue.FromDouble(Check(left * right)),
        BoundBinaryOperatorKind.Division => SmileValue.FromDouble(right != 0 ? Check(left / right) : throw new ArithmeticException(FailureMessage)),
        BoundBinaryOperatorKind.Equality => SmileValue.FromBoolean(left == right),
        BoundBinaryOperatorKind.Inequality => SmileValue.FromBoolean(left != right),
        BoundBinaryOperatorKind.Less => SmileValue.FromBoolean(left < right),
        BoundBinaryOperatorKind.LessOrEquals => SmileValue.FromBoolean(left <= right),
        BoundBinaryOperatorKind.Greater => SmileValue.FromBoolean(left > right),
        BoundBinaryOperatorKind.GreaterOrEquals => SmileValue.FromBoolean(left >= right),
        _ => throw new InvalidOperationException("Unsupported Double operator.")
    };

    public static SmileValue Intrinsic(BoundIntrinsicKind kind, IReadOnlyList<SmileValue> values)
    {
        if (kind is BoundIntrinsicKind.ToDouble) return SmileValue.FromDouble(values[0].IntegerValue);
        if (kind is BoundIntrinsicKind.TextToDouble) return TryParse(values[0].StringValue, out double parsed)
            ? SmileValue.FromDouble(parsed) : throw new ArithmeticException(FailureMessage);
        double a = values[0].DoubleValue;
        if (kind is BoundIntrinsicKind.TextFromDouble) return SmileValue.FromString(Format(a));
        if (kind is BoundIntrinsicKind.ToNumber)
        {
            double truncated = Math.Truncate(a);
            if (truncated < -9223372036854775808.0 || truncated >= 9223372036854775808.0)
                throw new ArithmeticException(FailureMessage);
            return SmileValue.FromInteger((long)truncated);
        }
        double b = values.Count > 1 ? values[1].DoubleValue : 0;
        double c = values.Count > 2 ? values[2].DoubleValue : 0;
        if (kind is BoundIntrinsicKind.Clamp && b > c || kind is BoundIntrinsicKind.Sqrt && a < 0)
            throw new ArithmeticException(FailureMessage);
        double result = kind switch
        {
            BoundIntrinsicKind.Abs => Math.Abs(a),
            BoundIntrinsicKind.Min => a <= b ? a : b,
            BoundIntrinsicKind.Max => a >= b ? a : b,
            BoundIntrinsicKind.Clamp => a < b ? b : a > c ? c : a,
            BoundIntrinsicKind.Sqrt => Math.Sqrt(a),
            BoundIntrinsicKind.Sin => Math.Sin(a),
            BoundIntrinsicKind.Cos => Math.Cos(a),
            BoundIntrinsicKind.Atan2 => Math.Atan2(a, b),
            BoundIntrinsicKind.Floor => Math.Floor(a),
            BoundIntrinsicKind.Ceiling => Math.Ceiling(a),
            BoundIntrinsicKind.Truncate => Math.Truncate(a),
            BoundIntrinsicKind.Round => Math.Round(a, MidpointRounding.ToEven),
            _ => throw new InvalidOperationException("Unsupported Double intrinsic.")
        };
        return SmileValue.FromDouble(Check(result));
    }
}
