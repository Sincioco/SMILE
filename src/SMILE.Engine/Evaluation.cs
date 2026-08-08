using System.Globalization;
using System.Text;

namespace SMILE.Engine;

public sealed record SmileRuntimeError(
    string Code,
    string Message)
{
    public override string ToString() => $"SMILE Runtime Error {Code}: {Message}";
}

public sealed record EvaluationResult(
    bool Success,
    string Output,
    IReadOnlyList<Diagnostic> Diagnostics,
    string ErrorOutput = "",
    int ExitCode = 0,
    SmileRuntimeError? RuntimeError = null)
{
    public string StandardOutput => Output;

    public string StandardError => ErrorOutput;
}

public sealed class SmileEvaluator
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly SmileTranspiler _transpiler = new();

    public EvaluationResult Evaluate(string source) =>
        Evaluate(source, TextReader.Null, CancellationToken.None);

    public EvaluationResult Evaluate(
        string source,
        CancellationToken cancellationToken) =>
        Evaluate(source, TextReader.Null, cancellationToken);

    public EvaluationResult Evaluate(string source, string scriptedStandardInput)
        => Evaluate(source, scriptedStandardInput, CancellationToken.None);

    public EvaluationResult Evaluate(
        string source,
        string scriptedStandardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scriptedStandardInput);
        using var input = new StringReader(scriptedStandardInput);
        return Evaluate(source, input, cancellationToken);
    }

    public EvaluationResult Evaluate(string source, Stream utf8StandardInput)
        => Evaluate(source, utf8StandardInput, CancellationToken.None);

    public EvaluationResult Evaluate(
        string source,
        Stream utf8StandardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(utf8StandardInput);
        using var input = new Utf8StreamLineReader(utf8StandardInput);
        return Evaluate(source, input, cancellationToken);
    }

    public EvaluationResult Evaluate(string source, TextReader input)
        => Evaluate(source, input, CancellationToken.None);

    public EvaluationResult Evaluate(
        string source,
        TextReader input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        BindResult bindResult = _transpiler.Bind(source);
        if (!bindResult.Success || bindResult.Program is null)
        {
            return new EvaluationResult(
                Success: false,
                Output: string.Empty,
                Diagnostics: bindResult.Diagnostics,
                ErrorOutput: string.Empty,
                ExitCode: 1);
        }

        var output = new StringBuilder();
        var values = new Dictionary<VariableSymbol, SmileValue>();
        SmileRuntimeError? runtimeError = ExecuteStatements(
            bindResult.Program.Statements,
            values,
            output,
            input,
            cancellationToken);

        if (runtimeError is not null)
        {
            return new EvaluationResult(
                Success: false,
                Output: output.ToString(),
                Diagnostics: bindResult.Diagnostics,
                ErrorOutput: runtimeError + "\n",
                ExitCode: 1,
                RuntimeError: runtimeError);
        }

        return new EvaluationResult(
            Success: true,
            Output: output.ToString(),
            Diagnostics: bindResult.Diagnostics,
            ErrorOutput: string.Empty,
            ExitCode: 0);
    }

    private static SmileRuntimeError? ExecuteStatements(
        IReadOnlyList<BoundStatement> statements,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        TextReader input,
        CancellationToken cancellationToken)
    {
        foreach (BoundStatement statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (statement)
            {
                case BoundLetStatement let:
                    if (!TryEvaluateExpression(
                            let.Initializer,
                            values,
                            out SmileValue initialValue,
                            out SmileRuntimeError? letError))
                    {
                        return letError;
                    }

                    values.Add(let.Variable, initialValue);
                    break;

                case BoundSetStatement set:
                    // Evaluate into a temporary first. The old target value is
                    // visible throughout the complete right side, and the
                    // environment changes only after evaluation succeeds.
                    if (!TryEvaluateExpression(
                            set.Value,
                            values,
                            out SmileValue assignedValue,
                            out SmileRuntimeError? setError))
                    {
                        return setError;
                    }

                    values[set.Variable] = assignedValue;
                    break;

                case BoundInputStatement inputStatement:
                    if (!TryReadInputValue(
                            input,
                            inputStatement.Variable,
                            out SmileValue inputValue,
                            out SmileRuntimeError? inputError))
                    {
                        return inputError;
                    }

                    // Reading, byte validation, and conversion all complete
                    // before the previous value is replaced.
                    values[inputStatement.Variable] = inputValue;
                    break;

                case BoundPrintStatement print:
                    if (!TryEvaluateExpression(
                            print.Value,
                            values,
                            out SmileValue printedValue,
                            out SmileRuntimeError? printError))
                    {
                        return printError;
                    }

                    if (!print.IsBlankLine)
                    {
                        output.Append(printedValue.ToDisplayText());
                    }

                    output.Append('\n');
                    break;

                case BoundIfStatement conditional:
                    SmileRuntimeError? ifError = ExecuteIf(
                        conditional,
                        values,
                        output,
                        input,
                        cancellationToken);
                    if (ifError is not null)
                    {
                        return ifError;
                    }

                    break;

                case BoundWhileStatement loop:
                    SmileRuntimeError? whileError = ExecuteWhile(
                        loop,
                        values,
                        output,
                        input,
                        cancellationToken);
                    if (whileError is not null)
                    {
                        return whileError;
                    }

                    break;
            }
        }

        return null;
    }

    private static SmileRuntimeError? ExecuteIf(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        TextReader input,
        CancellationToken cancellationToken)
    {
        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEvaluateExpression(
                    clause.Condition,
                    values,
                    out SmileValue condition,
                    out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (!condition.BooleanValue)
            {
                continue;
            }

            return ExecuteStatements(
                clause.Statements,
                values,
                output,
                input,
                cancellationToken);
        }

        return conditional.HasElseClause
            ? ExecuteStatements(
                conditional.ElseStatements,
                values,
                output,
                input,
                cancellationToken)
            : null;
    }

    private static SmileRuntimeError? ExecuteWhile(
        BoundWhileStatement loop,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        TextReader input,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            // The host cancellation check is deliberately outside SMILE's
            // runtime-error model. Infinite loops retain their language
            // semantics while Desktop, CLI, and tests can still stop them.
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEvaluateExpression(
                    loop.Condition,
                    values,
                    out SmileValue condition,
                    out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (!condition.BooleanValue)
            {
                return null;
            }

            SmileRuntimeError? bodyError = ExecuteStatements(
                loop.Statements,
                values,
                output,
                input,
                cancellationToken);
            if (bodyError is not null)
            {
                return bodyError;
            }
        }
    }

    private static bool TryEvaluateExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(expression, values);
        if (result.IsKnown && !result.MayFailAtRuntime)
        {
            value = result.Value;
            error = null;
            return true;
        }

        if (result.IsInvalid && result.Error is SmileArithmeticError arithmeticError)
        {
            value = default;
            error = new SmileRuntimeError(
                arithmeticError.RuntimeCode,
                arithmeticError.Message);
            return false;
        }

        // A successfully bound runtime environment contains every declared
        // variable that a reached expression can read. Unknown here therefore
        // signals internal semantic-model corruption, not a learner error.
        throw new InvalidOperationException(
            "A reached bound expression remained Unknown during runtime evaluation.");
    }

    private static bool TryReadInputValue(
        TextReader input,
        VariableSymbol variable,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        string? line;
        try
        {
            line = input.ReadLine();
        }
        catch (Exception exception) when (
            exception is IOException or
            DecoderFallbackException or
            ObjectDisposedException or
            InvalidOperationException or
            NotSupportedException)
        {
            value = default;
            error = InputError(
                "SMILER1506",
                $"Input for '{variable.Name}' could not be read as valid UTF-8 text.");
            return false;
        }

        if (line is null)
        {
            value = default;
            error = InputError(
                "SMILER1501",
                $"Input ended before a value was received for '{variable.Name}'.");
            return false;
        }

        return TryConvertInput(line, variable, out value, out error);
    }

    private static bool TryConvertInput(
        string line,
        VariableSymbol variable,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        switch (variable.Type)
        {
            case SmileType.String:
                value = SmileValue.FromString(line);
                error = null;
                return true;

            case SmileType.Integer:
                string integerText = TrimAsciiHorizontalWhitespace(line);
                if (!HasInputIntegerGrammar(integerText))
                {
                    value = default;
                    error = InputError(
                        "SMILER1503",
                        $"Input for '{variable.Name}' is not a valid Integer.");
                    return false;
                }

                if (!long.TryParse(
                        integerText,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out long integer))
                {
                    value = default;
                    error = InputError(
                        "SMILER1504",
                        $"Input for '{variable.Name}' is outside the signed 64-bit Integer range.");
                    return false;
                }

                value = SmileValue.FromInteger(integer);
                error = null;
                return true;

            case SmileType.Boolean:
                string booleanText = TrimAsciiHorizontalWhitespace(line);
                if (booleanText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    value = SmileValue.FromBoolean(true);
                    error = null;
                    return true;
                }

                if (booleanText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    value = SmileValue.FromBoolean(false);
                    error = null;
                    return true;
                }

                value = default;
                error = InputError(
                    "SMILER1505",
                    $"Input for '{variable.Name}' must be TRUE or FALSE.");
                return false;

            default:
                value = default;
                error = InputError(
                    "SMILER1506",
                    $"Input for '{variable.Name}' could not be read as valid UTF-8 text.");
                return false;
        }
    }

    private static string TrimAsciiHorizontalWhitespace(string text)
    {
        int start = 0;
        while (start < text.Length && text[start] is ' ' or '\t')
        {
            start++;
        }

        int end = text.Length;
        while (end > start && text[end - 1] is ' ' or '\t')
        {
            end--;
        }

        return text[start..end];
    }

    private static bool HasInputIntegerGrammar(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        int position = text[0] is '+' or '-' ? 1 : 0;
        if (position >= text.Length)
        {
            return false;
        }

        for (; position < text.Length; position++)
        {
            if (text[position] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static SmileRuntimeError InputError(string code, string message) =>
        new(code, message);

    // StreamReader may decode bytes from a later line while satisfying the
    // current ReadLine call. Reading one raw logical line at a time keeps a
    // malformed future line from failing an earlier INPUT and preserves exact
    // CRLF, LF, standalone-CR, EOF, and embedded-NUL behavior.
    private sealed class Utf8StreamLineReader : TextReader
    {
        private readonly Stream _stream;
        private bool _skipLeadingLineFeed;

        public Utf8StreamLineReader(Stream stream)
        {
            _stream = stream;
        }

        public override string? ReadLine()
        {
            var bytes = new List<byte>();
            while (true)
            {
                int next = _stream.ReadByte();
                if (_skipLeadingLineFeed)
                {
                    _skipLeadingLineFeed = false;
                    if (next == '\n')
                    {
                        continue;
                    }
                }

                if (next < 0)
                {
                    return bytes.Count == 0
                        ? null
                        : StrictUtf8.GetString(bytes.ToArray());
                }

                if (next == '\n')
                {
                    return StrictUtf8.GetString(bytes.ToArray());
                }

                if (next == '\r')
                {
                    // A standalone CR completes the current logical line
                    // immediately. Delay the optional LF check until the next
                    // INPUT so a future byte cannot block or fail this line.
                    _skipLeadingLineFeed = true;
                    return StrictUtf8.GetString(bytes.ToArray());
                }

                bytes.Add((byte)next);
            }
        }
    }
}
