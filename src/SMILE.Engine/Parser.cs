using System.Globalization;
using System.Text;

namespace SMILE.Engine;

// SMILE has one source front end. Tokenization lives beside parsing so every
// public entry point receives the same syntax tree and canonical diagnostics.
internal sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics = new();
    private int _position;
    private int _delimiterDepth;
    private int _routineDepth;

    public Parser(string source)
    {
        var lexer = new Lexer(source);
        _tokens = lexer.LexAll();
        _diagnostics.AddRange(lexer.Diagnostics);
    }

    public ParseResult Parse()
    {
        IReadOnlyList<SourceItemSyntax> items = ParseItems(() => Current.Kind is TokenKind.EndOfFile);
        TextSpan span = items.Count == 0
            ? Current.Span
            : Combine(items[0].Span, items[^1].Span);
        return new ParseResult(new SmileProgramSyntax(items, span), _diagnostics);
    }

    private IReadOnlyList<SourceItemSyntax> ParseItems(Func<bool> atTerminator)
    {
        var items = new List<SourceItemSyntax>();
        while (!atTerminator() && Current.Kind is not TokenKind.EndOfFile)
        {
            if (Current.Kind is TokenKind.EndOfLine)
            {
                items.Add(new BlankLineSyntax(Next().Span));
                continue;
            }

            if (Current.Kind is TokenKind.Comment)
            {
                Token comment = Next();
                items.Add(new FullLineCommentSyntax(
                    FullLineCommentMarker.Apostrophe,
                    (string?)comment.Value ?? string.Empty,
                    comment.Span));
                ConsumeLineEnd();
                continue;
            }

            int start = _position;
            StatementSyntax? statement = ParseStatement();
            if (statement is not null)
            {
                items.Add(statement);
            }

            if (_position == start)
            {
                Report("SMILE2001", "Expected a Core BASIC statement.", Current.Span);
                Next();
            }

            ConsumeStatementEnd();
        }

        return items;
    }

    private StatementSyntax? ParseStatement() => Current.Kind switch
    {
        TokenKind.Option => ParseOptionExplicit(),
        TokenKind.Identifier => ParseAssignment(),
        TokenKind.Dim => ParseDim(),
        TokenKind.Const => ParseConst(),
        TokenKind.Sub => ParseRoutine(RoutineKind.Sub),
        TokenKind.Function => ParseRoutine(RoutineKind.Function),
        TokenKind.Call => ParseCallStatement(),
        TokenKind.Return => ParseReturn(),
        TokenKind.Select => ParseSelect(),
        TokenKind.Print => ParsePrint(),
        TokenKind.Get => ParseGetKey(),
        TokenKind.Clear => ParseClearScreen(),
        TokenKind.Wait => ParseWait(),
        TokenKind.Random => ParseRandom(),
        TokenKind.If => ParseIf(),
        TokenKind.For => ParseFor(),
        TokenKind.Do => ParseDo(),
        TokenKind.Exit => ParseExit(),
        TokenKind.End => ParseEndProgram(),
        TokenKind.UnsupportedKeyword or TokenKind.ByRef or TokenKind.Optional => ParseUnsupported(),
        _ => ParseUnexpectedStatement()
    };

    private StatementSyntax ParseOptionExplicit()
    {
        Token start = Next();
        Token explicitToken = Match(TokenKind.Explicit, "Expected 'Explicit' after 'Option'.");
        return new OptionExplicitStatementSyntax(Combine(start.Span, explicitToken.Span));
    }

    private StatementSyntax ParseAssignment()
    {
        Token name = Next();
        if (Current.Kind is TokenKind.OpenBracket)
        {
            IReadOnlyList<ExpressionSyntax> indices = ParseBracketExpressionList("array index");
            Token close = Match(TokenKind.CloseBracket, "Expected ']' after the array index.");
            Match(TokenKind.Equals, "Expected '=' after the array element target.");
            ExpressionSyntax value = ParseExpression();
            return new CoreArrayAssignmentStatementSyntax(
                name.Text,
                name.Span,
                indices,
                value,
                Combine(name.Span, value.Span.Length == 0 ? close.Span : value.Span));
        }

        Match(TokenKind.Equals, "Expected '=' after the assignment target.");
        ExpressionSyntax scalarValue = ParseExpression();
        return new CoreAssignmentStatementSyntax(
            name.Text,
            name.Span,
            scalarValue,
            Combine(name.Span, scalarValue.Span));
    }

    private StatementSyntax ParseDim()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected an identifier after 'Dim'.");
        IReadOnlyList<ExpressionSyntax> arraySizes = Array.Empty<ExpressionSyntax>();
        TextSpan endSpan = name.Span;
        if (Current.Kind is TokenKind.OpenBracket)
        {
            arraySizes = ParseBracketExpressionList("array dimension");
            Token close = Match(TokenKind.CloseBracket, "Expected ']' after the array size.");
            endSpan = close.Span;
        }

        Match(TokenKind.As, "Declarations require 'As Number', 'As Boolean', or 'As Text'.");
        Token type = ParseType("Expected Number, Boolean, or Text after 'As'.");
        SmileType declaredType = ToSmileType(type.Kind);
        endSpan = type.Span.Length == 0 ? endSpan : type.Span;
        return new DimStatementSyntax(name.Text, name.Span, declaredType, arraySizes, Combine(start.Span, endSpan));
    }

    private StatementSyntax ParseConst()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected an identifier after 'Const'.");
        Match(TokenKind.Equals, "Expected '=' in a constant declaration.");
        ExpressionSyntax initializer = ParseExpression();
        return new ConstStatementSyntax(
            name.Text,
            name.Span,
            initializer,
            Combine(start.Span, initializer.Span));
    }

    private StatementSyntax ParseRoutine(RoutineKind kind)
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, $"Expected a name after '{start.Text}'.");
        Match(TokenKind.OpenParenthesis, "Routine declarations require '('.");
        _delimiterDepth++;
        var parameters = new List<ParameterSyntax>();
        if (Current.Kind is not TokenKind.CloseParenthesis)
        {
            while (true)
            {
                Token parameterStart = Current;
                bool explicitByVal = false;
                if (Current.Kind is TokenKind.ByVal)
                {
                    explicitByVal = true;
                    parameterStart = Next();
                }
                else if (Current.Kind is TokenKind.ByRef)
                {
                    parameterStart = Next();
                    Report("SMILE2011", "Core BASIC 2 parameters are ByVal; ByRef is not supported.", parameterStart.Span);
                }
                else if (Current.Kind is TokenKind.Optional)
                {
                    parameterStart = Next();
                    Report("SMILE2012", "Core BASIC 2 does not support Optional parameters.", parameterStart.Span);
                }

                Token parameterName = Match(TokenKind.Identifier, "Expected a parameter name.");
                Match(TokenKind.As, "Typed parameters require 'As Number', 'As Boolean', or 'As Text'.");
                Token type = ParseType("Expected Number, Boolean, or Text for the parameter type.");
                parameters.Add(new ParameterSyntax(
                    parameterName.Text,
                    parameterName.Span,
                    ToSmileType(type.Kind),
                    explicitByVal,
                    Combine(parameterStart.Span, type.Span)));

                if (Current.Kind is not TokenKind.Comma)
                {
                    break;
                }

                Next();
            }
        }

        Token close = Match(TokenKind.CloseParenthesis, "Expected ')' after the parameter list.");
        _delimiterDepth--;
        SmileType? returnType = null;
        TextSpan headerEnd = close.Span;
        if (kind is RoutineKind.Function)
        {
            Match(TokenKind.As, "Function declarations require a return type after 'As'.");
            Token type = ParseType("Expected Number, Boolean, or Text for the Function return type.");
            returnType = ToSmileType(type.Kind);
            headerEnd = type.Span;
        }

        ConsumeRequiredLineEnd($"The {kind} header must end after its declaration.");
        _routineDepth++;
        IReadOnlyList<SourceItemSyntax> body = ParseItems(() => IsRoutineTerminator(kind) || Current.Kind is TokenKind.Sub or TokenKind.Function);
        _routineDepth--;

        TextSpan endSpan = LastSpan(body, headerEnd);
        if (IsRoutineTerminator(kind))
        {
            Token end = Next();
            Token closeKind = Next();
            endSpan = closeKind.Span.Length == 0 ? end.Span : closeKind.Span;
        }
        else
        {
            Report("SMILE2013", $"Routines cannot be nested; expected 'End {kind}' before the next routine declaration or end of file.", Current.Span);
        }

        return new RoutineDeclarationSyntax(kind, name.Text, name.Span, parameters, returnType, body, Combine(start.Span, endSpan));
    }

    private bool IsRoutineTerminator(RoutineKind kind) =>
        Current.Kind is TokenKind.End && Peek(1).Kind == (kind is RoutineKind.Sub ? TokenKind.Sub : TokenKind.Function);

    private StatementSyntax ParseCallStatement()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected a Sub name after 'Call'.");
        IReadOnlyList<ExpressionSyntax> arguments = ParseArgumentList("Call statements require a parenthesized argument list.");
        TextSpan end = arguments.Count > 0 ? arguments[^1].Span : Previous.Span;
        return new CallStatementSyntax(name.Text, name.Span, arguments, Combine(start.Span, end));
    }

    private StatementSyntax ParseReturn()
    {
        Token start = Next();
        ExpressionSyntax? value = AtLineEnd() ? null : ParseExpression();
        return new ReturnStatementSyntax(value, Combine(start.Span, value?.Span ?? start.Span));
    }

    private StatementSyntax ParseSelect()
    {
        Token start = Next();
        Match(TokenKind.Case, "Expected 'Case' after 'Select'.");
        ExpressionSyntax selector = ParseExpression();
        ConsumeRequiredLineEnd("The Select Case header must end after its selector.");

        // This deliberately follows the pinned SMILE 2.0 parser: the first Case
        // follows the header directly. Recover after diagnosing blank lines.
        while (Current.Kind is TokenKind.EndOfLine)
        {
            Report("SMILE2014", "Expected 'Case' directly after the Select Case header.", Current.Span);
            Next();
        }

        var clauses = new List<SelectCaseClauseSyntax>();
        while (Current.Kind is TokenKind.Case)
        {
            Token caseToken = Next();
            bool isElse = false;
            ExpressionSyntax? value = null;
            if (Current.Kind is TokenKind.Else)
            {
                isElse = true;
                Next();
            }
            else
            {
                value = ParseExpression();
            }

            ConsumeRequiredLineEnd("The Case header must end after its value.");
            IReadOnlyList<SourceItemSyntax> body = ParseItems(() =>
                Current.Kind is TokenKind.Case ||
                (Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Select));
            TextSpan clauseEnd = LastSpan(body, value?.Span ?? caseToken.Span);
            clauses.Add(new SelectCaseClauseSyntax(value, isElse, body, Combine(caseToken.Span, clauseEnd)));
        }

        Token end = Match(TokenKind.End, "Expected 'End Select'.");
        Token select = Match(TokenKind.Select, "Expected 'Select' after 'End'.");
        return new SelectStatementSyntax(selector, clauses, Combine(start.Span, select.Span.Length == 0 ? end.Span : select.Span));
    }

    private StatementSyntax ParsePrint()
    {
        Token start = Next();
        var values = new List<ExpressionSyntax>();
        bool suppressNewLine = false;
        if (!AtLineEnd())
        {
            values.Add(ParseExpression());
            while (Current.Kind is TokenKind.Semicolon)
            {
                Next();
                suppressNewLine = AtLineEnd();
                if (!suppressNewLine)
                {
                    values.Add(ParseExpression());
                }
            }
        }

        TextSpan end = values.Count > 0 ? values[^1].Span : start.Span;
        return new CorePrintStatementSyntax(values, suppressNewLine, Combine(start.Span, end));
    }

    private StatementSyntax ParseGetKey()
    {
        Token start = Next();
        Match(TokenKind.Key, "Expected 'Key' after 'Get'.");
        Token target = Match(TokenKind.Identifier, "Get Key requires a writable Number variable.");
        return new GetKeyStatementSyntax(target.Text, target.Span, Combine(start.Span, target.Span));
    }

    private StatementSyntax ParseClearScreen()
    {
        Token start = Next();
        Token screen = Match(TokenKind.Screen, "Expected 'Screen' after 'Clear'.");
        return new ClearScreenStatementSyntax(Combine(start.Span, screen.Span));
    }

    private StatementSyntax ParseWait()
    {
        Token start = Next();
        ExpressionSyntax duration = ParseExpression();
        Token milliseconds = Match(TokenKind.Milliseconds, "Wait requires the 'Milliseconds' unit.");
        return new WaitStatementSyntax(duration, Combine(start.Span, milliseconds.Span));
    }

    private StatementSyntax ParseRandom()
    {
        Token start = Next();
        Token target = Match(TokenKind.Identifier, "Random requires a writable Number variable.");
        Match(TokenKind.From, "Expected 'From' after the Random target.");
        ExpressionSyntax lower = ParseExpression();
        Match(TokenKind.To, "Expected 'To' between Random bounds.");
        ExpressionSyntax upper = ParseExpression();
        return new RandomStatementSyntax(
            target.Text,
            target.Span,
            lower,
            upper,
            Combine(start.Span, upper.Span));
    }

    private StatementSyntax ParseIf()
    {
        Token start = Next();
        var clauses = new List<ConditionalClauseSyntax>();
        ExpressionSyntax firstCondition = ParseExpression();
        Match(TokenKind.Then, "Expected 'Then' after the IF condition.");
        ConsumeRequiredLineEnd("The IF header must end after 'Then'.");

        IReadOnlyList<SourceItemSyntax> firstBody = ParseItems(IsIfTerminator);
        clauses.Add(new ConditionalClauseSyntax(firstCondition, firstBody, Combine(firstCondition.Span, LastSpan(firstBody, firstCondition.Span))));

        while (Current.Kind is TokenKind.Else && Peek(1).Kind is TokenKind.If)
        {
            Token elseToken = Next();
            Next();
            ExpressionSyntax condition = ParseExpression();
            Match(TokenKind.Then, "Expected 'Then' after the ELSE IF condition.");
            ConsumeRequiredLineEnd("The ELSE IF header must end after 'Then'.");
            IReadOnlyList<SourceItemSyntax> body = ParseItems(IsIfTerminator);
            clauses.Add(new ConditionalClauseSyntax(condition, body, Combine(elseToken.Span, LastSpan(body, condition.Span))));
        }

        bool hasElse = false;
        IReadOnlyList<SourceItemSyntax> elseItems = Array.Empty<SourceItemSyntax>();
        if (Current.Kind is TokenKind.Else)
        {
            hasElse = true;
            Next();
            ConsumeRequiredLineEnd("The ELSE line cannot contain another statement.");
            elseItems = ParseItems(() => Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.If);
        }

        Token end = Match(TokenKind.End, "Expected 'End If'.");
        Token ifToken = Match(TokenKind.If, "Expected 'If' after 'End'.");
        return new IfStatementSyntax(clauses, elseItems, hasElse, Combine(start.Span, ifToken.Span.Length == 0 ? end.Span : ifToken.Span));
    }

    private bool IsIfTerminator() =>
        Current.Kind is TokenKind.Else ||
        (Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.If);

    private StatementSyntax ParseFor()
    {
        Token start = Next();
        Token counter = Match(TokenKind.Identifier, "Expected a FOR counter identifier.");
        Match(TokenKind.Equals, "Expected '=' after the FOR counter.");
        ExpressionSyntax lower = ParseExpression();
        bool descending = Current.Kind is TokenKind.Down;
        if (descending)
        {
            Next();
        }

        Match(TokenKind.To, "Expected 'To' or 'Down To' in the FOR header.");
        ExpressionSyntax upper = ParseExpression();
        ConsumeRequiredLineEnd("The FOR header must end after its upper bound.");
        IReadOnlyList<SourceItemSyntax> body = ParseItems(() => Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.For);
        Match(TokenKind.End, "Expected 'End For'.");
        Token endFor = Match(TokenKind.For, "Expected 'For' after 'End'.");
        return new ForStatementSyntax(
            counter.Text,
            counter.Span,
            lower,
            upper,
            descending,
            body,
            Combine(start.Span, endFor.Span));
    }

    private StatementSyntax ParseDo()
    {
        Token start = Next();
        ConsumeRequiredLineEnd("'Do' must appear alone on its line.");
        IReadOnlyList<SourceItemSyntax> body = ParseItems(() => Current.Kind is TokenKind.Loop);
        Token loop = Match(TokenKind.Loop, "Expected 'Loop' to close the DO block.");
        ExpressionSyntax? until = null;
        if (Current.Kind is TokenKind.Until)
        {
            Next();
            until = ParseExpression();
        }

        return new DoStatementSyntax(body, until, Combine(start.Span, until?.Span ?? loop.Span));
    }

    private StatementSyntax ParseExit()
    {
        Token start = Next();
        if (Current.Kind is TokenKind.For)
        {
            Token end = Next();
            return new ExitStatementSyntax(ExitStatementKind.For, Combine(start.Span, end.Span));
        }

        Token doToken = Match(TokenKind.Do, "Expected 'For' or 'Do' after 'Exit'.");
        return new ExitStatementSyntax(ExitStatementKind.Do, Combine(start.Span, doToken.Span));
    }

    private StatementSyntax ParseEndProgram()
    {
        Token start = Next();
        Token program = Match(TokenKind.Program, "Expected 'Program' after 'End' in this context.");
        return new EndProgramStatementSyntax(Combine(start.Span, program.Span));
    }

    private StatementSyntax? ParseUnsupported()
    {
        Token token = Next();
        string message = token.Kind switch
        {
            TokenKind.ByRef => "Core BASIC 2 parameters are ByVal; ByRef is not supported.",
            TokenKind.Optional => "Core BASIC 2 does not support Optional parameters.",
            _ => $"'{token.Text}' is outside the Core BASIC 2 profile."
        };
        Report("SMILE2002", message, token.Span);
        SkipToLineEnd();
        return null;
    }

    private StatementSyntax? ParseUnexpectedStatement()
    {
        Report("SMILE2001", "Expected a Core BASIC 2 statement.", Current.Span);
        SkipToLineEnd();
        return null;
    }

    private ExpressionSyntax ParseExpression(int parentPrecedence = 0)
    {
        SkipExpressionContinuations();
        ExpressionSyntax left;
        int unaryPrecedence = GetUnaryPrecedence(Current.Kind);
        if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
        {
            Token op = Next();
            ExpressionSyntax operand = ParseExpression(unaryPrecedence);
            left = new UnaryExpressionSyntax(ToSyntaxToken(op), operand, Combine(op.Span, operand.Span));
        }
        else
        {
            left = ParsePrimaryExpression();
        }

        while (true)
        {
            SkipExpressionContinuations();
            int precedence = GetBinaryPrecedence(Current.Kind);
            if (precedence == 0 || precedence <= parentPrecedence)
            {
                break;
            }

            Token op = Next();
            ExpressionSyntax right = ParseExpression(precedence);
            left = new BinaryExpressionSyntax(left, ToSyntaxToken(op), right, Combine(left.Span, right.Span));
        }

        return left;
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        SkipExpressionContinuations();
        Token token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Next();
                return new IntegerLiteralExpressionSyntax(token.Text, token.Span);
            case TokenKind.String:
                Next();
                return new StringLiteralExpressionSyntax((string?)token.Value ?? string.Empty, token.Span);
            case TokenKind.True:
            case TokenKind.False:
                Next();
                return new BooleanLiteralExpressionSyntax(token.Kind is TokenKind.True, token.Span);
            case TokenKind.BuiltInConstant:
                Next();
                return new IntegerLiteralExpressionSyntax(
                    Convert.ToInt64(token.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                    token.Span);
            case TokenKind.Identifier:
            case TokenKind.BuiltInFunction:
                Next();
                if (Current.Kind is TokenKind.OpenParenthesis)
                {
                    IReadOnlyList<ExpressionSyntax> arguments = ParseArgumentList("Function calls require a parenthesized argument list.");
                    TextSpan end = arguments.Count > 0 ? arguments[^1].Span : Previous.Span;
                    return new CallExpressionSyntax(token.Text, token.Span, arguments, Combine(token.Span, end));
                }

                if (Current.Kind is TokenKind.OpenBracket)
                {
                    IReadOnlyList<ExpressionSyntax> indices = ParseBracketExpressionList("array index");
                    Token close = Match(TokenKind.CloseBracket, "Expected ']' after the array index.");
                    return new ArrayAccessExpressionSyntax(token.Text, token.Span, indices, Combine(token.Span, close.Span));
                }

                if (token.Kind is TokenKind.BuiltInFunction)
                {
                    Report("SMILE2017", $"Built-in function '{token.Text}' requires parentheses.", token.Span);
                    return new ErrorExpressionSyntax(token.Span);
                }

                return new NameExpressionSyntax(token.Text, token.Span);
            case TokenKind.OpenParenthesis:
                Token open = Next();
                _delimiterDepth++;
                ExpressionSyntax inner = ParseExpression();
                Token closeParenthesis = Match(TokenKind.CloseParenthesis, "Expected ')' to close the expression.");
                _delimiterDepth--;
                return new ParenthesizedExpressionSyntax(
                    ToSyntaxToken(open),
                    inner,
                    ToSyntaxToken(closeParenthesis),
                    Combine(open.Span, closeParenthesis.Span));
            case TokenKind.UnsupportedKeyword:
            case TokenKind.ByRef:
            case TokenKind.Optional:
                Next();
                Report("SMILE2002", $"'{token.Text}' is reserved and outside the Core BASIC 2 profile.", token.Span);
                return new ErrorExpressionSyntax(token.Span);
            default:
                Next();
                Report("SMILE2003", "Expected a Number, Boolean, Text, identifier, call, array access, or parenthesized expression.", token.Span);
                return new ErrorExpressionSyntax(token.Span);
        }
    }

    private IReadOnlyList<ExpressionSyntax> ParseBracketExpressionList(string itemName)
    {
        Match(TokenKind.OpenBracket, $"Expected '[' before the {itemName}.");
        var expressions = new List<ExpressionSyntax>();
        if (Current.Kind is TokenKind.CloseBracket)
        {
            Report("SMILE2010", $"An {itemName} list cannot be empty.", Current.Span);
            return expressions;
        }

        while (true)
        {
            if (AtLineEnd() || Current.Kind is TokenKind.CloseBracket)
            {
                Report("SMILE2010", $"Expected an expression for the {itemName}.", Current.Span);
                break;
            }

            expressions.Add(ParseExpression());
            if (Current.Kind is not TokenKind.Comma)
            {
                break;
            }

            Token comma = Next();
            if (expressions.Count == 2)
            {
                Report("SMILE2010", "Arrays support at most two dimensions and indexes.", comma.Span);
            }
        }

        return expressions;
    }

    private IReadOnlyList<ExpressionSyntax> ParseArgumentList(string openMessage)
    {
        Match(TokenKind.OpenParenthesis, openMessage);
        _delimiterDepth++;
        var arguments = new List<ExpressionSyntax>();
        if (Current.Kind is not TokenKind.CloseParenthesis)
        {
            while (true)
            {
                if (Current.Kind is TokenKind.Identifier && Peek(1).Kind is TokenKind.ColonEquals)
                {
                    Token name = Next();
                    Next();
                    Report("SMILE2015", "Core BASIC 2 does not support named arguments.", name.Span);
                }

                arguments.Add(ParseExpression());
                if (Current.Kind is not TokenKind.Comma)
                {
                    break;
                }

                Next();
            }
        }

        Match(TokenKind.CloseParenthesis, "Expected ')' after the argument list.");
        _delimiterDepth--;
        return arguments;
    }

    private void SkipExpressionContinuations()
    {
        if (_delimiterDepth == 0)
        {
            return;
        }

        while (Current.Kind is TokenKind.EndOfLine or TokenKind.Comment)
        {
            Next();
        }
    }

    private static int GetUnaryPrecedence(TokenKind kind) => kind switch
    {
        TokenKind.Minus or TokenKind.Not => 8,
        _ => 0
    };

    private static int GetBinaryPrecedence(TokenKind kind) => kind switch
    {
        TokenKind.Star or TokenKind.Slash or TokenKind.Mod => 7,
        TokenKind.Plus or TokenKind.Minus => 6,
        TokenKind.Less or TokenKind.LessOrEquals or TokenKind.Greater or TokenKind.GreaterOrEquals => 5,
        TokenKind.Equals or TokenKind.NotEquals => 4,
        TokenKind.And => 3,
        TokenKind.Or => 2,
        _ => 0
    };

    private static SyntaxToken ToSyntaxToken(Token token)
    {
        SyntaxKind kind = token.Kind switch
        {
            TokenKind.Plus => SyntaxKind.PlusToken,
            TokenKind.Minus => SyntaxKind.MinusToken,
            TokenKind.Star => SyntaxKind.StarToken,
            TokenKind.Slash => SyntaxKind.SlashToken,
            TokenKind.Mod => SyntaxKind.ModKeyword,
            TokenKind.Equals => SyntaxKind.EqualsToken,
            TokenKind.NotEquals => SyntaxKind.NotEqualsToken,
            TokenKind.Less => SyntaxKind.LessToken,
            TokenKind.LessOrEquals => SyntaxKind.LessOrEqualsToken,
            TokenKind.Greater => SyntaxKind.GreaterToken,
            TokenKind.GreaterOrEquals => SyntaxKind.GreaterOrEqualsToken,
            TokenKind.Not => SyntaxKind.NotKeyword,
            TokenKind.And => SyntaxKind.AndKeyword,
            TokenKind.Or => SyntaxKind.OrKeyword,
            TokenKind.OpenParenthesis => SyntaxKind.OpenParenthesisToken,
            TokenKind.CloseParenthesis => SyntaxKind.CloseParenthesisToken,
            _ => SyntaxKind.BadToken
        };
        return new SyntaxToken(kind, token.Text, token.Value, token.Span);
    }

    private Token ParseType(string message) =>
        Current.Kind is TokenKind.NumberType or TokenKind.BooleanType or TokenKind.TextType
            ? Next()
            : Match(TokenKind.NumberType, message);

    private static SmileType ToSmileType(TokenKind kind) => kind switch
    {
        TokenKind.BooleanType => SmileType.Boolean,
        TokenKind.TextType => SmileType.String,
        _ => SmileType.Integer
    };

    private bool AtLineEnd() => Current.Kind is TokenKind.EndOfLine or TokenKind.EndOfFile or TokenKind.Comment;

    private void ConsumeStatementEnd()
    {
        if (Current.Kind is TokenKind.Comment)
        {
            Next();
        }

        if (Current.Kind is TokenKind.EndOfLine)
        {
            Next();
            return;
        }

        if (Current.Kind is not TokenKind.EndOfFile)
        {
            Report("SMILE2004", $"Unexpected token '{Current.Text}' after the statement.", Current.Span);
            SkipToLineEnd();
            ConsumeLineEnd();
        }
    }

    private void ConsumeRequiredLineEnd(string message)
    {
        if (Current.Kind is TokenKind.Comment)
        {
            Next();
        }

        if (Current.Kind is TokenKind.EndOfLine)
        {
            Next();
            return;
        }

        if (Current.Kind is not TokenKind.EndOfFile)
        {
            Report("SMILE2004", message, Current.Span);
            SkipToLineEnd();
            ConsumeLineEnd();
        }
    }

    private void ConsumeLineEnd()
    {
        if (Current.Kind is TokenKind.EndOfLine)
        {
            Next();
        }
    }

    private void SkipToLineEnd()
    {
        while (Current.Kind is not TokenKind.EndOfLine and not TokenKind.EndOfFile)
        {
            Next();
        }
    }

    private Token Match(TokenKind kind, string message)
    {
        if (Current.Kind == kind)
        {
            return Next();
        }

        Report("SMILE2005", message, Current.Span);
        return new Token(kind, string.Empty, null, new TextSpan(Current.Span.Start, 0, Current.Span.Line, Current.Span.Column));
    }

    private Token Current => Peek(0);
    private Token Previous => Peek(-1);

    private Token Peek(int offset)
    {
        int index = Math.Clamp(_position + offset, 0, _tokens.Count - 1);
        return _tokens[index];
    }

    private Token Next()
    {
        Token token = Current;
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }

        return token;
    }

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));

    private static TextSpan LastSpan(IReadOnlyList<SourceItemSyntax> items, TextSpan fallback) =>
        items.Count == 0 ? fallback : items[^1].Span;

    private static TextSpan Combine(TextSpan first, TextSpan last) =>
        new(first.Start, Math.Max(0, last.Start + last.Length - first.Start), first.Line, first.Column);

    private enum TokenKind
    {
        Bad, EndOfFile, EndOfLine, Comment, Identifier, Number, String,
        Dim, If, Then, Else, End, For, To, Down, Do, Loop, Until, Print,
        Get, Key, Clear, Screen, Wait, Milliseconds, Random, From,
        True, False, And, Or, Not, Const, Mod, Exit, Program, As,
        NumberType, BooleanType, TextType, Option, Explicit, Sub, Function,
        Call, Return, Select, Case, ByVal, ByRef, Optional, BuiltInConstant, BuiltInFunction, UnsupportedKeyword,
        Plus, Minus, Star, Slash, Equals, NotEquals, Less, LessOrEquals,
        Greater, GreaterOrEquals, OpenParenthesis, CloseParenthesis,
        OpenBracket, CloseBracket, Semicolon, Comma, ColonEquals
    }

    private sealed record Token(TokenKind Kind, string Text, object? Value, TextSpan Span);

    private sealed class Lexer
    {
        private static readonly Dictionary<string, TokenKind> CoreKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dim"] = TokenKind.Dim, ["If"] = TokenKind.If, ["Then"] = TokenKind.Then,
            ["Else"] = TokenKind.Else, ["End"] = TokenKind.End, ["For"] = TokenKind.For,
            ["To"] = TokenKind.To, ["Down"] = TokenKind.Down, ["Do"] = TokenKind.Do,
            ["Loop"] = TokenKind.Loop, ["Until"] = TokenKind.Until, ["Print"] = TokenKind.Print,
            ["Get"] = TokenKind.Get, ["Key"] = TokenKind.Key,
            ["Clear"] = TokenKind.Clear, ["Screen"] = TokenKind.Screen,
            ["Wait"] = TokenKind.Wait, ["Milliseconds"] = TokenKind.Milliseconds,
            ["Random"] = TokenKind.Random, ["From"] = TokenKind.From,
            ["True"] = TokenKind.True, ["False"] = TokenKind.False, ["And"] = TokenKind.And,
            ["Or"] = TokenKind.Or, ["Not"] = TokenKind.Not, ["Const"] = TokenKind.Const,
            ["Mod"] = TokenKind.Mod, ["Exit"] = TokenKind.Exit, ["Program"] = TokenKind.Program,
            ["As"] = TokenKind.As, ["Number"] = TokenKind.NumberType,
            ["Boolean"] = TokenKind.BooleanType, ["Text"] = TokenKind.TextType,
            ["Option"] = TokenKind.Option, ["Explicit"] = TokenKind.Explicit,
            ["Sub"] = TokenKind.Sub, ["Function"] = TokenKind.Function,
            ["Call"] = TokenKind.Call, ["Return"] = TokenKind.Return,
            ["Select"] = TokenKind.Select, ["Case"] = TokenKind.Case,
            ["ByVal"] = TokenKind.ByVal, ["ByRef"] = TokenKind.ByRef,
            ["Optional"] = TokenKind.Optional,
            ["Timer"] = TokenKind.BuiltInFunction, ["Abs"] = TokenKind.BuiltInFunction,
            ["Min"] = TokenKind.BuiltInFunction, ["Max"] = TokenKind.BuiltInFunction
        };

        private static readonly Dictionary<string, long> KeyConstants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["KEY_NONE"] = 0,
            ["KEY_W"] = 1,
            ["KEY_A"] = 2,
            ["KEY_S"] = 3,
            ["KEY_D"] = 4,
            ["KEY_UP"] = 10,
            ["KEY_DOWN"] = 11,
            ["KEY_LEFT"] = 12,
            ["KEY_RIGHT"] = 13,
            ["KEY_ENTER"] = 14,
            ["KEY_ESCAPE"] = 15,
            ["KEY_SPACE"] = 16,
            ["KEY_1"] = 17,
            ["KEY_2"] = 18,
            ["KEY_OTHER"] = 19,
            ["KEY_3"] = 20,
            ["KEY_TAB"] = 21,
            ["KEY_4"] = 22
        };

        private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Dim", "If", "Then", "Else", "End", "With", "For", "To", "Down", "Do", "Loop", "Until", "Print", "Get", "Key", "Clear", "Screen", "Wait", "Milliseconds", "Random", "From", "True", "False", "And", "Or", "Not", "Const", "Mod", "Sub", "Call", "Function", "Return", "Select", "Case", "Exit", "Program", "Timer", "Rgb", "Abs", "Min", "Max", "Game_Closed", "Key_Held", "Pointer_X", "Pointer_Y", "Pointer_Delta_X", "Pointer_Delta_Y", "Pointer_Wheel_Delta", "Pointer_Inside", "Pointer_Held", "Pointer_Pressed", "Pointer_Released", "Image_Width", "Image_Height", "Image_Loaded", "Text_Width", "Text_Height", "Text_Length", "Text_Code_At", "Text_Slice", "Renderer3D", "Renderer3DImage", "Renderer3DText", "Game", "Window", "Size", "By", "Fill", "Draw", "Rectangle", "Rounded", "Circle", "Arc", "Quadrilateral", "Line", "Text", "Number", "At", "Color", "Centered", "Show", "Play", "Sound", "Music", "Pause", "Resume", "Volume", "Stop", "Load", "File", "Into", "Count", "Save", "Default", "Module", "Import", "As", "Public", "Private", "Option", "Explicit", "Boolean", "ByRef", "ByVal", "Type", "Enum", "Property", "Set", "Me", "Optional", "Class", "New", "Nothing", "Is", "Image", "Unload", "Clip", "Data", "Opacity", "Anchor", "Flip", "Horizontal", "Vertical", "Both", "Filter", "Smooth", "Pixel", "On", "Channel", "NONE", "W", "A", "S", "D", "UP", "LEFT", "RIGHT", "KEY_NONE", "KEY_W", "KEY_A", "KEY_S", "KEY_D", "KEY_UP", "KEY_DOWN", "KEY_LEFT", "KEY_RIGHT", "KEY_ENTER", "KEY_ESCAPE", "KEY_SPACE", "KEY_1", "KEY_2", "KEY_3", "KEY_4", "KEY_TAB", "KEY_OTHER", "KEY_PAD_A", "KEY_PAD_B", "KEY_PAD_X", "KEY_PAD_Y", "POINTER_PRIMARY", "POINTER_SECONDARY", "POINTER_MIDDLE", "BLACK", "WHITE", "RED", "GREEN", "BLUE", "CYAN", "MAGENTA", "YELLOW", "ORANGE", "GRAY", "DARK_RED", "DARK_GREEN", "DARK_BLUE", "DARK_GRAY", "LIGHT_RED", "LIGHT_GREEN", "LIGHT_BLUE", "LIGHT_GRAY", "SOUND_CHANNEL_COUNT", "DATA_BLOCK_MAX_BYTES"
        };

        private readonly string _source;
        private readonly List<Diagnostic> _diagnostics = new();
        private int _position;
        private int _line = 1;
        private int _column = 1;

        public Lexer(string source) => _source = source;

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public IReadOnlyList<Token> LexAll()
        {
            var tokens = new List<Token>();
            while (true)
            {
                Token token = Lex();
                tokens.Add(token);
                if (token.Kind is TokenKind.EndOfFile)
                {
                    return tokens;
                }
            }
        }

        private Token Lex()
        {
            while (Current is ' ' or '\t' or '\f' or '\v')
            {
                Advance();
            }

            int start = _position;
            int line = _line;
            int column = _column;
            if (_position >= _source.Length)
            {
                return Make(TokenKind.EndOfFile, start, line, column);
            }

            if (Current is '\r' or '\n')
            {
                if (Current == '\r' && PeekChar(1) == '\n')
                {
                    Advance();
                }

                AdvanceLine();
                return Make(TokenKind.EndOfLine, start, line, column);
            }

            if (Current == '\'')
            {
                Advance();
                int payloadStart = _position;
                while (_position < _source.Length && Current is not '\r' and not '\n')
                {
                    Advance();
                }

                string payload = _source[payloadStart.._position];
                return Make(TokenKind.Comment, start, line, column, payload);
            }

            if (char.IsLetter(Current) || Current == '_')
            {
                Advance();
                while (_position < _source.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
                {
                    Advance();
                }

                string text = _source[start.._position];
                if (KeyConstants.TryGetValue(text, out long keyValue))
                {
                    return Make(TokenKind.BuiltInConstant, start, line, column, keyValue);
                }

                TokenKind kind = CoreKeywords.TryGetValue(text, out TokenKind coreKind)
                    ? coreKind
                    : ReservedWords.Contains(text) ? TokenKind.UnsupportedKeyword : TokenKind.Identifier;
                return Make(kind, start, line, column);
            }

            if (Current is >= '0' and <= '9')
            {
                Advance();
                while (_position < _source.Length && Current is >= '0' and <= '9')
                {
                    Advance();
                }

                string text = _source[start.._position];
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    _diagnostics.Add(new Diagnostic(
                        "SMILE2006",
                        DiagnosticSeverity.Error,
                        "Number literal is outside the signed 64-bit range.",
                        new TextSpan(start, _position - start, line, column)));
                }

                return Make(TokenKind.Number, start, line, column);
            }

            if (Current == '"')
            {
                Advance();
                var value = new StringBuilder();
                bool terminated = false;
                while (_position < _source.Length)
                {
                    if (Current == '"')
                    {
                        if (PeekChar(1) == '"')
                        {
                            value.Append('"');
                            Advance();
                            Advance();
                            continue;
                        }

                        Advance();
                        terminated = true;
                        break;
                    }

                    if (Current == '\r')
                    {
                        if (PeekChar(1) == '\n')
                        {
                            Advance();
                        }

                        AdvanceLine();
                        value.Append('\n');
                    }
                    else if (Current == '\n')
                    {
                        AdvanceLine();
                        value.Append('\n');
                    }
                    else
                    {
                        value.Append(Current);
                        Advance();
                    }
                }

                if (!terminated)
                {
                    _diagnostics.Add(new Diagnostic(
                        "SMILE2007",
                        DiagnosticSeverity.Error,
                        "Unterminated Text literal.",
                        new TextSpan(start, _position - start, line, column)));
                }

                return Make(TokenKind.String, start, line, column, value.ToString());
            }

            TokenKind single = Current switch
            {
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '=' => TokenKind.Equals,
                '(' => TokenKind.OpenParenthesis,
                ')' => TokenKind.CloseParenthesis,
                '[' => TokenKind.OpenBracket,
                ']' => TokenKind.CloseBracket,
                ';' => TokenKind.Semicolon,
                ',' => TokenKind.Comma,
                ':' when PeekChar(1) == '=' => TokenKind.ColonEquals,
                '<' when PeekChar(1) == '=' => TokenKind.LessOrEquals,
                '<' when PeekChar(1) == '>' => TokenKind.NotEquals,
                '<' => TokenKind.Less,
                '>' when PeekChar(1) == '=' => TokenKind.GreaterOrEquals,
                '>' => TokenKind.Greater,
                _ => TokenKind.Bad
            };
            Advance();
            if (single is TokenKind.ColonEquals or TokenKind.LessOrEquals or TokenKind.NotEquals or TokenKind.GreaterOrEquals)
            {
                Advance();
            }

            Token result = Make(single, start, line, column);
            if (single is TokenKind.Bad)
            {
                _diagnostics.Add(new Diagnostic(
                    "SMILE2008",
                    DiagnosticSeverity.Error,
                    $"Unexpected character '{result.Text}'.",
                    result.Span));
            }

            return result;
        }

        private char Current => _position < _source.Length ? _source[_position] : '\0';

        private char PeekChar(int offset) => _position + offset < _source.Length ? _source[_position + offset] : '\0';

        private void Advance()
        {
            _position++;
            _column++;
        }

        private void AdvanceLine()
        {
            _position++;
            _line++;
            _column = 1;
        }

        private Token Make(TokenKind kind, int start, int line, int column, object? value = null) =>
            new(kind, _source[start.._position], value, new TextSpan(start, _position - start, line, column));
    }
}
