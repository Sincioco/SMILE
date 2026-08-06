namespace SMILE.Engine;

public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    EndOfLineToken,
    FullLineCommentToken,

    IdentifierToken,
    StringLiteralToken,
    BlockStringLiteralToken,
    IntegerLiteralToken,

    LetKeyword,
    SetKeyword,
    InputKeyword,
    PrintKeyword,
    IfKeyword,
    ThenKeyword,
    ElseKeyword,
    EndKeyword,
    TrueKeyword,
    FalseKeyword,
    NotKeyword,
    AndKeyword,
    OrKeyword,

    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,

    EqualsToken,
    NotEqualsToken,
    LessToken,
    LessOrEqualsToken,
    GreaterToken,
    GreaterOrEqualsToken,

    OpenParenthesisToken,
    CloseParenthesisToken,
    InterpolatedStringStartToken
}
