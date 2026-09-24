using System.Text;

namespace SMILE.Engine;

// SMILE indexes Unicode scalar values, independently of the host's UTF-16 storage.
// This owner is shared by constant folding and runtime evaluation.
internal static class TextIntrinsics
{
    public static SmileValue Evaluate(BoundIntrinsicKind kind, IReadOnlyList<SmileValue> arguments)
    {
        string text = arguments[0].StringValue;
        if (kind is BoundIntrinsicKind.TextLength)
        {
            return SmileValue.FromInteger(text.EnumerateRunes().Count());
        }

        long start = arguments[1].IntegerValue;
        long count = kind is BoundIntrinsicKind.TextSlice ? arguments[2].IntegerValue : 1;
        if (start < 0 || count <= 0)
        {
            return kind is BoundIntrinsicKind.TextSlice ? SmileValue.FromString("") : SmileValue.FromInteger(-1);
        }

        var result = new StringBuilder();
        foreach (Rune scalar in text.EnumerateRunes())
        {
            if (start > 0)
            {
                start--;
                continue;
            }

            if (kind is BoundIntrinsicKind.TextCodeAt)
            {
                return SmileValue.FromInteger(scalar.Value);
            }

            result.Append(scalar.ToString());
            if (--count == 0) break;
        }

        return kind is BoundIntrinsicKind.TextSlice ? SmileValue.FromString(result.ToString()) : SmileValue.FromInteger(-1);
    }
}
