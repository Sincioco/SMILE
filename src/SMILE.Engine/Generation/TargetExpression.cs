using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal static class TargetExpression
{
    public static string CSharp(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).Write(expression);

    public static string CSharpDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).WriteDisplay(expression);

    public static string JavaScript(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).Write(expression);

    public static string JavaScriptDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).WriteDisplay(expression);

    public static string Java(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.Java, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).Write(expression);

    public static string JavaDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.Java, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).WriteDisplay(expression);

    public static string Swift(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.Swift, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).Write(expression);

    public static string SwiftDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(TargetLanguage.Swift, identifiers, integers, checkedRuntimeArithmetic: checkedRuntimeArithmetic).WriteDisplay(expression);

    public static string C(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>? runtimeStringBuffers = null,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(
            TargetLanguage.C,
            identifiers,
            integers,
            values,
            exactStringLengths,
            runtimeStringBuffers,
            checkedRuntimeArithmetic).Write(expression);

    public static string ObjectiveC(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>? runtimeStringBuffers = null,
        bool checkedRuntimeArithmetic = false) =>
        new Writer(
            TargetLanguage.ObjectiveC,
            identifiers,
            integers,
            values,
            exactStringLengths,
            runtimeStringBuffers,
            checkedRuntimeArithmetic).Write(expression);

    public static string CConstant(SmileValue value, TargetIntegerProfile integers) =>
        value.Type switch
        {
            SmileType.String => TargetEscapes.CString(value.StringValue),
            SmileType.Integer => CIntegerLiteral(value.IntegerValue, integers),
            SmileType.Boolean => value.BooleanValue ? "true" : "false",
            _ => TargetEscapes.CString(string.Empty)
        };

    private static string CIntegerLiteral(long value, TargetIntegerProfile integers)
    {
        if (!integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private sealed class Writer
    {
        private readonly TargetLanguage _language;
        private readonly TargetIdentifierMap _identifiers;
        private readonly TargetIntegerProfile _integers;
        private readonly IReadOnlyDictionary<VariableSymbol, SmileValue>? _values;
        private readonly IReadOnlyDictionary<VariableSymbol, string>? _exactStringLengths;
        private readonly IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>?
            _runtimeStringBuffers;
        private readonly bool _checkedRuntimeArithmetic;
        private int _checkedArithmeticDepth;

        private readonly record struct CStringStorageOperand(
            string Value,
            string Length,
            string? Build = null);

        public Writer(
            TargetLanguage language,
            TargetIdentifierMap identifiers,
            TargetIntegerProfile integers,
            IReadOnlyDictionary<VariableSymbol, SmileValue>? values = null,
            IReadOnlyDictionary<VariableSymbol, string>? exactStringLengths = null,
            IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer>?
                runtimeStringBuffers = null,
            bool checkedRuntimeArithmetic = false)
        {
            _language = language;
            _identifiers = identifiers;
            _integers = integers;
            _values = values;
            _exactStringLengths = exactStringLengths;
            _runtimeStringBuffers = runtimeStringBuffers;
            _checkedRuntimeArithmetic = checkedRuntimeArithmetic;
        }

        public string Write(BoundExpression expression) =>
            WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

        public string WriteDisplay(BoundExpression expression) =>
            expression.Type switch
            {
                SmileType.String => Write(expression),
                SmileType.Integer => _language switch
                {
                    TargetLanguage.CSharp => $"{MaybeParenthesizeForCall(Write(expression))}.ToString(CultureInfo.InvariantCulture)",
                    TargetLanguage.JavaScript => $"({Write(expression)}).toString()",
                    TargetLanguage.Java => _integers.RequiresSigned64Storage
                        ? $"Long.toString({Write(expression)})"
                        : $"Integer.toString({Write(expression)})",
                    TargetLanguage.Swift => $"String({Write(expression)})",
                    _ => Write(expression)
                },
                SmileType.Boolean => _language switch
                {
                    TargetLanguage.CSharp => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.JavaScript => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Java => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Swift => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    _ => Write(expression)
                },
                _ => EmptyStringLiteral()
            };

        private string WriteExpression(
            BoundExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            return expression switch
            {
                BoundStringLiteralExpression literal => StringLiteral(literal.Value),
                BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
                BoundBooleanLiteralExpression literal => BooleanLiteral(literal.Value),
                BoundVariableExpression variable => _identifiers.Get(variable.Variable),
                BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
                BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
                BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
                _ => EmptyStringLiteral()
            };
        }

        private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
        {
            if (_checkedRuntimeArithmetic &&
                expression.Operator.Kind is BoundUnaryOperatorKind.Negation &&
                expression.Operand.Type is SmileType.Integer)
            {
                string call = "_smile_negate(" +
                    WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) +
                    ")";
                return parentPrecedence > 7 ? "(" + call + ")" : call;
            }

            int precedence = 7;
            string op = expression.Operator.Kind switch
            {
                // JavaScript BigInt deliberately has no unary-plus operator.
                // The SMILE identity operator is still preserved semantically
                // by emitting its already-typed operand unchanged.
                BoundUnaryOperatorKind.Identity when
                    _language is TargetLanguage.JavaScript &&
                    _integers.RequiresJavaScriptBigInt => string.Empty,
                BoundUnaryOperatorKind.Identity => "+",
                BoundUnaryOperatorKind.Negation => "-",
                BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Swift ? "!" : "!",
                _ => string.Empty
            };

            string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
            string text = op + operand;
            return precedence < parentPrecedence ? "(" + text + ")" : text;
        }

        private string WriteBinary(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (_checkedRuntimeArithmetic &&
                expression.Left.Type is SmileType.Integer &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division)
            {
                string helper = expression.Operator.Kind switch
                {
                    BoundBinaryOperatorKind.Addition => "_smile_add",
                    BoundBinaryOperatorKind.Subtraction => "_smile_subtract",
                    BoundBinaryOperatorKind.Multiplication => "_smile_multiply",
                    BoundBinaryOperatorKind.Division => "_smile_divide",
                    _ => throw new InvalidOperationException("Unsupported checked Integer operator.")
                };
                int depth = _checkedArithmeticDepth++;
                string checkedLeft = WriteExpression(
                    expression.Left,
                    0,
                    isRightChild: false,
                    parentOperator: null);
                string checkedRight = WriteExpression(
                    expression.Right,
                    0,
                    isRightChild: false,
                    parentOperator: null);
                _checkedArithmeticDepth--;
                string call = _language is TargetLanguage.C or TargetLanguage.ObjectiveC
                    ? "(_smile_arithmetic_left[" + depth.ToString(CultureInfo.InvariantCulture) +
                      "] = " + checkedLeft + ", _smile_arithmetic_right[" +
                      depth.ToString(CultureInfo.InvariantCulture) + "] = " + checkedRight + ", " +
                      helper + "(_smile_arithmetic_left[" +
                      depth.ToString(CultureInfo.InvariantCulture) +
                      "], _smile_arithmetic_right[" +
                      depth.ToString(CultureInfo.InvariantCulture) + "]))"
                    : helper + "(" + checkedLeft + ", " + checkedRight + ")";
                return parentPrecedence > 7 ? "(" + call + ")" : call;
            }

            if (_language is TargetLanguage.JavaScript &&
                !_integers.RequiresJavaScriptBigInt &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Division)
            {
                // Number division is floating point. Math.trunc restores
                // SMILE's signed Integer quotient semantics while leaving
                // ordinary safe-Integer programs on idiomatic Number values.
                const int divisionPrecedence = 6;
                string call =
                    "Math.trunc(" +
                    WriteExpression(
                        expression.Left,
                        divisionPrecedence,
                        isRightChild: false,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    " / " +
                    WriteExpression(
                        expression.Right,
                        divisionPrecedence,
                        isRightChild: true,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    ")";
                return parentPrecedence > 7 ? "(" + call + ")" : call;
            }

            if (_language is TargetLanguage.Java &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteJavaStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteCStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            if (_checkedRuntimeArithmetic &&
                _language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
                expression.Left.Type is SmileType.Integer or SmileType.Boolean &&
                expression.Operator.Kind is not (
                    BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr) &&
                (ContainsCheckedArithmetic(expression.Left) || ContainsCheckedArithmetic(expression.Right)))
            {
                int depth = _checkedArithmeticDepth++;
                string sequencedLeft = WriteExpression(
                    expression.Left,
                    0,
                    isRightChild: false,
                    parentOperator: null);
                string sequencedRight = WriteExpression(
                    expression.Right,
                    0,
                    isRightChild: false,
                    parentOperator: null);
                _checkedArithmeticDepth--;
                string slot = depth.ToString(CultureInfo.InvariantCulture);
                return "(_smile_arithmetic_left[" + slot + "] = " + sequencedLeft +
                    ", _smile_arithmetic_right[" + slot + "] = " + sequencedRight +
                    ", _smile_arithmetic_left[" + slot + "] " +
                    OperatorText(expression.Operator.Kind) + " _smile_arithmetic_right[" +
                    slot + "])";
            }

            int precedence = Precedence(expression.Operator.Kind);
            string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
            string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);
            string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

            if (NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator))
            {
                return "(" + text + ")";
            }

            return text;
        }

        private string WriteJavaStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            int precedence = expression.Operator.Kind is BoundBinaryOperatorKind.Inequality
                ? 7
                : Precedence(expression.Operator.Kind);
            string receiver = IsSimpleReceiver(expression.Left)
                ? WriteExpression(expression.Left, 8, isRightChild: false, parentOperator: null)
                : "(" + WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) + ")";
            string text = receiver + ".equals(" + WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) + ")";
            if (expression.Operator.Kind is BoundBinaryOperatorKind.Inequality)
            {
                text = "!" + text;
            }

            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private string WriteCStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            bool hasRuntimeOperand = _runtimeStringBuffers is not null &&
                (_runtimeStringBuffers.ContainsKey(expression.Left) ||
                 _runtimeStringBuffers.ContainsKey(expression.Right));
            if (_exactStringLengths is not null &&
                (hasRuntimeOperand ||
                 CGenerationFacts.ShouldUseExactStorageComparison(expression, _exactStringLengths)))
            {
                CStringStorageOperand left = WriteCStringStorageOperand(expression.Left);
                CStringStorageOperand right = WriteCStringStorageOperand(expression.Right);
                bool equality = expression.Operator.Kind is BoundBinaryOperatorKind.Equality;
                string lengthOperator = equality ? " == " : " != ";
                string logicalOperator = equality ? " && " : " || ";
                string byteOperator = equality ? " == 0" : " != 0";

                // Compare lengths first so memcmp is reached only when both
                // operands expose the same number of logical UTF-8 bytes. That
                // keeps prefix collisions exact and never reads past either
                // current target value.
                string exactComparison = left.Length + lengthOperator + right.Length +
                    logicalOperator +
                    "memcmp(" + left.Value + ", " + right.Value + ", " + left.Length + ")" +
                    byteOperator;
                string[] parts = new[] { left.Build, right.Build, exactComparison }
                    .Where(part => !string.IsNullOrEmpty(part))
                    .Select(part => part!)
                    .ToArray();
                return "(" + string.Join(", ", parts) + ")";
            }

            if ((GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                     expression.Left,
                     out SmileValue constantLeft) &&
                 constantLeft.Type is SmileType.String &&
                 constantLeft.StringValue.Contains('\0', StringComparison.Ordinal)) ||
                (GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                     expression.Right,
                     out SmileValue constantRight) &&
                 constantRight.Type is SmileType.String &&
                 constantRight.StringValue.Contains('\0', StringComparison.Ordinal)))
            {
                // strcmp stops at NUL, so a constant NUL-bearing comparison
                // is the one literal-only case that cannot use the natural C
                // spelling below. Both operands are pure and path-independent.
                if (!GeneratorConditionFacts.TryEvaluateWithoutVariableReads(
                        expression,
                        out SmileValue evaluated))
                {
                    throw new InvalidOperationException(
                        "A constant C String comparison could not be evaluated.");
                }

                return evaluated.BooleanValue ? "1" : "0";
            }

            int precedence = Precedence(expression.Operator.Kind);
            string comparison = expression.Operator.Kind is BoundBinaryOperatorKind.Equality
                ? " == 0"
                : " != 0";
            string text =
                "strcmp(" +
                WriteCStringEqualityOperand(expression.Left) +
                ", " +
                WriteCStringEqualityOperand(expression.Right) +
                ")" +
                comparison;
            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private CStringStorageOperand WriteCStringStorageOperand(BoundExpression expression)
        {
            if (expression is BoundVariableExpression variable)
            {
                return WriteCStringVariableStorageOperand(variable);
            }

            if (expression is BoundStringLiteralExpression literal)
            {
                return new CStringStorageOperand(
                    TargetEscapes.CString(literal.Value),
                    Encoding.UTF8.GetByteCount(literal.Value).ToString(CultureInfo.InvariantCulture));
            }

            if (_runtimeStringBuffers is not null &&
                _runtimeStringBuffers.TryGetValue(
                    expression,
                    out CCodeGenerator.RuntimeStringBuffer? buffer))
            {
                return new CStringStorageOperand(
                    buffer.Name,
                    buffer.Name + "Used",
                    WriteCStringRuntimeBuild(expression, buffer));
            }

            if (_values is not null &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
                value.Type is SmileType.String)
            {
                return new CStringStorageOperand(
                    TargetEscapes.CString(value.StringValue),
                    Encoding.UTF8.GetByteCount(value.StringValue)
                        .ToString(CultureInfo.InvariantCulture));
            }

            throw new InvalidOperationException(
                "Exact C String storage comparisons require a planned operand.");
        }

        private CStringStorageOperand WriteCStringVariableStorageOperand(BoundVariableExpression variable)
        {
            string name = _identifiers.Get(variable.Variable);
            string length = _exactStringLengths!.TryGetValue(variable.Variable, out string? exactLength)
                ? exactLength
                : $"strlen({name})";
            return new CStringStorageOperand(name, length);
        }

        private string WriteCStringRuntimeBuild(
            BoundExpression expression,
            CCodeGenerator.RuntimeStringBuffer buffer)
        {
            string used = buffer.Name + "Used";
            var operations = new List<string> { used + " = 0" };
            foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
            {
                switch (segment)
                {
                    case RuntimeLiteralTextSegment literal:
                        AppendBytes(TargetEscapes.CString(literal.Text),
                            Encoding.UTF8.GetByteCount(literal.Text));
                        break;

                    case RuntimeExpressionTextSegment
                        {
                            Expression: BoundVariableExpression variable
                        } when variable.Variable.Type is SmileType.String:
                        string name = _identifiers.Get(variable.Variable);
                        string length = _exactStringLengths!.TryGetValue(
                            variable.Variable,
                            out string? exactLength)
                            ? exactLength
                            : $"strlen({name})";
                        operations.Add(
                            "(memcpy(" + buffer.Name + " + " + used + ", " + name + ", " +
                            length + "), " + used + " += " + length + ")");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Integer:
                        string integer = WriteExpression(
                            runtime.Expression,
                            parentPrecedence: 0,
                            isRightChild: false,
                            parentOperator: null);
                        string format = _integers.RequiresSigned64Storage ? "%lld" : "%d";
                        string argument = _integers.RequiresSigned64Storage
                            ? "(long long)(" + integer + ")"
                            : integer;
                        operations.Add(
                            used + " += (size_t)snprintf(" + buffer.Name + " + " + used + ", " +
                            (buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture) + " - " + used +
                            ", \"" + format + "\", " + argument + ")");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Boolean:
                        string boolean = WriteExpression(
                            runtime.Expression,
                            parentPrecedence: 0,
                            isRightChild: false,
                            parentOperator: null);
                        operations.Add(
                            "((" + boolean + ") ? " +
                            "(memcpy(" + buffer.Name + " + " + used + ", \"TRUE\", 4), " + used + " += 4) : " +
                            "(memcpy(" + buffer.Name + " + " + used + ", \"FALSE\", 5), " + used + " += 5))");
                        break;
                }
            }

            operations.Add(buffer.Name + "[" + used + "] = '\\0'");
            return "(" + string.Join(", ", operations) + ")";

            void AppendBytes(string value, int byteLength)
            {
                if (byteLength == 0)
                {
                    return;
                }

                operations.Add(
                    "(memcpy(" + buffer.Name + " + " + used + ", " + value + ", " +
                    byteLength.ToString(CultureInfo.InvariantCulture) + "), " + used + " += " +
                    byteLength.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }

        private string WriteCStringEqualityOperand(BoundExpression expression)
        {
            if (expression is BoundStringLiteralExpression or BoundVariableExpression)
            {
                return WriteExpression(expression, 0, isRightChild: false, parentOperator: null);
            }

            if (_values is not null &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
                value.Type is SmileType.String)
            {
                // Statement-order abstract facts may prove a composite operand
                // without selecting a concrete IF path. Keep the generated C
                // compact by rendering that proven value as an ordinary literal.
                return TargetEscapes.CString(value.StringValue);
            }

            throw new InvalidOperationException(
                "A complex C String comparison operand must have runtime storage.");
        }

        private string WriteInterpolatedString(BoundInterpolatedStringExpression expression) =>
            _language switch
            {
                TargetLanguage.CSharp => "$\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.CSharpInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "{" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "\"",

                TargetLanguage.JavaScript => "`" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaScriptTemplateText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "${" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "`",

                TargetLanguage.Java => JoinJavaDisplaySegments(expression.Parts),

                TargetLanguage.Swift => "\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.SwiftInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "\\(" + WriteDisplay(interpolation.Expression) + ")",
                    _ => string.Empty
                })) + "\"",

                _ => EmptyStringLiteral()
            };

        private string JoinJavaDisplaySegments(IReadOnlyList<BoundInterpolatedPart> parts)
        {
            string[] segments = parts
                .Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaString(text.Text),
                    BoundInterpolationExpressionPart interpolation => WriteDisplay(interpolation.Expression),
                    _ => TargetEscapes.JavaString(string.Empty)
                })
                .Where(segment => segment != TargetEscapes.JavaString(string.Empty))
                .ToArray();
            return segments.Length == 0
                ? TargetEscapes.JavaString(string.Empty)
                : string.Join(" + ", segments);
        }

        private string StringLiteral(string value) =>
            _language switch
            {
                TargetLanguage.CSharp => TargetEscapes.CSharpString(value),
                TargetLanguage.JavaScript => TargetEscapes.JavaScriptString(value),
                TargetLanguage.Java => TargetEscapes.JavaString(value),
                TargetLanguage.Swift => TargetEscapes.SwiftString(value),
                _ => TargetEscapes.CString(value)
            };

        private string EmptyStringLiteral() => StringLiteral(string.Empty);

        private static bool ContainsCheckedArithmetic(BoundExpression expression) =>
            expression switch
            {
                BoundUnaryExpression unary =>
                    (unary.Operator.Kind is BoundUnaryOperatorKind.Negation &&
                     unary.Operand.Type is SmileType.Integer) ||
                    ContainsCheckedArithmetic(unary.Operand),
                BoundBinaryExpression binary =>
                    (binary.Left.Type is SmileType.Integer &&
                     binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                         BoundBinaryOperatorKind.Subtraction or
                         BoundBinaryOperatorKind.Multiplication or
                         BoundBinaryOperatorKind.Division) ||
                    ContainsCheckedArithmetic(binary.Left) ||
                    ContainsCheckedArithmetic(binary.Right),
                BoundInterpolatedStringExpression interpolated => interpolated.Parts
                    .OfType<BoundInterpolationExpressionPart>()
                    .Any(part => ContainsCheckedArithmetic(part.Expression)),
                _ => false
            };

        private string IntegerLiteral(long value) =>
            _language switch
            {
                TargetLanguage.CSharp when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "long.MinValue"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.JavaScript when _integers.RequiresJavaScriptBigInt =>
                    value == long.MinValue
                        ? "(-9223372036854775808n)"
                        : value.ToString(CultureInfo.InvariantCulture) + "n",
                TargetLanguage.Java when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "Long.MIN_VALUE"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.Swift when _integers.RequiresSigned64Storage && value == long.MinValue =>
                    "Int64.min",
                TargetLanguage.C or TargetLanguage.ObjectiveC => CIntegerLiteral(value, _integers),
                _ => value.ToString(CultureInfo.InvariantCulture)
            };

        private string BooleanLiteral(bool value) =>
            _language is TargetLanguage.Swift
                ? value ? "true" : "false"
                : value ? "true" : "false";

        private string OperatorText(BoundBinaryOperatorKind kind) =>
            _language switch
            {
                TargetLanguage.JavaScript => kind switch
                {
                    BoundBinaryOperatorKind.Equality => "===",
                    BoundBinaryOperatorKind.Inequality => "!==",
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                TargetLanguage.Swift => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                _ => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                }
            };

        private static string CommonOperatorText(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
                BoundBinaryOperatorKind.Subtraction => "-",
                BoundBinaryOperatorKind.Multiplication => "*",
                BoundBinaryOperatorKind.Division => "/",
                BoundBinaryOperatorKind.Equality => "==",
                BoundBinaryOperatorKind.Inequality => "!=",
                BoundBinaryOperatorKind.Less => "<",
                BoundBinaryOperatorKind.LessOrEquals => "<=",
                BoundBinaryOperatorKind.Greater => ">",
                BoundBinaryOperatorKind.GreaterOrEquals => ">=",
                _ => string.Empty
            };

        private static int Precedence(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals => 4,
                BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
                BoundBinaryOperatorKind.LogicalAnd => 2,
                BoundBinaryOperatorKind.LogicalOr => 1,
                _ => 0
            };

        private static bool NeedsParentheses(
            int precedence,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (parentOperator.HasValue &&
                IsComparison(parentOperator.Value) &&
                precedence is 3 or 4)
            {
                // Swift comparison operators are non-associative, and the
                // grouping is educationally clearer in every destination.
                // Preserve both sides of nested SMILE Boolean comparisons.
                return true;
            }

            if (precedence < parentPrecedence)
            {
                return true;
            }

            return isRightChild &&
                precedence == parentPrecedence &&
                parentOperator is not (
                    BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.StringConcatenation or
                    BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr);
        }

        private static bool IsComparison(BoundBinaryOperatorKind kind) =>
            kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality or
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals;

        private static bool IsSimpleReceiver(BoundExpression expression) =>
            expression is BoundStringLiteralExpression or BoundVariableExpression;

        private static string MaybeParenthesizeForCall(string expression) =>
            IsSimpleCSharpCallReceiver(expression)
                ? expression
                : "(" + expression + ")";

        private static bool IsSimpleCSharpCallReceiver(string expression)
        {
            if (string.IsNullOrEmpty(expression) ||
                !SyntaxFacts.IsIdentifierStart(expression[0]))
            {
                return false;
            }

            // Identifiers and dotted constants such as long.MinValue can receive
            // a method call directly. Operators, negative literals, and grouped
            // expressions are parenthesized before .ToString(...) is appended.
            return expression.All(character =>
                SyntaxFacts.IsIdentifierPart(character) ||
                character == '.');
        }
    }
}
