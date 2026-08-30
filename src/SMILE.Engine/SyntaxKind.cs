namespace SMILE.Engine;

public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    IdentifierToken,
    StringLiteralToken,
    IntegerLiteralToken,

    NotKeyword,
    AndKeyword,
    OrKeyword,
    ModKeyword,

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
    CloseParenthesisToken
}
