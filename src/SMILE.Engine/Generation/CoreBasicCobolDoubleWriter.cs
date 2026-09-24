namespace SMILE.Engine;

internal sealed partial class CobolWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteDoubleSelectCases(IReadOnlyList<BoundSelectCaseClause> clauses, int index, Temporary selector, int indent)
        {
            if (index >= clauses.Count) return;
            BoundSelectCaseClause clause = clauses[index];
            if (clause.IsElse) { WriteItems(clause.SourceItems, indent); return; }
            string value = PrepareDoubleLiteral(clause.Value!.Value.DoubleValue, indent);
            Temporary equal = NewTemporary(SmileType.Boolean);
            Line(indent, $"CALL \"smile_dbl_equality_cobol\" USING BY REFERENCE {selector.Name} {value} {equal.Name} BY VALUE 0");
            Line(indent, $"IF {equal.Name} = 1");
            WriteItems(clause.SourceItems, indent + 1);
            if (index + 1 < clauses.Count)
            {
                Line(indent, "ELSE");
                WriteDoubleSelectCases(clauses, index + 1, selector, indent + 1);
            }
            Line(indent, "END-IF");
        }

        private string PrepareDoubleLiteral(double value, int indent)
        {
            string literal = DoubleSemantics.FormatLiteral(value);
            Temporary result = NewTemporary(SmileType.Double);
            Line(indent, $"CALL \"smile_dbl_literal_cobol\" USING BY REFERENCE Z\"{literal}\" {result.Name}");
            return result.Name;
        }

        private Temporary CaptureDouble(BoundExpression expression, int indent)
        {
            string value = PrepareExpression(expression, indent);
            Temporary captured = NewTemporary(expression.Type);
            Assign(captured.Name, expression.Type, expression, value, indent,
                expression.Type is { Kind: SmileTypeKind.String } ? LengthName(captured) : null);
            return captured;
        }

        private string PrepareDoubleBinary(BoundBinaryExpression binary, int indent)
        {
            Temporary left = CaptureDouble(binary.Left, indent);
            Temporary right = CaptureDouble(binary.Right, indent);
            Temporary result = NewTemporary(binary.Type);
            string operation = binary.Operator.Kind.ToString().ToLowerInvariant();
            Line(indent, $"CALL \"smile_dbl_{operation}_cobol\" USING BY REFERENCE {left.Name} {right.Name} {result.Name} BY VALUE {binary.OperatorSpan.Line}");
            return binary.Type is { Kind: SmileTypeKind.Boolean } ? $"({result.Name} = 1)" : result.Name;
        }

        private string PrepareDoubleNegation(BoundUnaryExpression unary, int indent)
        {
            Temporary value = CaptureDouble(unary.Operand, indent);
            Temporary result = NewTemporary(SmileType.Double);
            Line(indent, $"CALL \"smile_dbl_negate_cobol\" USING BY REFERENCE {value.Name} {result.Name}");
            return result.Name;
        }

        private string PrepareDoubleIntrinsic(BoundIntrinsicExpression intrinsic, int indent)
        {
            Temporary[] arguments = intrinsic.Arguments.Select(argument => CaptureDouble(argument, indent)).ToArray();
            Temporary result = NewTemporary(intrinsic.Type);
            var parameters = new List<string>();
            foreach (Temporary argument in arguments)
            {
                parameters.Add(argument.Name);
                if (argument.Type is { Kind: SmileTypeKind.String }) parameters.Add(LengthName(argument));
            }
            parameters.Add(result.Name);
            if (intrinsic.Type is { Kind: SmileTypeKind.String })
            {
                parameters.Add(LengthName(result));
                _preparedTextLengths[intrinsic] = LengthName(result);
            }
            string name = intrinsic.Kind.ToString().ToLowerInvariant();
            Line(indent, $"CALL \"smile_dbl_{name}_cobol\" USING BY REFERENCE {string.Join(" ", parameters)} BY VALUE {intrinsic.Span.Line}");
            return result.Name;
        }

        private void PrintDouble(BoundExpression expression, string value, int indent)
        {
            Temporary captured = NewTemporary(SmileType.Double);
            Assign(captured.Name, SmileType.Double, expression, value, indent);
            Temporary text = NewTemporary(SmileType.String);
            Line(indent, $"CALL \"smile_dbl_textfromdouble_cobol\" USING BY REFERENCE {captured.Name} {text.Name} {LengthName(text)} BY VALUE 0");
            Line(indent, $"DISPLAY {text.Name}(1:{LengthName(text)}) WITH NO ADVANCING");
        }
    }
}
