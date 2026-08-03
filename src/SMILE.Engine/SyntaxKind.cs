namespace SMILE.Engine;

public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    EndOfLineToken,

    IdentifierToken,
    StringLiteralToken,
    IntegerLiteralToken,

    LetKeyword,
    PrintKeyword,
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
